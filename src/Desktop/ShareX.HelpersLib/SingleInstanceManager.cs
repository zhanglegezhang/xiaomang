#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShareX.HelpersLib
{
    public class SingleInstanceManager : IDisposable
    {
        public event Action<string[]> ArgumentsReceived;

        public string MutexName { get; private set; }
        public string PipeName { get; private set; }
        public bool IsSingleInstance { get; private set; }
        public bool IsFirstInstance { get; private set; }
        public bool ArgumentsForwarded { get; private set; }

        private const int MaxArgumentsLength = 100;
        private const int ConnectTimeout = 5000;
        internal const int MaxArgumentBytes = 64 * 1024;
        internal const int MaxMessageBytes = 256 * 1024;
        private const int MessageTimeout = 3000;

        private readonly MutexManager mutex;
        private CancellationTokenSource cts;
        private Task listener;
        private int disposed;

        public SingleInstanceManager(string mutexName, string pipeName, string[] args) : this(mutexName, pipeName, true, args)
        {
        }

        public SingleInstanceManager(string mutexName, string pipeName, bool isSingleInstance, string[] args)
        {
            MutexName = mutexName;
            PipeName = pipeName;
            IsSingleInstance = isSingleInstance;

            mutex = new MutexManager(MutexName, 0);
            IsFirstInstance = mutex.HasHandle;

            if (IsSingleInstance)
            {
                if (IsFirstInstance)
                {
                    cts = new CancellationTokenSource();

                    listener = Task.Run(ListenForConnectionsAsync);
                }
                else
                {
                    ArgumentsForwarded = RedirectArgumentsToFirstInstance(args);
                }
            }
        }

        protected virtual void OnArgumentsReceived(string[] arguments)
        {
            if (ArgumentsReceived != null)
            {
                Task.Run(() => ArgumentsReceived?.Invoke(arguments));
            }
        }

        private async Task ListenForConnectionsAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                bool namedPipeServerCreated = false;

                try
                {
                    PipeSecurity pipeSecurity = new PipeSecurity();

                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        pipeSecurity.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    }

                    using (NamedPipeServerStream namedPipeServer = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity))
                    {
                        namedPipeServerCreated = true;

                        await namedPipeServer.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);

                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                        {
                            deadline.CancelAfter(MessageTimeout);
                            string[] args = await ReadArgumentsAsync(namedPipeServer, deadline.Token).ConfigureAwait(false);
                            OnArgumentsReceived(args);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    DebugHelper.WriteException(e);

                    if (!namedPipeServerCreated)
                    {
                        break;
                    }
                }
            }
        }

        // Keep the BinaryWriter wire format for already-running older instances,
        // but validate lengths before allocating strings and bound the whole frame.
        internal static async Task<string[]> ReadArgumentsAsync(Stream stream, CancellationToken token)
        {
            byte[] number = new byte[4];
            await stream.ReadExactlyAsync(number, token).ConfigureAwait(false);
            int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(number);
            if (count < 0 || count > MaxArgumentsLength) throw new InvalidDataException("Invalid argument count.");
            int budget = MaxMessageBytes - 4;
            var args = new string[count];
            byte[] prefix = new byte[1];
            var utf8 = new UTF8Encoding(false, true);
            for (int i = 0; i < count; i++)
            {
                uint length = 0;
                for (int shift = 0; ; shift += 7)
                {
                    if (--budget < 0 || shift > 28) throw new InvalidDataException("Invalid string length.");
                    await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
                    byte part = prefix[0];
                    if (shift == 28 && part > 7) throw new InvalidDataException("Invalid string length.");
                    length |= (uint)(part & 127) << shift;
                    if ((part & 128) == 0) break;
                }
                if (length > MaxArgumentBytes || length > budget) throw new InvalidDataException("Argument message exceeds limit.");
                budget -= (int)length;
                byte[] bytes = new byte[(int)length];
                await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                args[i] = utf8.GetString(bytes);
            }
            return args;
        }

        private bool RedirectArgumentsToFirstInstance(string[] args)
        {
            try
            {
                using var payload = new MemoryStream();
                if (args == null || args.Length > MaxArgumentsLength) throw new InvalidDataException("Invalid argument count.");
                using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
                {
                    writer.Write(args.Length);
                    foreach (string argument in args)
                    {
                        if (argument == null || Encoding.UTF8.GetByteCount(argument) > MaxArgumentBytes) throw new InvalidDataException("Argument exceeds limit.");
                        writer.Write(argument);
                        if (payload.Length > MaxMessageBytes) throw new InvalidDataException("Argument message exceeds limit.");
                    }
                }
                using var namedPipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                using var deadline = new CancellationTokenSource(ConnectTimeout);
                namedPipeClient.ConnectAsync(deadline.Token).GetAwaiter().GetResult();
                namedPipeClient.WriteAsync(payload.ToArray(), deadline.Token).AsTask().GetAwaiter().GetResult();
                return true;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e);
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (cts != null)
            {
                cts.Cancel();
                // The async pipe reads are cancellable, so shutdown cannot retain
                // an occupied listener or race a disposed cancellation source.
                listener?.GetAwaiter().GetResult();
                cts.Dispose();
            }

            mutex?.Dispose();
        }
    }
}
