namespace ShareX
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            scMain = new ShareX.HelpersLib.SplitContainerCustomSplitter();
            lvUploads = new ShareX.HelpersLib.MyListView();
            chFilename = new System.Windows.Forms.ColumnHeader();
            chStatus = new System.Windows.Forms.ColumnHeader();
            chProgress = new System.Windows.Forms.ColumnHeader();
            chSpeed = new System.Windows.Forms.ColumnHeader();
            chElapsed = new System.Windows.Forms.ColumnHeader();
            chRemaining = new System.Windows.Forms.ColumnHeader();
            chURL = new System.Windows.Forms.ColumnHeader();
            pbPreview = new ShareX.HelpersLib.MyPictureBox();
            tsMain = new ShareX.HelpersLib.ToolStripBorderRight();
            tsbApplicationSettings = new System.Windows.Forms.ToolStripButton();
            tsbHotkeySettings = new System.Windows.Forms.ToolStripButton();
            tsbScreenshotsFolder = new System.Windows.Forms.ToolStripButton();
            tsbAbout = new System.Windows.Forms.ToolStripButton();
            cmsTaskInfo = new System.Windows.Forms.ContextMenuStrip(components);
            tsmiShowErrors = new System.Windows.Forms.ToolStripMenuItem();
            tsmiStopUpload = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpen = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpenURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpenShortenedURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpenThumbnailURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpenDeletionURL = new System.Windows.Forms.ToolStripMenuItem();
            tssOpen1 = new System.Windows.Forms.ToolStripSeparator();
            tsmiOpenFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpenFolder = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOpenThumbnailFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopy = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyShortenedURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyThumbnailURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyDeletionURL = new System.Windows.Forms.ToolStripMenuItem();
            tssCopy1 = new System.Windows.Forms.ToolStripSeparator();
            tsmiCopyFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyImageDimensions = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyText = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyThumbnailFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyThumbnailImage = new System.Windows.Forms.ToolStripMenuItem();
            tssCopy2 = new System.Windows.Forms.ToolStripSeparator();
            tsmiCopyHTMLLink = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyHTMLImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyHTMLLinkedImage = new System.Windows.Forms.ToolStripMenuItem();
            tssCopy3 = new System.Windows.Forms.ToolStripSeparator();
            tsmiCopyForumLink = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyForumImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyForumLinkedImage = new System.Windows.Forms.ToolStripMenuItem();
            tssCopy4 = new System.Windows.Forms.ToolStripSeparator();
            tsmiCopyMarkdownLink = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyMarkdownImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyMarkdownLinkedImage = new System.Windows.Forms.ToolStripMenuItem();
            tssCopy5 = new System.Windows.Forms.ToolStripSeparator();
            tsmiCopyFilePath = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyFileName = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyFileNameWithExtension = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCopyFolder = new System.Windows.Forms.ToolStripMenuItem();
            tssCopy6 = new System.Windows.Forms.ToolStripSeparator();
            tsmiUploadSelectedFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiDownloadSelectedURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiEditSelectedFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiBeautifyImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiAddImageEffects = new System.Windows.Forms.ToolStripMenuItem();
            tsmiPinSelectedFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiRunAction = new System.Windows.Forms.ToolStripMenuItem();
            tsmiDeleteSelectedItem = new System.Windows.Forms.ToolStripMenuItem();
            tsmiDeleteSelectedFile = new System.Windows.Forms.ToolStripMenuItem();
            tsmiShortenSelectedURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiShareSelectedURL = new System.Windows.Forms.ToolStripMenuItem();
            tsmiAnalyzeImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiGoogleLens = new System.Windows.Forms.ToolStripMenuItem();
            tsmiBingVisualSearch = new System.Windows.Forms.ToolStripMenuItem();
            tsmiShowQRCode = new System.Windows.Forms.ToolStripMenuItem();
            tsmiOCRImage = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCombineImages = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCombineImagesHorizontally = new System.Windows.Forms.ToolStripMenuItem();
            tsmiCombineImagesVertically = new System.Windows.Forms.ToolStripMenuItem();
            tsmiShowResponse = new System.Windows.Forms.ToolStripMenuItem();
            tsmiClearList = new System.Windows.Forms.ToolStripMenuItem();
            tssUploadInfo1 = new System.Windows.Forms.ToolStripSeparator();
            tsmiSwitchTaskViewMode = new System.Windows.Forms.ToolStripMenuItem();
            niTray = new System.Windows.Forms.NotifyIcon(components);
            cmsTray = new System.Windows.Forms.ContextMenuStrip(components);
            tsmiTrayToggleHotkeys = new System.Windows.Forms.ToolStripMenuItem();
            tsmiTrayExit = new System.Windows.Forms.ToolStripMenuItem();
            ttMain = new System.Windows.Forms.ToolTip(components);
            pToolbars = new System.Windows.Forms.Panel();
            dgvHotkeys = new System.Windows.Forms.DataGridView();
            cHotkeyStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cHotkey = new System.Windows.Forms.DataGridViewTextBoxColumn();
            cDescription = new System.Windows.Forms.DataGridViewTextBoxColumn();
            pMain = new System.Windows.Forms.Panel();
            pHotkeys = new System.Windows.Forms.Panel();
            ucTaskThumbnailView = new TaskThumbnailView();
            ((System.ComponentModel.ISupportInitialize)scMain).BeginInit();
            scMain.Panel1.SuspendLayout();
            scMain.Panel2.SuspendLayout();
            scMain.SuspendLayout();
            tsMain.SuspendLayout();
            cmsTaskInfo.SuspendLayout();
            cmsTray.SuspendLayout();
            pToolbars.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dgvHotkeys).BeginInit();
            pMain.SuspendLayout();
            pHotkeys.SuspendLayout();
            SuspendLayout();
            // 
            // scMain
            // 
            resources.ApplyResources(scMain, "scMain");
            scMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            scMain.Name = "scMain";
            // 
            // scMain.Panel1
            // 
            scMain.Panel1.Controls.Add(lvUploads);
            // 
            // scMain.Panel2
            // 
            scMain.Panel2.Controls.Add(pbPreview);
            scMain.SplitterColor = System.Drawing.Color.White;
            scMain.SplitterLineColor = System.Drawing.Color.FromArgb(189, 189, 189);
            scMain.SplitterMoved += scMain_SplitterMoved;
            // 
            // lvUploads
            // 
            lvUploads.AutoFillColumn = true;
            lvUploads.BorderStyle = System.Windows.Forms.BorderStyle.None;
            lvUploads.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {chFilename, chStatus, chProgress, chSpeed, chElapsed, chRemaining, chURL});
            resources.ApplyResources(lvUploads, "lvUploads");
            lvUploads.FullRowSelect = true;
            lvUploads.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
            lvUploads.Name = "lvUploads";
            lvUploads.ShowItemToolTips = true;
            lvUploads.UseCompatibleStateImageBehavior = false;
            lvUploads.View = System.Windows.Forms.View.Details;
            lvUploads.ColumnWidthChanged += lvUploads_ColumnWidthChanged;
            lvUploads.ItemDrag += lvUploads_ItemDrag;
            lvUploads.SelectedIndexChanged += lvUploads_SelectedIndexChanged;
            lvUploads.KeyDown += lvUploads_KeyDown;
            lvUploads.MouseDoubleClick += lvUploads_MouseDoubleClick;
            lvUploads.MouseUp += lvUploads_MouseUp;
            // 
            // chFilename
            // 
            resources.ApplyResources(chFilename, "chFilename");
            // 
            // chStatus
            // 
            resources.ApplyResources(chStatus, "chStatus");
            // 
            // chProgress
            // 
            resources.ApplyResources(chProgress, "chProgress");
            // 
            // chSpeed
            // 
            resources.ApplyResources(chSpeed, "chSpeed");
            // 
            // chElapsed
            // 
            resources.ApplyResources(chElapsed, "chElapsed");
            // 
            // chRemaining
            // 
            resources.ApplyResources(chRemaining, "chRemaining");
            // 
            // chURL
            // 
            resources.ApplyResources(chURL, "chURL");
            // 
            // pbPreview
            // 
            pbPreview.BackColor = System.Drawing.SystemColors.Window;
            pbPreview.Cursor = System.Windows.Forms.Cursors.Hand;
            resources.ApplyResources(pbPreview, "pbPreview");
            pbPreview.DrawCheckeredBackground = true;
            pbPreview.EnableRightClickMenu = true;
            pbPreview.Name = "pbPreview";
            pbPreview.PictureBoxBackColor = System.Drawing.SystemColors.Control;
            pbPreview.ShowImageSizeLabel = true;
            pbPreview.MouseDown += pbPreview_MouseDown;
            // 
            // tsMain
            // 
            tsMain.CanOverflow = false;
            resources.ApplyResources(tsMain, "tsMain");
            tsMain.DrawCustomBorder = true;
            tsMain.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            tsMain.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {tsbApplicationSettings, tsbHotkeySettings, tsbScreenshotsFolder, tsbAbout});
            tsMain.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.VerticalStackWithOverflow;
            tsMain.Name = "tsMain";
            tsMain.ShowItemToolTips = false;
            tsMain.TabStop = true;
            // 
            // tsbApplicationSettings
            // 
            tsbApplicationSettings.Image = Properties.Resources.wrench_screwdriver;
            resources.ApplyResources(tsbApplicationSettings, "tsbApplicationSettings");
            tsbApplicationSettings.Name = "tsbApplicationSettings";
            tsbApplicationSettings.Click += tsbApplicationSettings_Click;
            // 
            // tsbHotkeySettings
            // 
            tsbHotkeySettings.Image = Properties.Resources.keyboard;
            resources.ApplyResources(tsbHotkeySettings, "tsbHotkeySettings");
            tsbHotkeySettings.Name = "tsbHotkeySettings";
            tsbHotkeySettings.Click += tsbHotkeySettings_Click;
            // 
            // tsbScreenshotsFolder
            // 
            tsbScreenshotsFolder.Image = Properties.Resources.folder_open_image;
            resources.ApplyResources(tsbScreenshotsFolder, "tsbScreenshotsFolder");
            tsbScreenshotsFolder.Name = "tsbScreenshotsFolder";
            tsbScreenshotsFolder.Click += tsbScreenshotsFolder_Click;
            // 
            // tsbAbout
            // 
            tsbAbout.Image = Properties.Resources.crown;
            resources.ApplyResources(tsbAbout, "tsbAbout");
            tsbAbout.Name = "tsbAbout";
            tsbAbout.Click += tsbAbout_Click;
            // 
            // cmsTaskInfo
            // 
            cmsTaskInfo.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {tsmiShowErrors, tsmiStopUpload, tsmiOpen, tsmiCopy, tsmiUploadSelectedFile, tsmiDownloadSelectedURL, tsmiEditSelectedFile, tsmiBeautifyImage, tsmiAddImageEffects, tsmiPinSelectedFile, tsmiRunAction, tsmiDeleteSelectedItem, tsmiDeleteSelectedFile, tsmiShortenSelectedURL, tsmiShareSelectedURL, tsmiAnalyzeImage, tsmiGoogleLens, tsmiBingVisualSearch, tsmiShowQRCode, tsmiOCRImage, tsmiCombineImages, tsmiShowResponse, tsmiClearList, tssUploadInfo1, tsmiSwitchTaskViewMode});
            cmsTaskInfo.Name = "cmsHistory";
            resources.ApplyResources(cmsTaskInfo, "cmsTaskInfo");
            cmsTaskInfo.Closing += cmsTaskInfo_Closing;
            cmsTaskInfo.PreviewKeyDown += cmsTaskInfo_PreviewKeyDown;
            // 
            // tsmiShowErrors
            // 
            tsmiShowErrors.Image = Properties.Resources.exclamation_button;
            tsmiShowErrors.Name = "tsmiShowErrors";
            resources.ApplyResources(tsmiShowErrors, "tsmiShowErrors");
            tsmiShowErrors.Click += tsmiShowErrors_Click;
            // 
            // tsmiStopUpload
            // 
            tsmiStopUpload.Image = Properties.Resources.cross_button;
            tsmiStopUpload.Name = "tsmiStopUpload";
            resources.ApplyResources(tsmiStopUpload, "tsmiStopUpload");
            tsmiStopUpload.Click += tsmiStopUpload_Click;
            // 
            // tsmiOpen
            // 
            tsmiOpen.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {tsmiOpenURL, tsmiOpenShortenedURL, tsmiOpenThumbnailURL, tsmiOpenDeletionURL, tssOpen1, tsmiOpenFile, tsmiOpenFolder, tsmiOpenThumbnailFile});
            tsmiOpen.Image = Properties.Resources.folder_open_document;
            tsmiOpen.Name = "tsmiOpen";
            resources.ApplyResources(tsmiOpen, "tsmiOpen");
            // 
            // tsmiOpenURL
            // 
            tsmiOpenURL.Name = "tsmiOpenURL";
            resources.ApplyResources(tsmiOpenURL, "tsmiOpenURL");
            tsmiOpenURL.Click += tsmiOpenURL_Click;
            // 
            // tsmiOpenShortenedURL
            // 
            tsmiOpenShortenedURL.Name = "tsmiOpenShortenedURL";
            resources.ApplyResources(tsmiOpenShortenedURL, "tsmiOpenShortenedURL");
            tsmiOpenShortenedURL.Click += tsmiOpenShortenedURL_Click;
            // 
            // tsmiOpenThumbnailURL
            // 
            tsmiOpenThumbnailURL.Name = "tsmiOpenThumbnailURL";
            resources.ApplyResources(tsmiOpenThumbnailURL, "tsmiOpenThumbnailURL");
            tsmiOpenThumbnailURL.Click += tsmiOpenThumbnailURL_Click;
            // 
            // tsmiOpenDeletionURL
            // 
            tsmiOpenDeletionURL.Name = "tsmiOpenDeletionURL";
            resources.ApplyResources(tsmiOpenDeletionURL, "tsmiOpenDeletionURL");
            tsmiOpenDeletionURL.Click += tsmiOpenDeletionURL_Click;
            // 
            // tssOpen1
            // 
            tssOpen1.Name = "tssOpen1";
            resources.ApplyResources(tssOpen1, "tssOpen1");
            // 
            // tsmiOpenFile
            // 
            tsmiOpenFile.Name = "tsmiOpenFile";
            resources.ApplyResources(tsmiOpenFile, "tsmiOpenFile");
            tsmiOpenFile.Click += tsmiOpenFile_Click;
            // 
            // tsmiOpenFolder
            // 
            tsmiOpenFolder.Name = "tsmiOpenFolder";
            resources.ApplyResources(tsmiOpenFolder, "tsmiOpenFolder");
            tsmiOpenFolder.Click += tsmiOpenFolder_Click;
            // 
            // tsmiOpenThumbnailFile
            // 
            tsmiOpenThumbnailFile.Name = "tsmiOpenThumbnailFile";
            resources.ApplyResources(tsmiOpenThumbnailFile, "tsmiOpenThumbnailFile");
            tsmiOpenThumbnailFile.Click += tsmiOpenThumbnailFile_Click;
            // 
            // tsmiCopy
            // 
            tsmiCopy.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {tsmiCopyURL, tsmiCopyShortenedURL, tsmiCopyThumbnailURL, tsmiCopyDeletionURL, tssCopy1, tsmiCopyFile, tsmiCopyImage, tsmiCopyImageDimensions, tsmiCopyText, tsmiCopyThumbnailFile, tsmiCopyThumbnailImage, tssCopy2, tsmiCopyHTMLLink, tsmiCopyHTMLImage, tsmiCopyHTMLLinkedImage, tssCopy3, tsmiCopyForumLink, tsmiCopyForumImage, tsmiCopyForumLinkedImage, tssCopy4, tsmiCopyMarkdownLink, tsmiCopyMarkdownImage, tsmiCopyMarkdownLinkedImage, tssCopy5, tsmiCopyFilePath, tsmiCopyFileName, tsmiCopyFileNameWithExtension, tsmiCopyFolder, tssCopy6});
            tsmiCopy.Image = Properties.Resources.document_copy;
            tsmiCopy.Name = "tsmiCopy";
            resources.ApplyResources(tsmiCopy, "tsmiCopy");
            // 
            // tsmiCopyURL
            // 
            tsmiCopyURL.Name = "tsmiCopyURL";
            resources.ApplyResources(tsmiCopyURL, "tsmiCopyURL");
            tsmiCopyURL.Click += tsmiCopyURL_Click;
            // 
            // tsmiCopyShortenedURL
            // 
            tsmiCopyShortenedURL.Name = "tsmiCopyShortenedURL";
            resources.ApplyResources(tsmiCopyShortenedURL, "tsmiCopyShortenedURL");
            tsmiCopyShortenedURL.Click += tsmiCopyShortenedURL_Click;
            // 
            // tsmiCopyThumbnailURL
            // 
            tsmiCopyThumbnailURL.Name = "tsmiCopyThumbnailURL";
            resources.ApplyResources(tsmiCopyThumbnailURL, "tsmiCopyThumbnailURL");
            tsmiCopyThumbnailURL.Click += tsmiCopyThumbnailURL_Click;
            // 
            // tsmiCopyDeletionURL
            // 
            tsmiCopyDeletionURL.Name = "tsmiCopyDeletionURL";
            resources.ApplyResources(tsmiCopyDeletionURL, "tsmiCopyDeletionURL");
            tsmiCopyDeletionURL.Click += tsmiCopyDeletionURL_Click;
            // 
            // tssCopy1
            // 
            tssCopy1.Name = "tssCopy1";
            resources.ApplyResources(tssCopy1, "tssCopy1");
            // 
            // tsmiCopyFile
            // 
            tsmiCopyFile.Name = "tsmiCopyFile";
            resources.ApplyResources(tsmiCopyFile, "tsmiCopyFile");
            tsmiCopyFile.Click += tsmiCopyFile_Click;
            // 
            // tsmiCopyImage
            // 
            tsmiCopyImage.Name = "tsmiCopyImage";
            resources.ApplyResources(tsmiCopyImage, "tsmiCopyImage");
            tsmiCopyImage.Click += tsmiCopyImage_Click;
            // 
            // tsmiCopyImageDimensions
            // 
            tsmiCopyImageDimensions.Name = "tsmiCopyImageDimensions";
            resources.ApplyResources(tsmiCopyImageDimensions, "tsmiCopyImageDimensions");
            tsmiCopyImageDimensions.Click += tsmiCopyImageDimensions_Click;
            // 
            // tsmiCopyText
            // 
            tsmiCopyText.Name = "tsmiCopyText";
            resources.ApplyResources(tsmiCopyText, "tsmiCopyText");
            tsmiCopyText.Click += tsmiCopyText_Click;
            // 
            // tsmiCopyThumbnailFile
            // 
            tsmiCopyThumbnailFile.Name = "tsmiCopyThumbnailFile";
            resources.ApplyResources(tsmiCopyThumbnailFile, "tsmiCopyThumbnailFile");
            tsmiCopyThumbnailFile.Click += tsmiCopyThumbnailFile_Click;
            // 
            // tsmiCopyThumbnailImage
            // 
            tsmiCopyThumbnailImage.Name = "tsmiCopyThumbnailImage";
            resources.ApplyResources(tsmiCopyThumbnailImage, "tsmiCopyThumbnailImage");
            tsmiCopyThumbnailImage.Click += tsmiCopyThumbnailImage_Click;
            // 
            // tssCopy2
            // 
            tssCopy2.Name = "tssCopy2";
            resources.ApplyResources(tssCopy2, "tssCopy2");
            // 
            // tsmiCopyHTMLLink
            // 
            tsmiCopyHTMLLink.Name = "tsmiCopyHTMLLink";
            resources.ApplyResources(tsmiCopyHTMLLink, "tsmiCopyHTMLLink");
            tsmiCopyHTMLLink.Click += tsmiCopyHTMLLink_Click;
            // 
            // tsmiCopyHTMLImage
            // 
            tsmiCopyHTMLImage.Name = "tsmiCopyHTMLImage";
            resources.ApplyResources(tsmiCopyHTMLImage, "tsmiCopyHTMLImage");
            tsmiCopyHTMLImage.Click += tsmiCopyHTMLImage_Click;
            // 
            // tsmiCopyHTMLLinkedImage
            // 
            tsmiCopyHTMLLinkedImage.Name = "tsmiCopyHTMLLinkedImage";
            resources.ApplyResources(tsmiCopyHTMLLinkedImage, "tsmiCopyHTMLLinkedImage");
            tsmiCopyHTMLLinkedImage.Click += tsmiCopyHTMLLinkedImage_Click;
            // 
            // tssCopy3
            // 
            tssCopy3.Name = "tssCopy3";
            resources.ApplyResources(tssCopy3, "tssCopy3");
            // 
            // tsmiCopyForumLink
            // 
            tsmiCopyForumLink.Name = "tsmiCopyForumLink";
            resources.ApplyResources(tsmiCopyForumLink, "tsmiCopyForumLink");
            tsmiCopyForumLink.Click += tsmiCopyForumLink_Click;
            // 
            // tsmiCopyForumImage
            // 
            tsmiCopyForumImage.Name = "tsmiCopyForumImage";
            resources.ApplyResources(tsmiCopyForumImage, "tsmiCopyForumImage");
            tsmiCopyForumImage.Click += tsmiCopyForumImage_Click;
            // 
            // tsmiCopyForumLinkedImage
            // 
            tsmiCopyForumLinkedImage.Name = "tsmiCopyForumLinkedImage";
            resources.ApplyResources(tsmiCopyForumLinkedImage, "tsmiCopyForumLinkedImage");
            tsmiCopyForumLinkedImage.Click += tsmiCopyForumLinkedImage_Click;
            // 
            // tssCopy4
            // 
            tssCopy4.Name = "tssCopy4";
            resources.ApplyResources(tssCopy4, "tssCopy4");
            // 
            // tsmiCopyMarkdownLink
            // 
            tsmiCopyMarkdownLink.Name = "tsmiCopyMarkdownLink";
            resources.ApplyResources(tsmiCopyMarkdownLink, "tsmiCopyMarkdownLink");
            tsmiCopyMarkdownLink.Click += tsmiCopyMarkdownLink_Click;
            // 
            // tsmiCopyMarkdownImage
            // 
            tsmiCopyMarkdownImage.Name = "tsmiCopyMarkdownImage";
            resources.ApplyResources(tsmiCopyMarkdownImage, "tsmiCopyMarkdownImage");
            tsmiCopyMarkdownImage.Click += tsmiCopyMarkdownImage_Click;
            // 
            // tsmiCopyMarkdownLinkedImage
            // 
            tsmiCopyMarkdownLinkedImage.Name = "tsmiCopyMarkdownLinkedImage";
            resources.ApplyResources(tsmiCopyMarkdownLinkedImage, "tsmiCopyMarkdownLinkedImage");
            tsmiCopyMarkdownLinkedImage.Click += tsmiCopyMarkdownLinkedImage_Click;
            // 
            // tssCopy5
            // 
            tssCopy5.Name = "tssCopy5";
            resources.ApplyResources(tssCopy5, "tssCopy5");
            // 
            // tsmiCopyFilePath
            // 
            tsmiCopyFilePath.Name = "tsmiCopyFilePath";
            resources.ApplyResources(tsmiCopyFilePath, "tsmiCopyFilePath");
            tsmiCopyFilePath.Click += tsmiCopyFilePath_Click;
            // 
            // tsmiCopyFileName
            // 
            tsmiCopyFileName.Name = "tsmiCopyFileName";
            resources.ApplyResources(tsmiCopyFileName, "tsmiCopyFileName");
            tsmiCopyFileName.Click += tsmiCopyFileName_Click;
            // 
            // tsmiCopyFileNameWithExtension
            // 
            tsmiCopyFileNameWithExtension.Name = "tsmiCopyFileNameWithExtension";
            resources.ApplyResources(tsmiCopyFileNameWithExtension, "tsmiCopyFileNameWithExtension");
            tsmiCopyFileNameWithExtension.Click += tsmiCopyFileNameWithExtension_Click;
            // 
            // tsmiCopyFolder
            // 
            tsmiCopyFolder.Name = "tsmiCopyFolder";
            resources.ApplyResources(tsmiCopyFolder, "tsmiCopyFolder");
            tsmiCopyFolder.Click += tsmiCopyFolder_Click;
            // 
            // tssCopy6
            // 
            tssCopy6.Name = "tssCopy6";
            resources.ApplyResources(tssCopy6, "tssCopy6");
            // 
            // tsmiUploadSelectedFile
            // 
            tsmiUploadSelectedFile.Image = Properties.Resources.drive_upload;
            tsmiUploadSelectedFile.Name = "tsmiUploadSelectedFile";
            resources.ApplyResources(tsmiUploadSelectedFile, "tsmiUploadSelectedFile");
            tsmiUploadSelectedFile.Click += tsmiUploadSelectedFile_Click;
            // 
            // tsmiDownloadSelectedURL
            // 
            tsmiDownloadSelectedURL.Image = Properties.Resources.drive_download;
            tsmiDownloadSelectedURL.Name = "tsmiDownloadSelectedURL";
            resources.ApplyResources(tsmiDownloadSelectedURL, "tsmiDownloadSelectedURL");
            tsmiDownloadSelectedURL.Click += tsmiDownloadSelectedURL_Click;
            // 
            // tsmiEditSelectedFile
            // 
            tsmiEditSelectedFile.Image = Properties.Resources.image_pencil;
            tsmiEditSelectedFile.Name = "tsmiEditSelectedFile";
            resources.ApplyResources(tsmiEditSelectedFile, "tsmiEditSelectedFile");
            tsmiEditSelectedFile.Click += tsmiEditSelectedFile_Click;
            // 
            // tsmiBeautifyImage
            // 
            tsmiBeautifyImage.Image = Properties.Resources.picture_sunset;
            tsmiBeautifyImage.Name = "tsmiBeautifyImage";
            resources.ApplyResources(tsmiBeautifyImage, "tsmiBeautifyImage");
            tsmiBeautifyImage.Click += tsmiBeautifyImage_Click;
            // 
            // tsmiAddImageEffects
            // 
            tsmiAddImageEffects.Image = Properties.Resources.image_saturation;
            tsmiAddImageEffects.Name = "tsmiAddImageEffects";
            resources.ApplyResources(tsmiAddImageEffects, "tsmiAddImageEffects");
            tsmiAddImageEffects.Click += tsmiAddImageEffects_Click;
            // 
            // tsmiPinSelectedFile
            // 
            tsmiPinSelectedFile.Image = Properties.Resources.pin;
            tsmiPinSelectedFile.Name = "tsmiPinSelectedFile";
            resources.ApplyResources(tsmiPinSelectedFile, "tsmiPinSelectedFile");
            tsmiPinSelectedFile.Click += tsmiPinSelectedFile_Click;
            // 
            // tsmiRunAction
            // 
            tsmiRunAction.Image = Properties.Resources.application_terminal;
            tsmiRunAction.Name = "tsmiRunAction";
            resources.ApplyResources(tsmiRunAction, "tsmiRunAction");
            // 
            // tsmiDeleteSelectedItem
            // 
            tsmiDeleteSelectedItem.Image = Properties.Resources.script__minus;
            tsmiDeleteSelectedItem.Name = "tsmiDeleteSelectedItem";
            resources.ApplyResources(tsmiDeleteSelectedItem, "tsmiDeleteSelectedItem");
            tsmiDeleteSelectedItem.Click += tsmiDeleteSelectedItem_Click;
            // 
            // tsmiDeleteSelectedFile
            // 
            tsmiDeleteSelectedFile.Image = Properties.Resources.bin;
            tsmiDeleteSelectedFile.Name = "tsmiDeleteSelectedFile";
            resources.ApplyResources(tsmiDeleteSelectedFile, "tsmiDeleteSelectedFile");
            tsmiDeleteSelectedFile.Click += tsmiDeleteSelectedFile_Click;
            // 
            // tsmiShortenSelectedURL
            // 
            tsmiShortenSelectedURL.Image = Properties.Resources.edit_scale;
            tsmiShortenSelectedURL.Name = "tsmiShortenSelectedURL";
            resources.ApplyResources(tsmiShortenSelectedURL, "tsmiShortenSelectedURL");
            // 
            // tsmiShareSelectedURL
            // 
            tsmiShareSelectedURL.Image = Properties.Resources.globe_share;
            tsmiShareSelectedURL.Name = "tsmiShareSelectedURL";
            resources.ApplyResources(tsmiShareSelectedURL, "tsmiShareSelectedURL");
            // 
            // tsmiAnalyzeImage
            // 
            tsmiAnalyzeImage.Image = Properties.Resources.robot;
            tsmiAnalyzeImage.Name = "tsmiAnalyzeImage";
            resources.ApplyResources(tsmiAnalyzeImage, "tsmiAnalyzeImage");
            tsmiAnalyzeImage.Click += tsmiAnalyzeImage_Click;
            // 
            // tsmiGoogleLens
            // 
            tsmiGoogleLens.Image = Properties.Resources.Google_Lens;
            tsmiGoogleLens.Name = "tsmiGoogleLens";
            resources.ApplyResources(tsmiGoogleLens, "tsmiGoogleLens");
            tsmiGoogleLens.Click += tsmiGoogleLens_Click;
            // 
            // tsmiBingVisualSearch
            // 
            tsmiBingVisualSearch.Image = Properties.Resources.Bing;
            tsmiBingVisualSearch.Name = "tsmiBingVisualSearch";
            resources.ApplyResources(tsmiBingVisualSearch, "tsmiBingVisualSearch");
            tsmiBingVisualSearch.Click += tsmiBingVisualSearch_Click;
            // 
            // tsmiShowQRCode
            // 
            tsmiShowQRCode.Image = Properties.Resources.barcode_2d;
            tsmiShowQRCode.Name = "tsmiShowQRCode";
            resources.ApplyResources(tsmiShowQRCode, "tsmiShowQRCode");
            tsmiShowQRCode.Click += tsmiShowQRCode_Click;
            // 
            // tsmiOCRImage
            // 
            tsmiOCRImage.Image = Properties.Resources.edit_drop_cap;
            tsmiOCRImage.Name = "tsmiOCRImage";
            resources.ApplyResources(tsmiOCRImage, "tsmiOCRImage");
            tsmiOCRImage.Click += tsmiOCRImage_Click;
            // 
            // tsmiCombineImages
            // 
            tsmiCombineImages.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {tsmiCombineImagesHorizontally, tsmiCombineImagesVertically});
            tsmiCombineImages.Image = Properties.Resources.document_break;
            tsmiCombineImages.Name = "tsmiCombineImages";
            resources.ApplyResources(tsmiCombineImages, "tsmiCombineImages");
            tsmiCombineImages.Click += tsmiCombineImages_Click;
            // 
            // tsmiCombineImagesHorizontally
            // 
            tsmiCombineImagesHorizontally.Image = Properties.Resources.application_tile_horizontal;
            tsmiCombineImagesHorizontally.Name = "tsmiCombineImagesHorizontally";
            resources.ApplyResources(tsmiCombineImagesHorizontally, "tsmiCombineImagesHorizontally");
            tsmiCombineImagesHorizontally.Click += tsmiCombineImagesHorizontally_Click;
            // 
            // tsmiCombineImagesVertically
            // 
            tsmiCombineImagesVertically.Image = Properties.Resources.application_tile_vertical;
            tsmiCombineImagesVertically.Name = "tsmiCombineImagesVertically";
            resources.ApplyResources(tsmiCombineImagesVertically, "tsmiCombineImagesVertically");
            tsmiCombineImagesVertically.Click += tsmiCombineImagesVertically_Click;
            // 
            // tsmiShowResponse
            // 
            tsmiShowResponse.Image = Properties.Resources.application_browser;
            tsmiShowResponse.Name = "tsmiShowResponse";
            resources.ApplyResources(tsmiShowResponse, "tsmiShowResponse");
            tsmiShowResponse.Click += tsmiShowResponse_Click;
            // 
            // tsmiClearList
            // 
            tsmiClearList.Image = Properties.Resources.eraser;
            tsmiClearList.Name = "tsmiClearList";
            resources.ApplyResources(tsmiClearList, "tsmiClearList");
            tsmiClearList.Click += tsmiClearList_Click;
            // 
            // tssUploadInfo1
            // 
            tssUploadInfo1.Name = "tssUploadInfo1";
            resources.ApplyResources(tssUploadInfo1, "tssUploadInfo1");
            // 
            // tsmiSwitchTaskViewMode
            // 
            tsmiSwitchTaskViewMode.Name = "tsmiSwitchTaskViewMode";
            resources.ApplyResources(tsmiSwitchTaskViewMode, "tsmiSwitchTaskViewMode");
            tsmiSwitchTaskViewMode.Click += TsmiSwitchTaskViewMode_Click;
            // 
            // niTray
            // 
            niTray.ContextMenuStrip = cmsTray;
            resources.ApplyResources(niTray, "niTray");
            niTray.MouseUp += niTray_MouseUp;
            // 
            // cmsTray
            // 
            cmsTray.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {tsmiTrayToggleHotkeys, tsmiTrayExit});
            cmsTray.Name = "cmsTray";
            resources.ApplyResources(cmsTray, "cmsTray");
            cmsTray.Closed += cmsTray_Closed;
            cmsTray.Opened += cmsTray_Opened;
            // 
            // tsmiTrayToggleHotkeys
            // 
            tsmiTrayToggleHotkeys.Image = Properties.Resources.keyboard__minus;
            tsmiTrayToggleHotkeys.Name = "tsmiTrayToggleHotkeys";
            resources.ApplyResources(tsmiTrayToggleHotkeys, "tsmiTrayToggleHotkeys");
            tsmiTrayToggleHotkeys.Click += tsmiTrayToggleHotkeys_Click;
            // 
            // tsmiTrayExit
            // 
            tsmiTrayExit.Image = Properties.Resources.cross_button;
            tsmiTrayExit.Name = "tsmiTrayExit";
            resources.ApplyResources(tsmiTrayExit, "tsmiTrayExit");
            tsmiTrayExit.Click += tsmiTrayExit_Click;
            tsmiTrayExit.MouseDown += tsmiTrayExit_MouseDown;
            // 
            // ttMain
            // 
            ttMain.AutoPopDelay = 5000;
            ttMain.InitialDelay = 200;
            ttMain.OwnerDraw = true;
            ttMain.ReshowDelay = 100;
            ttMain.Draw += TtMain_Draw;
            // 
            // pToolbars
            // 
            resources.ApplyResources(pToolbars, "pToolbars");
            pToolbars.Controls.Add(tsMain);
            pToolbars.Name = "pToolbars";
            // 
            // dgvHotkeys
            // 
            dgvHotkeys.AllowUserToAddRows = false;
            dgvHotkeys.AllowUserToDeleteRows = false;
            dgvHotkeys.AllowUserToResizeColumns = false;
            dgvHotkeys.AllowUserToResizeRows = false;
            dgvHotkeys.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            dgvHotkeys.BorderStyle = System.Windows.Forms.BorderStyle.None;
            dgvHotkeys.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle1.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            dgvHotkeys.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            dgvHotkeys.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvHotkeys.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {cHotkeyStatus, cHotkey, cDescription});
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            dataGridViewCellStyle3.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle3.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            dgvHotkeys.DefaultCellStyle = dataGridViewCellStyle3;
            resources.ApplyResources(dgvHotkeys, "dgvHotkeys");
            dgvHotkeys.MultiSelect = false;
            dgvHotkeys.Name = "dgvHotkeys";
            dgvHotkeys.ReadOnly = true;
            dgvHotkeys.RowHeadersVisible = false;
            dgvHotkeys.TabStop = false;
            dgvHotkeys.MouseDoubleClick += dgvHotkeys_MouseDoubleClick;
            dgvHotkeys.MouseUp += dgvHotkeys_MouseUp;
            // 
            // cHotkeyStatus
            // 
            resources.ApplyResources(cHotkeyStatus, "cHotkeyStatus");
            cHotkeyStatus.Name = "cHotkeyStatus";
            cHotkeyStatus.ReadOnly = true;
            // 
            // cHotkey
            // 
            cHotkey.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            cHotkey.DefaultCellStyle = dataGridViewCellStyle2;
            resources.ApplyResources(cHotkey, "cHotkey");
            cHotkey.Name = "cHotkey";
            cHotkey.ReadOnly = true;
            cHotkey.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // cDescription
            // 
            cDescription.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            resources.ApplyResources(cDescription, "cDescription");
            cDescription.Name = "cDescription";
            cDescription.ReadOnly = true;
            cDescription.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // pMain
            // 
            pMain.Controls.Add(pHotkeys);
            pMain.Controls.Add(ucTaskThumbnailView);
            pMain.Controls.Add(scMain);
            resources.ApplyResources(pMain, "pMain");
            pMain.Name = "pMain";
            // 
            // pHotkeys
            // 
            pHotkeys.Controls.Add(dgvHotkeys);
            resources.ApplyResources(pHotkeys, "pHotkeys");
            pHotkeys.Name = "pHotkeys";
            // 
            // ucTaskThumbnailView
            // 
            resources.ApplyResources(ucTaskThumbnailView, "ucTaskThumbnailView");
            ucTaskThumbnailView.BackColor = System.Drawing.SystemColors.Window;
            ucTaskThumbnailView.ClickAction = ThumbnailViewClickAction.Default;
            ucTaskThumbnailView.Name = "ucTaskThumbnailView";
            ucTaskThumbnailView.ThumbnailSize = new System.Drawing.Size(200, 150);
            ucTaskThumbnailView.TitleLocation = ThumbnailTitleLocation.Top;
            ucTaskThumbnailView.TitleVisible = true;
            ucTaskThumbnailView.ContextMenuRequested += UcTaskView_ContextMenuRequested;
            ucTaskThumbnailView.SelectedPanelChanged += ucTaskThumbnailView_SelectedPanelChanged;
            ucTaskThumbnailView.KeyDown += lvUploads_KeyDown;
            // 
            // MainForm
            // 
            AllowDrop = true;
            resources.ApplyResources(this, "$this");
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = System.Drawing.SystemColors.Window;
            Controls.Add(pMain);
            Controls.Add(pToolbars);
            DoubleBuffered = true;
            Name = "MainForm";
            FormClosing += MainForm_FormClosing;
            FormClosed += MainForm_FormClosed;
            Shown += MainForm_Shown;
            LocationChanged += MainForm_LocationChanged;
            SizeChanged += MainForm_SizeChanged;
            VisibleChanged += MainForm_VisibleChanged;
            DragDrop += MainForm_DragDrop;
            DragEnter += MainForm_DragEnter;
            Resize += MainForm_Resize;
            scMain.Panel1.ResumeLayout(false);
            scMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)scMain).EndInit();
            scMain.ResumeLayout(false);
            tsMain.ResumeLayout(false);
            tsMain.PerformLayout();
            cmsTaskInfo.ResumeLayout(false);
            cmsTray.ResumeLayout(false);
            pToolbars.ResumeLayout(false);
            pToolbars.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dgvHotkeys).EndInit();
            pMain.ResumeLayout(false);
            pHotkeys.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion Windows Form Designer generated code
        private System.Windows.Forms.ColumnHeader chStatus;
        private System.Windows.Forms.ColumnHeader chURL;
        private System.Windows.Forms.ColumnHeader chFilename;
        private System.Windows.Forms.ColumnHeader chProgress;
        private ShareX.HelpersLib.ToolStripBorderRight tsMain;
        private System.Windows.Forms.ColumnHeader chSpeed;
        private System.Windows.Forms.ColumnHeader chRemaining;
        private System.Windows.Forms.ColumnHeader chElapsed;
        private System.Windows.Forms.ToolStripMenuItem tsmiTrayExit;
        public System.Windows.Forms.NotifyIcon niTray;
        private HelpersLib.SplitContainerCustomSplitter scMain;
        private System.Windows.Forms.ContextMenuStrip cmsTaskInfo;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpen;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenShortenedURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenThumbnailURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenDeletionURL;
        private System.Windows.Forms.ToolStripSeparator tssOpen1;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenFolder;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopy;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyShortenedURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyThumbnailURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyDeletionURL;
        private System.Windows.Forms.ToolStripSeparator tssCopy1;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyImage;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyText;
        private System.Windows.Forms.ToolStripSeparator tssCopy2;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyHTMLLink;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyHTMLImage;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyHTMLLinkedImage;
        private System.Windows.Forms.ToolStripSeparator tssCopy3;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyForumLink;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyForumImage;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyForumLinkedImage;
        private System.Windows.Forms.ToolStripSeparator tssCopy4;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyFilePath;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyFileName;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyFileNameWithExtension;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyFolder;
        private System.Windows.Forms.ToolStripMenuItem tsmiStopUpload;
        private HelpersLib.MyPictureBox pbPreview;
        private System.Windows.Forms.ToolStripMenuItem tsmiShowErrors;
        private System.Windows.Forms.ToolStripMenuItem tsmiShowResponse;
        private System.Windows.Forms.ToolStripButton tsbScreenshotsFolder;
        private System.Windows.Forms.ToolStripSeparator tssUploadInfo1;
        private System.Windows.Forms.ToolStripMenuItem tsmiUploadSelectedFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiClearList;
        private System.Windows.Forms.ToolStripSeparator tssCopy5;
        private System.Windows.Forms.ToolStripButton tsbApplicationSettings;
        private System.Windows.Forms.ToolStripButton tsbHotkeySettings;
        private System.Windows.Forms.ToolStripButton tsbAbout;
        private System.Windows.Forms.ToolStripMenuItem tsmiOpenThumbnailFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyThumbnailFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyThumbnailImage;
        private System.Windows.Forms.ToolStripMenuItem tsmiShowQRCode;
        private System.Windows.Forms.ToolStripMenuItem tsmiShareSelectedURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiShortenSelectedURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiEditSelectedFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiDeleteSelectedFile;
        public System.Windows.Forms.ToolStripMenuItem tsmiTrayRecentItems = null;
        private System.Windows.Forms.ContextMenuStrip cmsTray;
        private System.Windows.Forms.ToolStripMenuItem tsmiTrayToggleHotkeys;
        private System.Windows.Forms.ToolStripMenuItem tsmiDownloadSelectedURL;
        private System.Windows.Forms.ToolStripMenuItem tsmiOCRImage;
        private System.Windows.Forms.ToolStripMenuItem tsmiCombineImages;
        private System.Windows.Forms.ToolStripMenuItem tsmiDeleteSelectedItem;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyMarkdownLink;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyMarkdownImage;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyMarkdownLinkedImage;
        private System.Windows.Forms.ToolStripSeparator tssCopy6;
        private System.Windows.Forms.ToolStripMenuItem tsmiCopyImageDimensions;
        private TaskThumbnailView ucTaskThumbnailView;
        private System.Windows.Forms.ToolStripMenuItem tsmiSwitchTaskViewMode;
        private System.Windows.Forms.ToolTip ttMain;
        private System.Windows.Forms.ToolStripMenuItem tsmiRunAction;
        private System.Windows.Forms.Panel pToolbars;
        private System.Windows.Forms.ToolStripMenuItem tsmiAddImageEffects;
        private System.Windows.Forms.ToolStripMenuItem tsmiCombineImagesHorizontally;
        private System.Windows.Forms.ToolStripMenuItem tsmiCombineImagesVertically;
        private System.Windows.Forms.ToolStripMenuItem tsmiBingVisualSearch;
        private System.Windows.Forms.ToolStripMenuItem tsmiPinSelectedFile;
        private System.Windows.Forms.ToolStripMenuItem tsmiBeautifyImage;
        private System.Windows.Forms.DataGridViewTextBoxColumn cHotkeyStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn cHotkey;
        private System.Windows.Forms.DataGridViewTextBoxColumn cDescription;
        private System.Windows.Forms.Panel pMain;
        private System.Windows.Forms.DataGridView dgvHotkeys;
        internal System.Windows.Forms.Panel pHotkeys;
        private HelpersLib.MyListView lvUploads;
        private System.Windows.Forms.ToolStripMenuItem tsmiGoogleLens;
        private System.Windows.Forms.ToolStripMenuItem tsmiAnalyzeImage;
    }
}
