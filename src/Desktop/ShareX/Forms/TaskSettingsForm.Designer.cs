namespace ShareX
{
    partial class TaskSettingsForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            tcTaskSettings = new System.Windows.Forms.TabControl();
            tpTask = new System.Windows.Forms.TabPage();
            tpGeneral = new System.Windows.Forms.TabPage();
            tcGeneral = new System.Windows.Forms.TabControl();
            tpGeneralMain = new System.Windows.Forms.TabPage();
            tpNotifications = new System.Windows.Forms.TabPage();
            tpCapture = new System.Windows.Forms.TabPage();
            tcCapture = new System.Windows.Forms.TabControl();
            tpCaptureGeneral = new System.Windows.Forms.TabPage();
            tpRegionCapture = new System.Windows.Forms.TabPage();
            tpScreenRecorder = new System.Windows.Forms.TabPage();
            tpOCR = new System.Windows.Forms.TabPage();
            tpTools = new System.Windows.Forms.TabPage();
            tcTools = new System.Windows.Forms.TabControl();
            tpToolsGeneral = new System.Windows.Forms.TabPage();
            tttvMain = new ShareX.HelpersLib.TabToTreeView();
            tcTaskSettings.SuspendLayout();
            tpTask.SuspendLayout();
            tpGeneral.SuspendLayout();
            tcGeneral.SuspendLayout();
            tpGeneralMain.SuspendLayout();
            tpNotifications.SuspendLayout();
            tpCapture.SuspendLayout();
            tcCapture.SuspendLayout();
            tpCaptureGeneral.SuspendLayout();
            tpRegionCapture.SuspendLayout();
            tpScreenRecorder.SuspendLayout();
            tpOCR.SuspendLayout();
            tpTools.SuspendLayout();
            tcTools.SuspendLayout();
            tpToolsGeneral.SuspendLayout();
            SuspendLayout();
            // 
            // tcTaskSettings
            // 
            tcTaskSettings.Controls.Add(tpTask);
            tcTaskSettings.Controls.Add(tpGeneral);
            tcTaskSettings.Controls.Add(tpCapture);
            tcTaskSettings.Controls.Add(tpTools);
            resources.ApplyResources(tcTaskSettings, "tcTaskSettings");
            tcTaskSettings.Name = "tcTaskSettings";
            tcTaskSettings.SelectedIndex = 0;
            // 
            // tpTask
            // 
            tpTask.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(tpTask, "tpTask");
            tpTask.Name = "tpTask";
            // 
            // tpGeneral
            // 
            tpGeneral.BackColor = System.Drawing.SystemColors.Window;
            tpGeneral.Controls.Add(tcGeneral);
            resources.ApplyResources(tpGeneral, "tpGeneral");
            tpGeneral.Name = "tpGeneral";
            // 
            // tcGeneral
            // 
            tcGeneral.Controls.Add(tpGeneralMain);
            tcGeneral.Controls.Add(tpNotifications);
            resources.ApplyResources(tcGeneral, "tcGeneral");
            tcGeneral.Name = "tcGeneral";
            tcGeneral.SelectedIndex = 0;
            resources.ApplyResources(tpGeneralMain, "tpGeneralMain");
            tpGeneralMain.Name = "tpGeneralMain";
            tpGeneralMain.UseVisualStyleBackColor = true;
            // 
            // tpNotifications
            // 
            resources.ApplyResources(tpNotifications, "tpNotifications");
            tpNotifications.Name = "tpNotifications";
            tpNotifications.UseVisualStyleBackColor = true;
            // 
            // tpCapture
            // 
            tpCapture.BackColor = System.Drawing.SystemColors.Window;
            tpCapture.Controls.Add(tcCapture);
            resources.ApplyResources(tpCapture, "tpCapture");
            tpCapture.Name = "tpCapture";
            // 
            // tcCapture
            // 
            tcCapture.Controls.Add(tpCaptureGeneral);
            tcCapture.Controls.Add(tpRegionCapture);
            tcCapture.Controls.Add(tpScreenRecorder);
            tcCapture.Controls.Add(tpOCR);
            resources.ApplyResources(tcCapture, "tcCapture");
            tcCapture.Name = "tcCapture";
            tcCapture.SelectedIndex = 0;
            // 
            // tpCaptureGeneral
            // 
            tpCaptureGeneral.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(tpCaptureGeneral, "tpCaptureGeneral");
            tpCaptureGeneral.Name = "tpCaptureGeneral";
            // 
            // tpRegionCapture
            // 
            tpRegionCapture.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(tpRegionCapture, "tpRegionCapture");
            tpRegionCapture.Name = "tpRegionCapture";
            // 
            // tpScreenRecorder
            // 
            tpScreenRecorder.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(tpScreenRecorder, "tpScreenRecorder");
            tpScreenRecorder.Name = "tpScreenRecorder";
            resources.ApplyResources(tpOCR, "tpOCR");
            tpOCR.Name = "tpOCR";
            tpOCR.UseVisualStyleBackColor = true;
            // 
            // tpTools
            // 
            tpTools.BackColor = System.Drawing.SystemColors.Window;
            tpTools.Controls.Add(tcTools);
            resources.ApplyResources(tpTools, "tpTools");
            tpTools.Name = "tpTools";
            // 
            // tcTools
            // 
            tcTools.Controls.Add(tpToolsGeneral);
            resources.ApplyResources(tcTools, "tcTools");
            tcTools.Name = "tcTools";
            tcTools.SelectedIndex = 0;
            resources.ApplyResources(tpToolsGeneral, "tpToolsGeneral");
            tpToolsGeneral.Name = "tpToolsGeneral";
            tpToolsGeneral.UseVisualStyleBackColor = true;
            // 
            // tttvMain
            // 
            resources.ApplyResources(tttvMain, "tttvMain");
            tttvMain.ImageList = null;
            tttvMain.LeftPanelBackColor = System.Drawing.SystemColors.Window;
            tttvMain.MainTabControl = null;
            tttvMain.Name = "tttvMain";
            tttvMain.SeparatorColor = System.Drawing.SystemColors.ControlDark;
            tttvMain.TreeViewFont = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 162);
            tttvMain.TreeViewSize = 190;
            tttvMain.TabChanged += tttvMain_TabChanged;
            // 
            // TaskSettingsForm
            // 
            resources.ApplyResources(this, "$this");
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            BackColor = System.Drawing.SystemColors.Window;
            Controls.Add(tcTaskSettings);
            Controls.Add(tttvMain);
            Name = "TaskSettingsForm";
            SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            Resize += TaskSettingsForm_Resize;
            tcTaskSettings.ResumeLayout(false);
            tpTask.ResumeLayout(false);
            tpTask.PerformLayout();
            tpGeneral.ResumeLayout(false);
            tcGeneral.ResumeLayout(false);
            tpGeneralMain.ResumeLayout(false);
            tpGeneralMain.PerformLayout();
            tpNotifications.ResumeLayout(false);
            tpNotifications.PerformLayout();
            tpCapture.ResumeLayout(false);
            tcCapture.ResumeLayout(false);
            tpCaptureGeneral.ResumeLayout(false);
            tpCaptureGeneral.PerformLayout();
            tpRegionCapture.ResumeLayout(false);
            tpRegionCapture.PerformLayout();
            tpScreenRecorder.ResumeLayout(false);
            tpScreenRecorder.PerformLayout();
            tpOCR.ResumeLayout(false);
            tpOCR.PerformLayout();
            tpTools.ResumeLayout(false);
            tcTools.ResumeLayout(false);
            tpToolsGeneral.ResumeLayout(false);
            tpToolsGeneral.PerformLayout();
            ResumeLayout(false);

        }

        private void InitializeTaskPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            cmsAfterCapture = new System.Windows.Forms.ContextMenuStrip(components);
            cbOverrideAfterCaptureSettings = new System.Windows.Forms.CheckBox();
            lblDescription = new System.Windows.Forms.Label();
            tbDescription = new System.Windows.Forms.TextBox();
            cmsTask = new System.Windows.Forms.ContextMenuStrip(components);
            lblTask = new System.Windows.Forms.Label();
            btnScreenshotsFolderBrowse = new System.Windows.Forms.Button();
            txtScreenshotsFolder = new System.Windows.Forms.TextBox();
            cbOverrideScreenshotsFolder = new System.Windows.Forms.CheckBox();
            btnAfterCapture = new ShareX.HelpersLib.MenuButton();
            btnTask = new ShareX.HelpersLib.MenuButton();
            tpTask.SuspendLayout();
            // 
            // cmsAfterCapture
            // 
            cmsAfterCapture.Name = "cmsAfterCapture";
            resources.ApplyResources(cmsAfterCapture, "cmsAfterCapture");
            // 
            // cbOverrideAfterCaptureSettings
            // 
            resources.ApplyResources(cbOverrideAfterCaptureSettings, "cbOverrideAfterCaptureSettings");
            cbOverrideAfterCaptureSettings.Name = "cbOverrideAfterCaptureSettings";
            cbOverrideAfterCaptureSettings.UseVisualStyleBackColor = true;
            cbOverrideAfterCaptureSettings.CheckedChanged += cbUseDefaultAfterCaptureSettings_CheckedChanged;
            // 
            // lblDescription
            // 
            resources.ApplyResources(lblDescription, "lblDescription");
            lblDescription.Name = "lblDescription";
            // 
            // tbDescription
            // 
            resources.ApplyResources(tbDescription, "tbDescription");
            tbDescription.Name = "tbDescription";
            tbDescription.TextChanged += tbDescription_TextChanged;
            // 
            // cmsTask
            // 
            cmsTask.Name = "cmsAfterCapture";
            resources.ApplyResources(cmsTask, "cmsTask");
            // 
            // tpTask
            // 
            tpTask.BackColor = System.Drawing.SystemColors.Window;
            tpTask.Controls.Add(lblTask);
            tpTask.Controls.Add(btnScreenshotsFolderBrowse);
            tpTask.Controls.Add(txtScreenshotsFolder);
            tpTask.Controls.Add(cbOverrideScreenshotsFolder);
            tpTask.Controls.Add(tbDescription);
            tpTask.Controls.Add(btnAfterCapture);
            tpTask.Controls.Add(cbOverrideAfterCaptureSettings);
            tpTask.Controls.Add(btnTask);
            tpTask.Controls.Add(lblDescription);
            resources.ApplyResources(tpTask, "tpTask");
            tpTask.Name = "tpTask";
            // 
            // lblTask
            // 
            resources.ApplyResources(lblTask, "lblTask");
            lblTask.Name = "lblTask";
            // 
            // btnScreenshotsFolderBrowse
            // 
            resources.ApplyResources(btnScreenshotsFolderBrowse, "btnScreenshotsFolderBrowse");
            btnScreenshotsFolderBrowse.Name = "btnScreenshotsFolderBrowse";
            btnScreenshotsFolderBrowse.UseVisualStyleBackColor = true;
            btnScreenshotsFolderBrowse.Click += btnScreenshotsFolderBrowse_Click;
            // 
            // txtScreenshotsFolder
            // 
            resources.ApplyResources(txtScreenshotsFolder, "txtScreenshotsFolder");
            txtScreenshotsFolder.Name = "txtScreenshotsFolder";
            txtScreenshotsFolder.TextChanged += txtScreenshotsFolder_TextChanged;
            // 
            // cbOverrideScreenshotsFolder
            // 
            resources.ApplyResources(cbOverrideScreenshotsFolder, "cbOverrideScreenshotsFolder");
            cbOverrideScreenshotsFolder.Name = "cbOverrideScreenshotsFolder";
            cbOverrideScreenshotsFolder.UseVisualStyleBackColor = true;
            cbOverrideScreenshotsFolder.CheckedChanged += cbOverrideScreenshotsFolder_CheckedChanged;
            // 
            // btnAfterCapture
            // 
            resources.ApplyResources(btnAfterCapture, "btnAfterCapture");
            btnAfterCapture.Menu = cmsAfterCapture;
            btnAfterCapture.Name = "btnAfterCapture";
            btnAfterCapture.UseMnemonic = false;
            btnAfterCapture.UseVisualStyleBackColor = true;
            // 
            // btnTask
            // 
            btnTask.Image = Properties.Resources.gear;
            resources.ApplyResources(btnTask, "btnTask");
            btnTask.Menu = cmsTask;
            btnTask.Name = "btnTask";
            btnTask.UseMnemonic = false;
            btnTask.UseVisualStyleBackColor = true;
            tpTask.ResumeLayout(false);
            tpTask.PerformLayout();
        }

        private void InitializeGeneralMainPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            cbOverrideGeneralSettings = new System.Windows.Forms.CheckBox();
            tpGeneralMain.SuspendLayout();
            // 
            // tpGeneralMain
            // 
            tpGeneralMain.Controls.Add(cbOverrideGeneralSettings);
            resources.ApplyResources(tpGeneralMain, "tpGeneralMain");
            tpGeneralMain.Name = "tpGeneralMain";
            tpGeneralMain.UseVisualStyleBackColor = true;
            // 
            // cbOverrideGeneralSettings
            // 
            resources.ApplyResources(cbOverrideGeneralSettings, "cbOverrideGeneralSettings");
            cbOverrideGeneralSettings.Checked = true;
            cbOverrideGeneralSettings.CheckState = System.Windows.Forms.CheckState.Checked;
            cbOverrideGeneralSettings.Name = "cbOverrideGeneralSettings";
            cbOverrideGeneralSettings.UseVisualStyleBackColor = true;
            cbOverrideGeneralSettings.CheckedChanged += cbUseDefaultGeneralSettings_CheckedChanged;
            tpGeneralMain.ResumeLayout(false);
            tpGeneralMain.PerformLayout();
        }

        private void InitializeCaptureGeneralPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            pCapture = new System.Windows.Forms.Panel();
            cbCaptureAutoHideDesktopIcons = new System.Windows.Forms.CheckBox();
            cbShowCursor = new System.Windows.Forms.CheckBox();
            lblCaptureShadowOffset = new System.Windows.Forms.Label();
            cbCaptureTransparent = new System.Windows.Forms.CheckBox();
            cbCaptureShadow = new System.Windows.Forms.CheckBox();
            nudCaptureShadowOffset = new System.Windows.Forms.NumericUpDown();
            cbOverrideCaptureSettings = new System.Windows.Forms.CheckBox();
            tpCaptureGeneral.SuspendLayout();
            pCapture.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudCaptureShadowOffset).BeginInit();
            // 
            // tpCaptureGeneral
            // 
            tpCaptureGeneral.BackColor = System.Drawing.SystemColors.Window;
            tpCaptureGeneral.Controls.Add(pCapture);
            tpCaptureGeneral.Controls.Add(cbOverrideCaptureSettings);
            resources.ApplyResources(tpCaptureGeneral, "tpCaptureGeneral");
            tpCaptureGeneral.Name = "tpCaptureGeneral";
            // 
            // pCapture
            // 
            pCapture.Controls.Add(cbCaptureAutoHideDesktopIcons);
            pCapture.Controls.Add(cbShowCursor);
            pCapture.Controls.Add(lblCaptureShadowOffset);
            pCapture.Controls.Add(cbCaptureTransparent);
            pCapture.Controls.Add(cbCaptureShadow);
            pCapture.Controls.Add(nudCaptureShadowOffset);
            resources.ApplyResources(pCapture, "pCapture");
            pCapture.Name = "pCapture";
            // 
            // cbCaptureAutoHideDesktopIcons
            // 
            resources.ApplyResources(cbCaptureAutoHideDesktopIcons, "cbCaptureAutoHideDesktopIcons");
            cbCaptureAutoHideDesktopIcons.Name = "cbCaptureAutoHideDesktopIcons";
            cbCaptureAutoHideDesktopIcons.UseVisualStyleBackColor = true;
            cbCaptureAutoHideDesktopIcons.CheckedChanged += cbCaptureAutoHideDesktopIcons_CheckedChanged;
            // 
            // cbShowCursor
            // 
            resources.ApplyResources(cbShowCursor, "cbShowCursor");
            cbShowCursor.Name = "cbShowCursor";
            cbShowCursor.UseVisualStyleBackColor = true;
            cbShowCursor.CheckedChanged += cbShowCursor_CheckedChanged;
            // 
            // lblCaptureShadowOffset
            // 
            resources.ApplyResources(lblCaptureShadowOffset, "lblCaptureShadowOffset");
            lblCaptureShadowOffset.Name = "lblCaptureShadowOffset";
            // 
            // cbCaptureTransparent
            // 
            resources.ApplyResources(cbCaptureTransparent, "cbCaptureTransparent");
            cbCaptureTransparent.Name = "cbCaptureTransparent";
            cbCaptureTransparent.UseVisualStyleBackColor = true;
            cbCaptureTransparent.CheckedChanged += cbCaptureTransparent_CheckedChanged;
            // 
            // cbCaptureShadow
            // 
            resources.ApplyResources(cbCaptureShadow, "cbCaptureShadow");
            cbCaptureShadow.Name = "cbCaptureShadow";
            cbCaptureShadow.UseVisualStyleBackColor = true;
            cbCaptureShadow.CheckedChanged += cbCaptureShadow_CheckedChanged;
            // 
            // nudCaptureShadowOffset
            // 
            resources.ApplyResources(nudCaptureShadowOffset, "nudCaptureShadowOffset");
            nudCaptureShadowOffset.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
            nudCaptureShadowOffset.Name = "nudCaptureShadowOffset";
            nudCaptureShadowOffset.Value = new decimal(new int[] { 100, 0, 0, 0 });
            nudCaptureShadowOffset.ValueChanged += nudCaptureShadowOffset_ValueChanged;
            // 
            // cbOverrideCaptureSettings
            // 
            resources.ApplyResources(cbOverrideCaptureSettings, "cbOverrideCaptureSettings");
            cbOverrideCaptureSettings.Checked = true;
            cbOverrideCaptureSettings.CheckState = System.Windows.Forms.CheckState.Checked;
            cbOverrideCaptureSettings.Name = "cbOverrideCaptureSettings";
            cbOverrideCaptureSettings.UseVisualStyleBackColor = true;
            cbOverrideCaptureSettings.CheckedChanged += cbUseDefaultCaptureSettings_CheckedChanged;
            tpCaptureGeneral.ResumeLayout(false);
            tpCaptureGeneral.PerformLayout();
            pCapture.ResumeLayout(false);
            pCapture.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudCaptureShadowOffset).EndInit();
        }

        private void InitializeRegionCapturePageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            lblRegionCaptureBackgroundDimStrengthHint = new System.Windows.Forms.Label();
            nudRegionCaptureBackgroundDimStrength = new System.Windows.Forms.NumericUpDown();
            lblRegionCaptureBackgroundDimStrength = new System.Windows.Forms.Label();
            cbRegionCaptureShowInfo = new System.Windows.Forms.CheckBox();
            cbRegionCaptureMouseRightClickAction = new System.Windows.Forms.ComboBox();
            lblRegionCaptureMouseRightClickAction = new System.Windows.Forms.Label();
            tpRegionCapture.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudRegionCaptureBackgroundDimStrength).BeginInit();
            // 
            // tpRegionCapture
            // 
            tpRegionCapture.BackColor = System.Drawing.SystemColors.Window;
            tpRegionCapture.Controls.Add(lblRegionCaptureBackgroundDimStrengthHint);
            tpRegionCapture.Controls.Add(nudRegionCaptureBackgroundDimStrength);
            tpRegionCapture.Controls.Add(lblRegionCaptureBackgroundDimStrength);
            tpRegionCapture.Controls.Add(cbRegionCaptureShowInfo);
            tpRegionCapture.Controls.Add(cbRegionCaptureMouseRightClickAction);
            tpRegionCapture.Controls.Add(lblRegionCaptureMouseRightClickAction);
            resources.ApplyResources(tpRegionCapture, "tpRegionCapture");
            tpRegionCapture.Name = "tpRegionCapture";
            // 
            // lblRegionCaptureBackgroundDimStrengthHint
            // 
            resources.ApplyResources(lblRegionCaptureBackgroundDimStrengthHint, "lblRegionCaptureBackgroundDimStrengthHint");
            lblRegionCaptureBackgroundDimStrengthHint.Name = "lblRegionCaptureBackgroundDimStrengthHint";
            // 
            // nudRegionCaptureBackgroundDimStrength
            // 
            resources.ApplyResources(nudRegionCaptureBackgroundDimStrength, "nudRegionCaptureBackgroundDimStrength");
            nudRegionCaptureBackgroundDimStrength.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            nudRegionCaptureBackgroundDimStrength.Name = "nudRegionCaptureBackgroundDimStrength";
            nudRegionCaptureBackgroundDimStrength.ValueChanged += nudRegionCaptureBackgroundDimStrength_ValueChanged;
            // 
            // lblRegionCaptureBackgroundDimStrength
            // 
            resources.ApplyResources(lblRegionCaptureBackgroundDimStrength, "lblRegionCaptureBackgroundDimStrength");
            lblRegionCaptureBackgroundDimStrength.Name = "lblRegionCaptureBackgroundDimStrength";
            // 
            // cbRegionCaptureShowInfo
            // 
            resources.ApplyResources(cbRegionCaptureShowInfo, "cbRegionCaptureShowInfo");
            cbRegionCaptureShowInfo.Name = "cbRegionCaptureShowInfo";
            cbRegionCaptureShowInfo.UseVisualStyleBackColor = true;
            cbRegionCaptureShowInfo.CheckedChanged += cbRegionCaptureShowInfo_CheckedChanged;
            // 
            // cbRegionCaptureMouseRightClickAction
            // 
            cbRegionCaptureMouseRightClickAction.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbRegionCaptureMouseRightClickAction.FormattingEnabled = true;
            resources.ApplyResources(cbRegionCaptureMouseRightClickAction, "cbRegionCaptureMouseRightClickAction");
            cbRegionCaptureMouseRightClickAction.Name = "cbRegionCaptureMouseRightClickAction";
            cbRegionCaptureMouseRightClickAction.SelectedIndexChanged += cbRegionCaptureMouseRightClickAction_SelectedIndexChanged;
            // 
            // lblRegionCaptureMouseRightClickAction
            // 
            resources.ApplyResources(lblRegionCaptureMouseRightClickAction, "lblRegionCaptureMouseRightClickAction");
            lblRegionCaptureMouseRightClickAction.Name = "lblRegionCaptureMouseRightClickAction";
            tpRegionCapture.ResumeLayout(false);
            tpRegionCapture.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudRegionCaptureBackgroundDimStrength).EndInit();
        }

        private void InitializeScreenRecorderPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            cbScreenRecordTransparentRegion = new System.Windows.Forms.CheckBox();
            cbScreenRecordTwoPassEncoding = new System.Windows.Forms.CheckBox();
            cbScreenRecordConfirmAbort = new System.Windows.Forms.CheckBox();
            cbScreenRecorderShowCursor = new System.Windows.Forms.CheckBox();
            btnScreenRecorderFFmpegOptions = new System.Windows.Forms.Button();
            lblScreenRecorderStartDelay = new System.Windows.Forms.Label();
            cbScreenRecordAutoStart = new System.Windows.Forms.CheckBox();
            lblScreenRecorderFixedDuration = new System.Windows.Forms.Label();
            nudScreenRecordFPS = new System.Windows.Forms.NumericUpDown();
            lblScreenRecordFPS = new System.Windows.Forms.Label();
            nudScreenRecorderDuration = new System.Windows.Forms.NumericUpDown();
            nudScreenRecorderStartDelay = new System.Windows.Forms.NumericUpDown();
            cbScreenRecorderFixedDuration = new System.Windows.Forms.CheckBox();
            nudGIFFPS = new System.Windows.Forms.NumericUpDown();
            lblGIFFPS = new System.Windows.Forms.Label();
            tpScreenRecorder.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudScreenRecordFPS).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudScreenRecorderDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudScreenRecorderStartDelay).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudGIFFPS).BeginInit();
            // 
            // tpScreenRecorder
            // 
            tpScreenRecorder.BackColor = System.Drawing.SystemColors.Window;
            tpScreenRecorder.Controls.Add(cbScreenRecordTransparentRegion);
            tpScreenRecorder.Controls.Add(cbScreenRecordTwoPassEncoding);
            tpScreenRecorder.Controls.Add(cbScreenRecordConfirmAbort);
            tpScreenRecorder.Controls.Add(cbScreenRecorderShowCursor);
            tpScreenRecorder.Controls.Add(btnScreenRecorderFFmpegOptions);
            tpScreenRecorder.Controls.Add(lblScreenRecorderStartDelay);
            tpScreenRecorder.Controls.Add(cbScreenRecordAutoStart);
            tpScreenRecorder.Controls.Add(lblScreenRecorderFixedDuration);
            tpScreenRecorder.Controls.Add(nudScreenRecordFPS);
            tpScreenRecorder.Controls.Add(lblScreenRecordFPS);
            tpScreenRecorder.Controls.Add(nudScreenRecorderDuration);
            tpScreenRecorder.Controls.Add(nudScreenRecorderStartDelay);
            tpScreenRecorder.Controls.Add(cbScreenRecorderFixedDuration);
            tpScreenRecorder.Controls.Add(nudGIFFPS);
            tpScreenRecorder.Controls.Add(lblGIFFPS);
            resources.ApplyResources(tpScreenRecorder, "tpScreenRecorder");
            tpScreenRecorder.Name = "tpScreenRecorder";
            // 
            // cbScreenRecordTransparentRegion
            // 
            resources.ApplyResources(cbScreenRecordTransparentRegion, "cbScreenRecordTransparentRegion");
            cbScreenRecordTransparentRegion.Name = "cbScreenRecordTransparentRegion";
            cbScreenRecordTransparentRegion.UseVisualStyleBackColor = true;
            cbScreenRecordTransparentRegion.CheckedChanged += cbScreenRecordTransparentRegion_CheckedChanged;
            // 
            // cbScreenRecordTwoPassEncoding
            // 
            resources.ApplyResources(cbScreenRecordTwoPassEncoding, "cbScreenRecordTwoPassEncoding");
            cbScreenRecordTwoPassEncoding.Name = "cbScreenRecordTwoPassEncoding";
            cbScreenRecordTwoPassEncoding.UseVisualStyleBackColor = true;
            cbScreenRecordTwoPassEncoding.CheckedChanged += cbScreenRecordTwoPassEncoding_CheckedChanged;
            // 
            // cbScreenRecordConfirmAbort
            // 
            resources.ApplyResources(cbScreenRecordConfirmAbort, "cbScreenRecordConfirmAbort");
            cbScreenRecordConfirmAbort.Name = "cbScreenRecordConfirmAbort";
            cbScreenRecordConfirmAbort.UseVisualStyleBackColor = true;
            cbScreenRecordConfirmAbort.CheckedChanged += cbScreenRecordConfirmAbort_CheckedChanged;
            // 
            // cbScreenRecorderShowCursor
            // 
            resources.ApplyResources(cbScreenRecorderShowCursor, "cbScreenRecorderShowCursor");
            cbScreenRecorderShowCursor.Name = "cbScreenRecorderShowCursor";
            cbScreenRecorderShowCursor.UseVisualStyleBackColor = true;
            cbScreenRecorderShowCursor.CheckedChanged += cbScreenRecorderShowCursor_CheckedChanged;
            // 
            // btnScreenRecorderFFmpegOptions
            // 
            resources.ApplyResources(btnScreenRecorderFFmpegOptions, "btnScreenRecorderFFmpegOptions");
            btnScreenRecorderFFmpegOptions.Name = "btnScreenRecorderFFmpegOptions";
            btnScreenRecorderFFmpegOptions.UseVisualStyleBackColor = true;
            btnScreenRecorderFFmpegOptions.Click += btnScreenRecorderFFmpegOptions_Click;
            // 
            // lblScreenRecorderStartDelay
            // 
            resources.ApplyResources(lblScreenRecorderStartDelay, "lblScreenRecorderStartDelay");
            lblScreenRecorderStartDelay.Name = "lblScreenRecorderStartDelay";
            // 
            // cbScreenRecordAutoStart
            // 
            resources.ApplyResources(cbScreenRecordAutoStart, "cbScreenRecordAutoStart");
            cbScreenRecordAutoStart.Name = "cbScreenRecordAutoStart";
            cbScreenRecordAutoStart.UseVisualStyleBackColor = true;
            cbScreenRecordAutoStart.CheckedChanged += cbScreenRecordAutoStart_CheckedChanged;
            // 
            // lblScreenRecorderFixedDuration
            // 
            resources.ApplyResources(lblScreenRecorderFixedDuration, "lblScreenRecorderFixedDuration");
            lblScreenRecorderFixedDuration.Name = "lblScreenRecorderFixedDuration";
            // 
            // nudScreenRecordFPS
            // 
            resources.ApplyResources(nudScreenRecordFPS, "nudScreenRecordFPS");
            nudScreenRecordFPS.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            nudScreenRecordFPS.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudScreenRecordFPS.Name = "nudScreenRecordFPS";
            nudScreenRecordFPS.Value = new decimal(new int[] { 20, 0, 0, 0 });
            nudScreenRecordFPS.ValueChanged += nudScreenRecordFPS_ValueChanged;
            // 
            // lblScreenRecordFPS
            // 
            resources.ApplyResources(lblScreenRecordFPS, "lblScreenRecordFPS");
            lblScreenRecordFPS.Name = "lblScreenRecordFPS";
            // 
            // nudScreenRecorderDuration
            // 
            nudScreenRecorderDuration.DecimalPlaces = 1;
            nudScreenRecorderDuration.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            resources.ApplyResources(nudScreenRecorderDuration, "nudScreenRecorderDuration");
            nudScreenRecorderDuration.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            nudScreenRecorderDuration.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudScreenRecorderDuration.Name = "nudScreenRecorderDuration";
            nudScreenRecorderDuration.Value = new decimal(new int[] { 3, 0, 0, 0 });
            nudScreenRecorderDuration.ValueChanged += nudScreenRecorderDuration_ValueChanged;
            // 
            // nudScreenRecorderStartDelay
            // 
            nudScreenRecorderStartDelay.DecimalPlaces = 1;
            nudScreenRecorderStartDelay.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            resources.ApplyResources(nudScreenRecorderStartDelay, "nudScreenRecorderStartDelay");
            nudScreenRecorderStartDelay.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            nudScreenRecorderStartDelay.Name = "nudScreenRecorderStartDelay";
            nudScreenRecorderStartDelay.Value = new decimal(new int[] { 1, 0, 0, 0 });
            nudScreenRecorderStartDelay.ValueChanged += nudScreenRecorderStartDelay_ValueChanged;
            // 
            // cbScreenRecorderFixedDuration
            // 
            resources.ApplyResources(cbScreenRecorderFixedDuration, "cbScreenRecorderFixedDuration");
            cbScreenRecorderFixedDuration.Name = "cbScreenRecorderFixedDuration";
            cbScreenRecorderFixedDuration.UseVisualStyleBackColor = true;
            cbScreenRecorderFixedDuration.CheckedChanged += cbScreenRecorderFixedDuration_CheckedChanged;
            // 
            // nudGIFFPS
            // 
            resources.ApplyResources(nudGIFFPS, "nudGIFFPS");
            nudGIFFPS.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            nudGIFFPS.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudGIFFPS.Name = "nudGIFFPS";
            nudGIFFPS.Value = new decimal(new int[] { 5, 0, 0, 0 });
            nudGIFFPS.ValueChanged += nudGIFFPS_ValueChanged;
            // 
            // lblGIFFPS
            // 
            resources.ApplyResources(lblGIFFPS, "lblGIFFPS");
            lblGIFFPS.Name = "lblGIFFPS";
            tpScreenRecorder.ResumeLayout(false);
            tpScreenRecorder.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudScreenRecordFPS).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudScreenRecorderDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudScreenRecorderStartDelay).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudGIFFPS).EndInit();
        }

        private void InitializeOCRPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            btnCaptureOCRHelp = new System.Windows.Forms.Button();
            cbCaptureOCRAutoCopy = new System.Windows.Forms.CheckBox();
            cbCloseWindowAfterOpenServiceLink = new System.Windows.Forms.CheckBox();
            cbCaptureOCRSilent = new System.Windows.Forms.CheckBox();
            lblOCRDefaultLanguage = new System.Windows.Forms.Label();
            cbCaptureOCRDefaultLanguage = new System.Windows.Forms.ComboBox();
            tpOCR.SuspendLayout();
            // 
            // tpOCR
            // 
            tpOCR.Controls.Add(btnCaptureOCRHelp);
            tpOCR.Controls.Add(cbCaptureOCRAutoCopy);
            tpOCR.Controls.Add(cbCloseWindowAfterOpenServiceLink);
            tpOCR.Controls.Add(cbCaptureOCRSilent);
            tpOCR.Controls.Add(lblOCRDefaultLanguage);
            tpOCR.Controls.Add(cbCaptureOCRDefaultLanguage);
            resources.ApplyResources(tpOCR, "tpOCR");
            tpOCR.Name = "tpOCR";
            tpOCR.UseVisualStyleBackColor = true;
            // 
            // btnCaptureOCRHelp
            // 
            btnCaptureOCRHelp.Image = Properties.Resources.question;
            resources.ApplyResources(btnCaptureOCRHelp, "btnCaptureOCRHelp");
            btnCaptureOCRHelp.Name = "btnCaptureOCRHelp";
            btnCaptureOCRHelp.UseVisualStyleBackColor = true;
            btnCaptureOCRHelp.Click += btnCaptureOCRHelp_Click;
            // 
            // cbCaptureOCRAutoCopy
            // 
            resources.ApplyResources(cbCaptureOCRAutoCopy, "cbCaptureOCRAutoCopy");
            cbCaptureOCRAutoCopy.Name = "cbCaptureOCRAutoCopy";
            cbCaptureOCRAutoCopy.UseVisualStyleBackColor = true;
            cbCaptureOCRAutoCopy.CheckedChanged += cbCaptureOCRAutoCopy_CheckedChanged;
            // 
            // cbCloseWindowAfterOpenServiceLink
            // 
            resources.ApplyResources(cbCloseWindowAfterOpenServiceLink, "cbCloseWindowAfterOpenServiceLink");
            cbCloseWindowAfterOpenServiceLink.Name = "cbCloseWindowAfterOpenServiceLink";
            cbCloseWindowAfterOpenServiceLink.UseVisualStyleBackColor = true;
            cbCloseWindowAfterOpenServiceLink.CheckedChanged += cbCloseWindowAfterOpenServiceLink_CheckedChanged;
            // 
            // cbCaptureOCRSilent
            // 
            resources.ApplyResources(cbCaptureOCRSilent, "cbCaptureOCRSilent");
            cbCaptureOCRSilent.Name = "cbCaptureOCRSilent";
            cbCaptureOCRSilent.UseVisualStyleBackColor = true;
            cbCaptureOCRSilent.CheckedChanged += cbCaptureOCRSilent_CheckedChanged;
            // 
            // lblOCRDefaultLanguage
            // 
            resources.ApplyResources(lblOCRDefaultLanguage, "lblOCRDefaultLanguage");
            lblOCRDefaultLanguage.Name = "lblOCRDefaultLanguage";
            // 
            // cbCaptureOCRDefaultLanguage
            // 
            cbCaptureOCRDefaultLanguage.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbCaptureOCRDefaultLanguage.FormattingEnabled = true;
            resources.ApplyResources(cbCaptureOCRDefaultLanguage, "cbCaptureOCRDefaultLanguage");
            cbCaptureOCRDefaultLanguage.Name = "cbCaptureOCRDefaultLanguage";
            cbCaptureOCRDefaultLanguage.SelectedIndexChanged += cbCaptureOCRDefaultLanguage_SelectedIndexChanged;
            tpOCR.ResumeLayout(false);
            tpOCR.PerformLayout();
        }

        private void InitializeToolsGeneralPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            pTools = new System.Windows.Forms.Panel();
            txtToolsScreenColorPickerFormatCtrl = new System.Windows.Forms.TextBox();
            lblToolsScreenColorPickerFormatCtrl = new System.Windows.Forms.Label();
            txtToolsScreenColorPickerInfoText = new System.Windows.Forms.TextBox();
            lblToolsScreenColorPickerInfoText = new System.Windows.Forms.Label();
            txtToolsScreenColorPickerFormat = new System.Windows.Forms.TextBox();
            lblToolsScreenColorPickerFormat = new System.Windows.Forms.Label();
            cbOverrideToolsSettings = new System.Windows.Forms.CheckBox();
            tpToolsGeneral.SuspendLayout();
            pTools.SuspendLayout();
            // 
            // tpToolsGeneral
            // 
            tpToolsGeneral.Controls.Add(pTools);
            tpToolsGeneral.Controls.Add(cbOverrideToolsSettings);
            resources.ApplyResources(tpToolsGeneral, "tpToolsGeneral");
            tpToolsGeneral.Name = "tpToolsGeneral";
            tpToolsGeneral.UseVisualStyleBackColor = true;
            pTools.Controls.Add(txtToolsScreenColorPickerFormatCtrl);
            pTools.Controls.Add(lblToolsScreenColorPickerFormatCtrl);
            pTools.Controls.Add(txtToolsScreenColorPickerInfoText);
            pTools.Controls.Add(lblToolsScreenColorPickerInfoText);
            pTools.Controls.Add(txtToolsScreenColorPickerFormat);
            pTools.Controls.Add(lblToolsScreenColorPickerFormat);
            resources.ApplyResources(pTools, "pTools");
            pTools.Name = "pTools";
            // 
            // txtToolsScreenColorPickerFormatCtrl
            // 
            resources.ApplyResources(txtToolsScreenColorPickerFormatCtrl, "txtToolsScreenColorPickerFormatCtrl");
            txtToolsScreenColorPickerFormatCtrl.Name = "txtToolsScreenColorPickerFormatCtrl";
            txtToolsScreenColorPickerFormatCtrl.TextChanged += txtToolsScreenColorPickerFormatCtrl_TextChanged;
            // 
            // lblToolsScreenColorPickerFormatCtrl
            // 
            resources.ApplyResources(lblToolsScreenColorPickerFormatCtrl, "lblToolsScreenColorPickerFormatCtrl");
            lblToolsScreenColorPickerFormatCtrl.Name = "lblToolsScreenColorPickerFormatCtrl";
            // 
            // txtToolsScreenColorPickerInfoText
            // 
            resources.ApplyResources(txtToolsScreenColorPickerInfoText, "txtToolsScreenColorPickerInfoText");
            txtToolsScreenColorPickerInfoText.Name = "txtToolsScreenColorPickerInfoText";
            txtToolsScreenColorPickerInfoText.TextChanged += txtToolsScreenColorPickerInfoText_TextChanged;
            // 
            // lblToolsScreenColorPickerInfoText
            // 
            resources.ApplyResources(lblToolsScreenColorPickerInfoText, "lblToolsScreenColorPickerInfoText");
            lblToolsScreenColorPickerInfoText.Name = "lblToolsScreenColorPickerInfoText";
            // 
            // txtToolsScreenColorPickerFormat
            // 
            resources.ApplyResources(txtToolsScreenColorPickerFormat, "txtToolsScreenColorPickerFormat");
            txtToolsScreenColorPickerFormat.Name = "txtToolsScreenColorPickerFormat";
            txtToolsScreenColorPickerFormat.TextChanged += txtToolsScreenColorPickerFormat_TextChanged;
            // 
            // lblToolsScreenColorPickerFormat
            // 
            resources.ApplyResources(lblToolsScreenColorPickerFormat, "lblToolsScreenColorPickerFormat");
            lblToolsScreenColorPickerFormat.Name = "lblToolsScreenColorPickerFormat";
            // 
            // cbOverrideToolsSettings
            // 
            resources.ApplyResources(cbOverrideToolsSettings, "cbOverrideToolsSettings");
            cbOverrideToolsSettings.Checked = true;
            cbOverrideToolsSettings.CheckState = System.Windows.Forms.CheckState.Checked;
            cbOverrideToolsSettings.Name = "cbOverrideToolsSettings";
            cbOverrideToolsSettings.UseVisualStyleBackColor = true;
            cbOverrideToolsSettings.CheckedChanged += cbUseDefaultToolsSettings_CheckedChanged;
            tpToolsGeneral.ResumeLayout(false);
            tpToolsGeneral.PerformLayout();
            pTools.ResumeLayout(false);
            pTools.PerformLayout();
        }

        #endregion

        private HelpersLib.MenuButton btnAfterCapture;
        private System.Windows.Forms.ContextMenuStrip cmsAfterCapture;
        private System.Windows.Forms.CheckBox cbOverrideAfterCaptureSettings;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.TextBox tbDescription;
        private HelpersLib.MenuButton btnTask;
        private System.Windows.Forms.ContextMenuStrip cmsTask;
        private System.Windows.Forms.TabControl tcTaskSettings;
        private System.Windows.Forms.TabPage tpCapture;
        private System.Windows.Forms.TabControl tcCapture;
        private System.Windows.Forms.TabPage tpCaptureGeneral;
        private System.Windows.Forms.NumericUpDown nudCaptureShadowOffset;
        private System.Windows.Forms.CheckBox cbCaptureShadow;
        private System.Windows.Forms.CheckBox cbShowCursor;
        private System.Windows.Forms.CheckBox cbCaptureTransparent;
        private System.Windows.Forms.TabPage tpRegionCapture;
        private System.Windows.Forms.TabPage tpScreenRecorder;
        private System.Windows.Forms.TabPage tpTask;
        private System.Windows.Forms.CheckBox cbOverrideCaptureSettings;
        private System.Windows.Forms.CheckBox cbScreenRecorderFixedDuration;
        private System.Windows.Forms.NumericUpDown nudGIFFPS;
        private System.Windows.Forms.NumericUpDown nudScreenRecorderDuration;
        private System.Windows.Forms.Label lblGIFFPS;
        private System.Windows.Forms.TabPage tpGeneral;
        private System.Windows.Forms.CheckBox cbOverrideGeneralSettings;
        private System.Windows.Forms.TabPage tpTools;
        private System.Windows.Forms.NumericUpDown nudScreenRecorderStartDelay;
        private System.Windows.Forms.NumericUpDown nudScreenRecordFPS;
        private System.Windows.Forms.Label lblScreenRecordFPS;
        private System.Windows.Forms.Label lblScreenRecorderFixedDuration;
        private System.Windows.Forms.Label lblCaptureShadowOffset;
        private System.Windows.Forms.CheckBox cbScreenRecordAutoStart;
        private System.Windows.Forms.Label lblScreenRecorderStartDelay;
        private HelpersLib.TabToTreeView tttvMain;
        private System.Windows.Forms.Panel pCapture;
        private System.Windows.Forms.Button btnScreenRecorderFFmpegOptions;
        private System.Windows.Forms.CheckBox cbScreenRecorderShowCursor;
        private System.Windows.Forms.CheckBox cbOverrideToolsSettings;
        private System.Windows.Forms.Label lblRegionCaptureMouseRightClickAction;
        private System.Windows.Forms.ComboBox cbRegionCaptureMouseRightClickAction;
        private System.Windows.Forms.CheckBox cbRegionCaptureShowInfo;
        private System.Windows.Forms.Panel pTools;
        private System.Windows.Forms.TextBox txtToolsScreenColorPickerFormat;
        private System.Windows.Forms.Label lblToolsScreenColorPickerFormat;
        private System.Windows.Forms.CheckBox cbScreenRecordConfirmAbort;
        private System.Windows.Forms.CheckBox cbScreenRecordTwoPassEncoding;
        private System.Windows.Forms.TabPage tpOCR;
        private System.Windows.Forms.Label lblOCRDefaultLanguage;
        private System.Windows.Forms.ComboBox cbCaptureOCRDefaultLanguage;
        private System.Windows.Forms.CheckBox cbCaptureOCRSilent;
        private System.Windows.Forms.CheckBox cbCaptureOCRAutoCopy;
        private System.Windows.Forms.CheckBox cbCloseWindowAfterOpenServiceLink;
        private System.Windows.Forms.CheckBox cbScreenRecordTransparentRegion;
        private System.Windows.Forms.CheckBox cbOverrideScreenshotsFolder;
        private System.Windows.Forms.Button btnScreenshotsFolderBrowse;
        private System.Windows.Forms.TextBox txtScreenshotsFolder;
        private System.Windows.Forms.TextBox txtToolsScreenColorPickerInfoText;
        private System.Windows.Forms.Label lblToolsScreenColorPickerInfoText;
        private System.Windows.Forms.TextBox txtToolsScreenColorPickerFormatCtrl;
        private System.Windows.Forms.Label lblToolsScreenColorPickerFormatCtrl;
        private System.Windows.Forms.TabControl tcGeneral;
        private System.Windows.Forms.TabPage tpGeneralMain;
        private System.Windows.Forms.TabPage tpNotifications;
        private System.Windows.Forms.Label lblTask;
        private System.Windows.Forms.Button btnCaptureOCRHelp;
        private System.Windows.Forms.NumericUpDown nudRegionCaptureBackgroundDimStrength;
        private System.Windows.Forms.Label lblRegionCaptureBackgroundDimStrength;
        private System.Windows.Forms.Label lblRegionCaptureBackgroundDimStrengthHint;
        private System.Windows.Forms.CheckBox cbCaptureAutoHideDesktopIcons;
        private System.Windows.Forms.TabControl tcTools;
        private System.Windows.Forms.TabPage tpToolsGeneral;
    }
}
