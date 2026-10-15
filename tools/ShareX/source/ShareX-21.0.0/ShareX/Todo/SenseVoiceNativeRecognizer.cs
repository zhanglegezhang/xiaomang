using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SherpaOnnx;

namespace ShareX
{
    // sherpa-onnx 1.13.8's C API requires UTF-8, but its managed configuration
    // uses LPStr (the Windows ANSI code page). Keep the package's ABI layout,
    // replacing string pointers only for the duration of native initialization.
    internal sealed class SenseVoiceNativeRecognizer : IDisposable
    {
        private const string Library = "sherpa-onnx-c-api";
        private readonly RecognizerHandle handle;

        public SenseVoiceNativeRecognizer(OfflineRecognizerConfig config)
        {
            using Utf8Configuration native = new Utf8Configuration(config);
            handle = new RecognizerHandle(CreateRecognizer(native.Pointer));
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new InvalidOperationException("离线识别器初始化失败，请检查模型、词表及运行库。");
            }
        }

        public RecognitionStream CreateStream()
        {
            StreamHandle stream = new StreamHandle(CreateNativeStream(handle));
            if (stream.IsInvalid)
            {
                stream.Dispose();
                throw new InvalidOperationException("离线识别流创建失败。");
            }
            return new RecognitionStream(stream);
        }

        public void Decode(RecognitionStream stream) => DecodeNativeStream(handle, stream.Handle);
        public void Dispose() => handle.Dispose();

        internal sealed class RecognitionStream : IDisposable
        {
            internal readonly StreamHandle Handle;
            internal RecognitionStream(StreamHandle handle) => Handle = handle;
            public void AcceptWaveform(int sampleRate, float[] samples) =>
                AcceptNativeWaveform(Handle, sampleRate, samples, samples.Length);
            public string ResultText
            {
                get
                {
                    IntPtr result = GetNativeResult(Handle);
                    if (result == IntPtr.Zero)
                        throw new InvalidOperationException("离线识别未返回有效结果。");
                    try
                    {
                        // The first field of SherpaOnnxOfflineRecognizerResult is
                        // const char *text. An empty string is valid; a null pointer is not.
                        IntPtr text = Marshal.ReadIntPtr(result);
                        if (text == IntPtr.Zero)
                            throw new InvalidOperationException("离线识别结果缺少文字字段。");
                        return Marshal.PtrToStringUTF8(text) ?? "";
                    }
                    finally { DestroyNativeResult(result); }
                }
            }
            public void Dispose() => Handle.Dispose();
        }

        internal sealed class StreamHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal StreamHandle(IntPtr pointer) : base(true) => SetHandle(pointer);
            protected override bool ReleaseHandle() { DestroyNativeStream(handle); return true; }
        }

        private sealed class RecognizerHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal RecognizerHandle(IntPtr pointer) : base(true) => SetHandle(pointer);
            protected override bool ReleaseHandle() { DestroyRecognizer(handle); return true; }
        }

        private sealed class Utf8Configuration : IDisposable
        {
            private readonly List<(IntPtr Slot, IntPtr Original, IntPtr Utf8)> strings = new();
            public IntPtr Pointer { get; private set; }
            public Utf8Configuration(OfflineRecognizerConfig config)
            {
                Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<OfflineRecognizerConfig>());
                try { Marshal.StructureToPtr(config, Pointer, false); }
                catch { Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; throw; }
                try { ReplaceStrings(config, typeof(OfflineRecognizerConfig), Pointer); }
                catch { Dispose(); throw; }
            }

            private void ReplaceStrings(object value, Type type, IntPtr address)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    IntPtr slot = IntPtr.Add(address, checked((int)Marshal.OffsetOf(type, field.Name)));
                    object fieldValue = field.GetValue(value);
                    if (field.FieldType == typeof(string) && fieldValue is string text)
                    {
                        IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(text);
                        strings.Add((slot, Marshal.ReadIntPtr(slot), utf8));
                        Marshal.WriteIntPtr(slot, utf8);
                    }
                    else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                        ReplaceStrings(fieldValue, field.FieldType, slot);
                }
            }

            public void Dispose()
            {
                if (Pointer == IntPtr.Zero) return;
                // Restore marshaler-owned pointers before DestroyStructure: UTF-8
                // allocations have separate ownership and must not be freed twice.
                foreach (var item in strings)
                {
                    Marshal.WriteIntPtr(item.Slot, item.Original);
                    Marshal.FreeCoTaskMem(item.Utf8);
                }
                Marshal.DestroyStructure<OfflineRecognizerConfig>(Pointer);
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }

        [DllImport(Library, EntryPoint = "SherpaOnnxCreateOfflineRecognizer", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateRecognizer(IntPtr config);
        [DllImport(Library, EntryPoint = "SherpaOnnxDestroyOfflineRecognizer", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DestroyRecognizer(IntPtr recognizer);
        [DllImport(Library, EntryPoint = "SherpaOnnxCreateOfflineStream", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateNativeStream(RecognizerHandle recognizer);
        [DllImport(Library, EntryPoint = "SherpaOnnxDestroyOfflineStream", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DestroyNativeStream(IntPtr stream);
        [DllImport(Library, EntryPoint = "SherpaOnnxAcceptWaveformOffline", CallingConvention = CallingConvention.Cdecl)]
        private static extern void AcceptNativeWaveform(StreamHandle stream, int sampleRate, float[] samples, int count);
        [DllImport(Library, EntryPoint = "SherpaOnnxDecodeOfflineStream", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DecodeNativeStream(RecognizerHandle recognizer, StreamHandle stream);
        [DllImport(Library, EntryPoint = "SherpaOnnxGetOfflineStreamResult", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetNativeResult(StreamHandle stream);
        [DllImport(Library, EntryPoint = "SherpaOnnxDestroyOfflineRecognizerResult", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DestroyNativeResult(IntPtr result);
    }
}
