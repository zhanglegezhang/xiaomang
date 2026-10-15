using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace ShareX
{
    internal enum FirstUseValidationResult { Authorized, InvalidPassword, StorageFailure }

    internal sealed class FirstUseAccessStore
    {
        private const string RecordIdentity = "xiaomangtongxue.first-use.v1";
        private const int PasswordIterations = 210000;
        private static readonly byte[] PasswordSalt = Convert.FromHexString("025042BAAAA79237BE492FB5C4FFAEB0");
        private static readonly byte[] PasswordHash = Convert.FromHexString("CDD95A6813A7D5F77675457E1458C6DE6F8583793C55CCA6DD536BE3A05A4EC5");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(RecordIdentity);
        private readonly byte[] expectedPayload;

        internal static string DefaultRecordPath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData) || !Path.IsPathFullyQualified(localAppData))
                    throw new DirectoryNotFoundException("Current user's local data folder is unavailable.");
                return Path.Combine(localAppData, "xiaomangtongxue", "Access", "first-use.bin");
            }
        }

        internal string RecordPath { get; }

        internal FirstUseAccessStore(string recordPath = null, string machineName = null, string userId = null)
        {
            RecordPath = recordPath ?? DefaultRecordPath;
            expectedPayload = Encoding.UTF8.GetBytes(RecordIdentity + "\n" +
                (machineName ?? Environment.MachineName) + "\n" + (userId ?? GetCurrentUserId()));
        }

        private static string GetCurrentUserId()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return identity.User.Value;
        }

        internal bool HasAccess()
        {
            try
            {
                using FileStream stream = new FileStream(RecordPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length <= 0 || stream.Length > 16384) return false;
                byte[] encrypted = new byte[(int)stream.Length];
                stream.ReadExactly(encrypted);
                byte[] payload = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return CryptographicOperations.FixedTimeEquals(payload, expectedPayload);
            }
            catch (Exception error) when (IsStorageError(error))
            {
                // Missing, unreadable or damaged records require password verification again.
                return false;
            }
        }

        internal static bool VerifyPassword(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length > 256) return false;
            byte[] candidate = Rfc2898DeriveBytes.Pbkdf2(password, PasswordSalt, PasswordIterations,
                HashAlgorithmName.SHA256, PasswordHash.Length);
            try { return CryptographicOperations.FixedTimeEquals(candidate, PasswordHash); }
            finally { CryptographicOperations.ZeroMemory(candidate); }
        }

        internal FirstUseValidationResult ValidateAndSave(string password)
        {
            if (!VerifyPassword(password)) return FirstUseValidationResult.InvalidPassword;
            string temporaryPath = RecordPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecordPath));
                byte[] encrypted = ProtectedData.Protect(expectedPayload, Entropy, DataProtectionScope.CurrentUser);
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(encrypted);
                    stream.Flush(true);
                }
                File.Move(temporaryPath, RecordPath, true);
                if (!HasAccess()) return FirstUseValidationResult.StorageFailure;
                return FirstUseValidationResult.Authorized;
            }
            catch (Exception error) when (IsStorageError(error))
            {
                return FirstUseValidationResult.StorageFailure;
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (Exception error) when (IsStorageError(error)) { }
            }
        }

        private static bool IsStorageError(Exception error) =>
            error is IOException || error is UnauthorizedAccessException ||
            error is CryptographicException || error is SecurityException;
    }
}
