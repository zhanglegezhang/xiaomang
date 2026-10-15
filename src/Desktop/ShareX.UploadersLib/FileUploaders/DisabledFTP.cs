using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ShareX.UploadersLib.Properties;

namespace ShareX.UploadersLib.FileUploaders
{
    // FTP, FTPS and SFTP are deliberately unavailable in the local-only build. These
    // compatibility types keep old settings readable without shipping either network stack.
    public sealed class FTPFileUploaderService : FileUploaderService
    {
        public override FileDestination EnumValue { get; } = FileDestination.FTP;
        public override Image ServiceImage => Resources.folder_network;
        public override bool CheckConfig(UploadersConfig config) => false;
        public override GenericUploader CreateUploader(UploadersConfig config, TaskReferenceHelper taskInfo) => null;
        public override TabPage GetUploadersConfigTabPage(UploadersConfigForm form) => form.tpFTP;
    }

    public sealed class FTP : FileUploader, IDisposable
    {
        public FTPAccount Account { get; }
        public bool IsConnected => false;

        public FTP(FTPAccount account) => Account = account;
        public override UploadResult Upload(Stream stream, string fileName) =>
            throw new NotSupportedException("FTP upload is not available in 小蟒.");
        public override void StopUpload() { }
        public bool Connect() => false;
        public bool DirectoryExists(string remotePath) => false;
        public List<string> CreateMultiDirectory(string remotePath) => new List<string>();
        public void Dispose() { }
    }

    public sealed class SFTP : FileUploader, IDisposable
    {
        public FTPAccount Account { get; }
        public bool IsValidAccount => false;
        public bool IsConnected => false;

        public SFTP(FTPAccount account) => Account = account;
        public override UploadResult Upload(Stream stream, string fileName) =>
            throw new NotSupportedException("SFTP upload is not available in 小蟒.");
        public override void StopUpload() { }
        public bool Connect() => false;
        public bool DirectoryExists(string path) => false;
        public List<string> CreateMultiDirectory(string path) => new List<string>();
        public void Dispose() { }
    }
}
