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

using ShareX.HelpersLib;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace ShareX
{
    // Both the global defaults and per-hotkey overrides use the same notification editor.
    internal sealed class NotificationSettingsControl : UserControl
    {
        private readonly Func<TaskSettingsGeneral> getSettings;
        private bool updating = true;

        private TaskSettingsGeneral Settings => getSettings();

        public NotificationSettingsControl(Func<TaskSettingsGeneral> getSettings, bool useGroupedLayout = false)
        {
            this.getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            InitializeComponent();
            Control[] controls = useGroupedLayout ? AllControls(this).ToArray() : Array.Empty<Control>();
            foreach (Control control in controls) control.SuspendLayout();
            try
            {
                cbPlaySoundAfterUpload.Visible = !SystemOptions.DisableUpload;
                if (useGroupedLayout) InitializeGroupedLayout();
                LoadSettings();
                ShareXResources.ApplyCustomThemeToControl(this);
            }
            finally
            {
                for (int i = controls.Length - 1; i >= 0; i--) controls[i].ResumeLayout(false);
                if (useGroupedLayout) PerformLayout();
            }
        }

        private static IEnumerable<Control> AllControls(Control parent)
        {
            yield return parent;
            foreach (Control child in parent.Controls)
                foreach (Control control in AllControls(child)) yield return control;
        }

        private void InitializeGroupedLayout()
        {
            using var layout = new SettingsPageLayout(this, 33.6f, true);
            var content = layout.Stack();
            layout.Heading(content, "提示音", "Sounds");
            layout.Add(content, cbPlaySoundAfterCapture);
            layout.Add(content, cbPlaySoundAfterUpload);
            layout.Add(content, cbPlaySoundAfterAction);
            layout.Heading(content, "弹窗显示", "Notification popup");
            layout.Add(content, cbShowToastNotificationAfterTaskCompleted);

            var popup = layout.Stack();
            layout.Add(popup, layout.Row(lblToastWindowDuration, layout.Inline(nudToastWindowDuration, lblToastWindowDurationSeconds)));
            layout.Add(popup, layout.Row(lblToastWindowFadeDuration, layout.Inline(nudToastWindowFadeDuration, lblToastWindowFadeDurationSeconds)));
            layout.Add(popup, layout.Row(lblToastWindowPlacement, cbToastWindowPlacement));
            layout.Add(popup, layout.Row(lblToastWindowSize, layout.Inline(nudToastWindowSizeWidth, lblToastWindowSizeX, nudToastWindowSizeHeight)));
            layout.Add(popup, cbToastWindowAutoHide);
            layout.Add(popup, cbDisableNotificationsOnFullscreen);
            layout.Heading(popup, "弹窗点击动作", "Popup click actions");
            layout.Add(popup, layout.Row(lblToastWindowLeftClickAction, cbToastWindowLeftClickAction));
            layout.Add(popup, layout.Row(lblToastWindowRightClickAction, cbToastWindowRightClickAction));
            layout.Add(popup, layout.Row(lblToastWindowMiddleClickAction, cbToastWindowMiddleClickAction));
            // Keep the existing enabled/disabled scope tied to the popup checkbox.
            gbToastWindow.Text = "";
            // The group has a measured height. Letting both it and its nested tables
            // auto-size repeatedly measures the entire popup subtree on each layout.
            gbToastWindow.AutoSize = false;
            gbToastWindow.Padding = new Padding(layout.Scale(10));
            popup.SizeChanged += (sender, e) =>
            {
                int height = popup.Height + gbToastWindow.Height - gbToastWindow.DisplayRectangle.Height;
                if (gbToastWindow.Height != height) gbToastWindow.Height = height;
            };
            gbToastWindow.Controls.Add(popup);
            layout.Add(content, gbToastWindow);

            layout.Heading(content, "自定义声音", "Custom sounds");
            void AddSound(CheckBox enabled, TextBox path, Button browse)
            {
                layout.Add(content, enabled);
                var row = new TableLayoutPanel
                {
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, Size = new Size(1, 1)
                };
                layout.Defer(row);
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                path.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                path.Margin = new Padding(0, layout.Scale(3), layout.Scale(8), layout.Scale(3));
                browse.AutoSize = true;
                browse.MinimumSize = new Size(layout.Scale(32), layout.Scale(26));
                browse.Margin = Padding.Empty;
                row.Controls.Add(path, 0, 0);
                row.Controls.Add(browse, 1, 0);
                layout.Add(content, row);
            }
            AddSound(cbUseCustomCaptureSound, txtCustomCaptureSoundPath, btnCustomCaptureSoundPath);
            AddSound(cbUseCustomTaskCompletedSound, txtCustomTaskCompletedSoundPath, btnCustomTaskCompletedSoundPath);
            AddSound(cbUseCustomActionCompletedSound, txtCustomActionCompletedSoundPath, btnCustomActionCompletedSoundPath);
            AddSound(cbUseCustomErrorSound, txtCustomErrorSoundPath, btnCustomErrorSoundPath);
            layout.Install(this, content);
        }

        public void LoadSettings()
        {
            updating = true;
            try
            {
                cbToastWindowPlacement.Items.Clear();
                cbToastWindowLeftClickAction.Items.Clear();
                cbToastWindowRightClickAction.Items.Clear();
                cbToastWindowMiddleClickAction.Items.Clear();
                cbPlaySoundAfterCapture.Checked = Settings.PlaySoundAfterCapture;
                cbPlaySoundAfterUpload.Checked = Settings.PlaySoundAfterUpload;
                cbPlaySoundAfterAction.Checked = Settings.PlaySoundAfterAction;
                cbShowToastNotificationAfterTaskCompleted.Checked = Settings.ShowToastNotificationAfterTaskCompleted;
                gbToastWindow.Enabled = Settings.ShowToastNotificationAfterTaskCompleted;
                nudToastWindowDuration.SetValue((decimal)Settings.ToastWindowDuration);
                nudToastWindowFadeDuration.SetValue((decimal)Settings.ToastWindowFadeDuration);
                cbToastWindowPlacement.Items.AddRange(Helpers.GetLocalizedEnumDescriptions<ContentAlignment>());
                cbToastWindowPlacement.SelectedIndex = Settings.ToastWindowPlacement.GetIndex();
                nudToastWindowSizeWidth.SetValue(Settings.ToastWindowSize.Width);
                nudToastWindowSizeHeight.SetValue(Settings.ToastWindowSize.Height);
                cbToastWindowLeftClickAction.Items.AddRange(Helpers.GetLocalizedEnumDescriptions<ToastClickAction>());
                cbToastWindowLeftClickAction.SelectedIndex = (int)Settings.ToastWindowLeftClickAction;
                cbToastWindowRightClickAction.Items.AddRange(Helpers.GetLocalizedEnumDescriptions<ToastClickAction>());
                cbToastWindowRightClickAction.SelectedIndex = (int)Settings.ToastWindowRightClickAction;
                cbToastWindowMiddleClickAction.Items.AddRange(Helpers.GetLocalizedEnumDescriptions<ToastClickAction>());
                cbToastWindowMiddleClickAction.SelectedIndex = (int)Settings.ToastWindowMiddleClickAction;
                cbToastWindowAutoHide.Checked = Settings.ToastWindowAutoHide;
                cbDisableNotificationsOnFullscreen.Checked = Settings.DisableNotificationsOnFullscreen;
                cbUseCustomCaptureSound.Checked = Settings.UseCustomCaptureSound;
                txtCustomCaptureSoundPath.Enabled = btnCustomCaptureSoundPath.Enabled = Settings.UseCustomCaptureSound;
                txtCustomCaptureSoundPath.Text = Settings.CustomCaptureSoundPath;
                cbUseCustomTaskCompletedSound.Checked = Settings.UseCustomTaskCompletedSound;
                txtCustomTaskCompletedSoundPath.Enabled = btnCustomTaskCompletedSoundPath.Enabled = Settings.UseCustomTaskCompletedSound;
                txtCustomTaskCompletedSoundPath.Text = Settings.CustomTaskCompletedSoundPath;
                cbUseCustomActionCompletedSound.Checked = Settings.UseCustomActionCompletedSound;
                txtCustomActionCompletedSoundPath.Enabled = btnCustomActionCompletedSoundPath.Enabled = Settings.UseCustomActionCompletedSound;
                txtCustomActionCompletedSoundPath.Text = Settings.CustomActionCompletedSoundPath;
                cbUseCustomErrorSound.Checked = Settings.UseCustomErrorSound;
                txtCustomErrorSoundPath.Enabled = btnCustomErrorSoundPath.Enabled = Settings.UseCustomErrorSound;
                txtCustomErrorSoundPath.Text = Settings.CustomErrorSoundPath;
            }
            finally
            {
                updating = false;
            }
        }

        private void InitializeComponent()
        {
            // Reuse the existing translated captions, positions, and limits.
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            btnCustomActionCompletedSoundPath = new System.Windows.Forms.Button();
            txtCustomActionCompletedSoundPath = new System.Windows.Forms.TextBox();
            cbUseCustomActionCompletedSound = new System.Windows.Forms.CheckBox();
            cbPlaySoundAfterAction = new System.Windows.Forms.CheckBox();
            cbShowToastNotificationAfterTaskCompleted = new System.Windows.Forms.CheckBox();
            btnCustomErrorSoundPath = new System.Windows.Forms.Button();
            btnCustomTaskCompletedSoundPath = new System.Windows.Forms.Button();
            btnCustomCaptureSoundPath = new System.Windows.Forms.Button();
            txtCustomErrorSoundPath = new System.Windows.Forms.TextBox();
            txtCustomTaskCompletedSoundPath = new System.Windows.Forms.TextBox();
            txtCustomCaptureSoundPath = new System.Windows.Forms.TextBox();
            cbUseCustomErrorSound = new System.Windows.Forms.CheckBox();
            cbUseCustomTaskCompletedSound = new System.Windows.Forms.CheckBox();
            cbUseCustomCaptureSound = new System.Windows.Forms.CheckBox();
            gbToastWindow = new System.Windows.Forms.GroupBox();
            cbToastWindowAutoHide = new System.Windows.Forms.CheckBox();
            lblToastWindowFadeDurationSeconds = new System.Windows.Forms.Label();
            lblToastWindowDurationSeconds = new System.Windows.Forms.Label();
            lblToastWindowSizeX = new System.Windows.Forms.Label();
            cbToastWindowMiddleClickAction = new System.Windows.Forms.ComboBox();
            cbToastWindowRightClickAction = new System.Windows.Forms.ComboBox();
            cbToastWindowLeftClickAction = new System.Windows.Forms.ComboBox();
            nudToastWindowSizeHeight = new System.Windows.Forms.NumericUpDown();
            nudToastWindowSizeWidth = new System.Windows.Forms.NumericUpDown();
            cbToastWindowPlacement = new PlacementComboBox();
            nudToastWindowFadeDuration = new System.Windows.Forms.NumericUpDown();
            cbDisableNotificationsOnFullscreen = new System.Windows.Forms.CheckBox();
            nudToastWindowDuration = new System.Windows.Forms.NumericUpDown();
            lblToastWindowMiddleClickAction = new System.Windows.Forms.Label();
            lblToastWindowRightClickAction = new System.Windows.Forms.Label();
            lblToastWindowLeftClickAction = new System.Windows.Forms.Label();
            lblToastWindowSize = new System.Windows.Forms.Label();
            lblToastWindowPlacement = new System.Windows.Forms.Label();
            lblToastWindowFadeDuration = new System.Windows.Forms.Label();
            lblToastWindowDuration = new System.Windows.Forms.Label();
            cbPlaySoundAfterCapture = new System.Windows.Forms.CheckBox();
            cbPlaySoundAfterUpload = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowSizeHeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowSizeWidth).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowFadeDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowDuration).BeginInit();
            gbToastWindow.SuspendLayout();
            SuspendLayout();
            // 
            // tpNotifications
            // 
            Controls.Add(btnCustomActionCompletedSoundPath);
            Controls.Add(txtCustomActionCompletedSoundPath);
            Controls.Add(cbUseCustomActionCompletedSound);
            Controls.Add(cbPlaySoundAfterAction);
            Controls.Add(cbShowToastNotificationAfterTaskCompleted);
            Controls.Add(btnCustomErrorSoundPath);
            Controls.Add(btnCustomTaskCompletedSoundPath);
            Controls.Add(btnCustomCaptureSoundPath);
            Controls.Add(txtCustomErrorSoundPath);
            Controls.Add(txtCustomTaskCompletedSoundPath);
            Controls.Add(txtCustomCaptureSoundPath);
            Controls.Add(cbUseCustomErrorSound);
            Controls.Add(cbUseCustomTaskCompletedSound);
            Controls.Add(cbUseCustomCaptureSound);
            Controls.Add(gbToastWindow);
            Controls.Add(cbPlaySoundAfterCapture);
            Controls.Add(cbPlaySoundAfterUpload);
            resources.ApplyResources(this, "tpNotifications");
            Name = "NotificationSettingsControl";
            // 
            // btnCustomActionCompletedSoundPath
            // 
            resources.ApplyResources(btnCustomActionCompletedSoundPath, "btnCustomActionCompletedSoundPath");
            btnCustomActionCompletedSoundPath.Name = "btnCustomActionCompletedSoundPath";
            btnCustomActionCompletedSoundPath.UseVisualStyleBackColor = true;
            btnCustomActionCompletedSoundPath.Click += btnCustomActionCompletedSoundPath_Click;
            // 
            // txtCustomActionCompletedSoundPath
            // 
            resources.ApplyResources(txtCustomActionCompletedSoundPath, "txtCustomActionCompletedSoundPath");
            txtCustomActionCompletedSoundPath.Name = "txtCustomActionCompletedSoundPath";
            txtCustomActionCompletedSoundPath.TextChanged += txtCustomActionCompletedSoundPath_TextChanged;
            // 
            // cbUseCustomActionCompletedSound
            // 
            resources.ApplyResources(cbUseCustomActionCompletedSound, "cbUseCustomActionCompletedSound");
            cbUseCustomActionCompletedSound.Name = "cbUseCustomActionCompletedSound";
            cbUseCustomActionCompletedSound.UseVisualStyleBackColor = true;
            cbUseCustomActionCompletedSound.CheckedChanged += cbUseCustomActionCompletedSound_CheckedChanged;
            // 
            // cbPlaySoundAfterAction
            // 
            resources.ApplyResources(cbPlaySoundAfterAction, "cbPlaySoundAfterAction");
            cbPlaySoundAfterAction.Name = "cbPlaySoundAfterAction";
            cbPlaySoundAfterAction.UseVisualStyleBackColor = true;
            cbPlaySoundAfterAction.CheckedChanged += cbPlaySoundAfterAction_CheckedChanged;
            // 
            // cbShowToastNotificationAfterTaskCompleted
            // 
            resources.ApplyResources(cbShowToastNotificationAfterTaskCompleted, "cbShowToastNotificationAfterTaskCompleted");
            cbShowToastNotificationAfterTaskCompleted.Name = "cbShowToastNotificationAfterTaskCompleted";
            cbShowToastNotificationAfterTaskCompleted.UseVisualStyleBackColor = true;
            cbShowToastNotificationAfterTaskCompleted.CheckedChanged += cbShowToastNotificationAfterTaskCompleted_CheckedChanged;
            // 
            // btnCustomErrorSoundPath
            // 
            resources.ApplyResources(btnCustomErrorSoundPath, "btnCustomErrorSoundPath");
            btnCustomErrorSoundPath.Name = "btnCustomErrorSoundPath";
            btnCustomErrorSoundPath.UseVisualStyleBackColor = true;
            btnCustomErrorSoundPath.Click += btnCustomErrorSoundPath_Click;
            // 
            // btnCustomTaskCompletedSoundPath
            // 
            resources.ApplyResources(btnCustomTaskCompletedSoundPath, "btnCustomTaskCompletedSoundPath");
            btnCustomTaskCompletedSoundPath.Name = "btnCustomTaskCompletedSoundPath";
            btnCustomTaskCompletedSoundPath.UseVisualStyleBackColor = true;
            btnCustomTaskCompletedSoundPath.Click += btnCustomTaskCompletedSoundPath_Click;
            // 
            // btnCustomCaptureSoundPath
            // 
            resources.ApplyResources(btnCustomCaptureSoundPath, "btnCustomCaptureSoundPath");
            btnCustomCaptureSoundPath.Name = "btnCustomCaptureSoundPath";
            btnCustomCaptureSoundPath.UseVisualStyleBackColor = true;
            btnCustomCaptureSoundPath.Click += btnCustomCaptureSoundPath_Click;
            // 
            // txtCustomErrorSoundPath
            // 
            resources.ApplyResources(txtCustomErrorSoundPath, "txtCustomErrorSoundPath");
            txtCustomErrorSoundPath.Name = "txtCustomErrorSoundPath";
            txtCustomErrorSoundPath.TextChanged += txtCustomErrorSoundPath_TextChanged;
            // 
            // txtCustomTaskCompletedSoundPath
            // 
            resources.ApplyResources(txtCustomTaskCompletedSoundPath, "txtCustomTaskCompletedSoundPath");
            txtCustomTaskCompletedSoundPath.Name = "txtCustomTaskCompletedSoundPath";
            txtCustomTaskCompletedSoundPath.TextChanged += txtCustomTaskCompletedSoundPath_TextChanged;
            // 
            // txtCustomCaptureSoundPath
            // 
            resources.ApplyResources(txtCustomCaptureSoundPath, "txtCustomCaptureSoundPath");
            txtCustomCaptureSoundPath.Name = "txtCustomCaptureSoundPath";
            txtCustomCaptureSoundPath.TextChanged += txtCustomCaptureSoundPath_TextChanged;
            // 
            // cbUseCustomErrorSound
            // 
            resources.ApplyResources(cbUseCustomErrorSound, "cbUseCustomErrorSound");
            cbUseCustomErrorSound.Name = "cbUseCustomErrorSound";
            cbUseCustomErrorSound.UseVisualStyleBackColor = true;
            cbUseCustomErrorSound.CheckedChanged += cbUseCustomErrorSound_CheckedChanged;
            // 
            // cbUseCustomTaskCompletedSound
            // 
            resources.ApplyResources(cbUseCustomTaskCompletedSound, "cbUseCustomTaskCompletedSound");
            cbUseCustomTaskCompletedSound.Name = "cbUseCustomTaskCompletedSound";
            cbUseCustomTaskCompletedSound.UseVisualStyleBackColor = true;
            cbUseCustomTaskCompletedSound.CheckedChanged += cbUseCustomTaskCompletedSound_CheckedChanged;
            // 
            // cbUseCustomCaptureSound
            // 
            resources.ApplyResources(cbUseCustomCaptureSound, "cbUseCustomCaptureSound");
            cbUseCustomCaptureSound.Name = "cbUseCustomCaptureSound";
            cbUseCustomCaptureSound.UseVisualStyleBackColor = true;
            cbUseCustomCaptureSound.CheckedChanged += cbUseCustomCaptureSound_CheckedChanged;
            // 
            // gbToastWindow
            // 
            gbToastWindow.Controls.Add(cbToastWindowAutoHide);
            gbToastWindow.Controls.Add(lblToastWindowFadeDurationSeconds);
            gbToastWindow.Controls.Add(lblToastWindowDurationSeconds);
            gbToastWindow.Controls.Add(lblToastWindowSizeX);
            gbToastWindow.Controls.Add(cbToastWindowMiddleClickAction);
            gbToastWindow.Controls.Add(cbToastWindowRightClickAction);
            gbToastWindow.Controls.Add(cbToastWindowLeftClickAction);
            gbToastWindow.Controls.Add(nudToastWindowSizeHeight);
            gbToastWindow.Controls.Add(nudToastWindowSizeWidth);
            gbToastWindow.Controls.Add(cbToastWindowPlacement);
            gbToastWindow.Controls.Add(nudToastWindowFadeDuration);
            gbToastWindow.Controls.Add(cbDisableNotificationsOnFullscreen);
            gbToastWindow.Controls.Add(nudToastWindowDuration);
            gbToastWindow.Controls.Add(lblToastWindowMiddleClickAction);
            gbToastWindow.Controls.Add(lblToastWindowRightClickAction);
            gbToastWindow.Controls.Add(lblToastWindowLeftClickAction);
            gbToastWindow.Controls.Add(lblToastWindowSize);
            gbToastWindow.Controls.Add(lblToastWindowPlacement);
            gbToastWindow.Controls.Add(lblToastWindowFadeDuration);
            gbToastWindow.Controls.Add(lblToastWindowDuration);
            resources.ApplyResources(gbToastWindow, "gbToastWindow");
            gbToastWindow.Name = "gbToastWindow";
            gbToastWindow.TabStop = false;
            // 
            // cbToastWindowAutoHide
            // 
            resources.ApplyResources(cbToastWindowAutoHide, "cbToastWindowAutoHide");
            cbToastWindowAutoHide.Name = "cbToastWindowAutoHide";
            cbToastWindowAutoHide.UseVisualStyleBackColor = true;
            cbToastWindowAutoHide.CheckedChanged += cbToastWindowAutoHide_CheckedChanged;
            // 
            // lblToastWindowFadeDurationSeconds
            // 
            resources.ApplyResources(lblToastWindowFadeDurationSeconds, "lblToastWindowFadeDurationSeconds");
            lblToastWindowFadeDurationSeconds.Name = "lblToastWindowFadeDurationSeconds";
            // 
            // lblToastWindowDurationSeconds
            // 
            resources.ApplyResources(lblToastWindowDurationSeconds, "lblToastWindowDurationSeconds");
            lblToastWindowDurationSeconds.Name = "lblToastWindowDurationSeconds";
            // 
            // lblToastWindowSizeX
            // 
            resources.ApplyResources(lblToastWindowSizeX, "lblToastWindowSizeX");
            lblToastWindowSizeX.Name = "lblToastWindowSizeX";
            // 
            // cbToastWindowMiddleClickAction
            // 
            cbToastWindowMiddleClickAction.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbToastWindowMiddleClickAction.FormattingEnabled = true;
            resources.ApplyResources(cbToastWindowMiddleClickAction, "cbToastWindowMiddleClickAction");
            cbToastWindowMiddleClickAction.Name = "cbToastWindowMiddleClickAction";
            cbToastWindowMiddleClickAction.SelectedIndexChanged += cbToastWindowMiddleClickAction_SelectedIndexChanged;
            // 
            // cbToastWindowRightClickAction
            // 
            cbToastWindowRightClickAction.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbToastWindowRightClickAction.FormattingEnabled = true;
            resources.ApplyResources(cbToastWindowRightClickAction, "cbToastWindowRightClickAction");
            cbToastWindowRightClickAction.Name = "cbToastWindowRightClickAction";
            cbToastWindowRightClickAction.SelectedIndexChanged += cbToastWindowRightClickAction_SelectedIndexChanged;
            // 
            // cbToastWindowLeftClickAction
            // 
            cbToastWindowLeftClickAction.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbToastWindowLeftClickAction.FormattingEnabled = true;
            resources.ApplyResources(cbToastWindowLeftClickAction, "cbToastWindowLeftClickAction");
            cbToastWindowLeftClickAction.Name = "cbToastWindowLeftClickAction";
            cbToastWindowLeftClickAction.SelectedIndexChanged += cbToastWindowLeftClickAction_SelectedIndexChanged;
            // 
            // nudToastWindowSizeHeight
            // 
            resources.ApplyResources(nudToastWindowSizeHeight, "nudToastWindowSizeHeight");
            nudToastWindowSizeHeight.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudToastWindowSizeHeight.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            nudToastWindowSizeHeight.Name = "nudToastWindowSizeHeight";
            nudToastWindowSizeHeight.Value = new decimal(new int[] { 100, 0, 0, 0 });
            nudToastWindowSizeHeight.ValueChanged += nudToastWindowSizeHeight_ValueChanged;
            // 
            // nudToastWindowSizeWidth
            // 
            resources.ApplyResources(nudToastWindowSizeWidth, "nudToastWindowSizeWidth");
            nudToastWindowSizeWidth.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            nudToastWindowSizeWidth.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            nudToastWindowSizeWidth.Name = "nudToastWindowSizeWidth";
            nudToastWindowSizeWidth.Value = new decimal(new int[] { 100, 0, 0, 0 });
            nudToastWindowSizeWidth.ValueChanged += nudToastWindowSizeWidth_ValueChanged;
            // 
            // cbToastWindowPlacement
            // 
            cbToastWindowPlacement.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            cbToastWindowPlacement.FormattingEnabled = true;
            resources.ApplyResources(cbToastWindowPlacement, "cbToastWindowPlacement");
            cbToastWindowPlacement.Name = "cbToastWindowPlacement";
            cbToastWindowPlacement.SelectionChangeCommitted += cbToastWindowPlacement_SelectedIndexChanged;
            // 
            // nudToastWindowFadeDuration
            // 
            nudToastWindowFadeDuration.DecimalPlaces = 1;
            resources.ApplyResources(nudToastWindowFadeDuration, "nudToastWindowFadeDuration");
            nudToastWindowFadeDuration.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            nudToastWindowFadeDuration.Name = "nudToastWindowFadeDuration";
            nudToastWindowFadeDuration.ValueChanged += nudToastWindowFadeDuration_ValueChanged;
            // 
            // cbDisableNotificationsOnFullscreen
            // 
            resources.ApplyResources(cbDisableNotificationsOnFullscreen, "cbDisableNotificationsOnFullscreen");
            cbDisableNotificationsOnFullscreen.Name = "cbDisableNotificationsOnFullscreen";
            cbDisableNotificationsOnFullscreen.UseVisualStyleBackColor = true;
            cbDisableNotificationsOnFullscreen.CheckedChanged += cbDisableNotificationsOnFullscreen_CheckedChanged;
            // 
            // nudToastWindowDuration
            // 
            nudToastWindowDuration.DecimalPlaces = 1;
            resources.ApplyResources(nudToastWindowDuration, "nudToastWindowDuration");
            nudToastWindowDuration.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            nudToastWindowDuration.Name = "nudToastWindowDuration";
            nudToastWindowDuration.ValueChanged += nudToastWindowDuration_ValueChanged;
            // 
            // lblToastWindowMiddleClickAction
            // 
            resources.ApplyResources(lblToastWindowMiddleClickAction, "lblToastWindowMiddleClickAction");
            lblToastWindowMiddleClickAction.Name = "lblToastWindowMiddleClickAction";
            // 
            // lblToastWindowRightClickAction
            // 
            resources.ApplyResources(lblToastWindowRightClickAction, "lblToastWindowRightClickAction");
            lblToastWindowRightClickAction.Name = "lblToastWindowRightClickAction";
            // 
            // lblToastWindowLeftClickAction
            // 
            resources.ApplyResources(lblToastWindowLeftClickAction, "lblToastWindowLeftClickAction");
            lblToastWindowLeftClickAction.Name = "lblToastWindowLeftClickAction";
            // 
            // lblToastWindowSize
            // 
            resources.ApplyResources(lblToastWindowSize, "lblToastWindowSize");
            lblToastWindowSize.Name = "lblToastWindowSize";
            // 
            // lblToastWindowPlacement
            // 
            resources.ApplyResources(lblToastWindowPlacement, "lblToastWindowPlacement");
            lblToastWindowPlacement.Name = "lblToastWindowPlacement";
            // 
            // lblToastWindowFadeDuration
            // 
            resources.ApplyResources(lblToastWindowFadeDuration, "lblToastWindowFadeDuration");
            lblToastWindowFadeDuration.Name = "lblToastWindowFadeDuration";
            // 
            // lblToastWindowDuration
            // 
            resources.ApplyResources(lblToastWindowDuration, "lblToastWindowDuration");
            lblToastWindowDuration.Name = "lblToastWindowDuration";
            // 
            // cbPlaySoundAfterCapture
            // 
            resources.ApplyResources(cbPlaySoundAfterCapture, "cbPlaySoundAfterCapture");
            cbPlaySoundAfterCapture.Name = "cbPlaySoundAfterCapture";
            cbPlaySoundAfterCapture.UseVisualStyleBackColor = true;
            cbPlaySoundAfterCapture.CheckedChanged += cbPlaySoundAfterCapture_CheckedChanged;
            // 
            // cbPlaySoundAfterUpload
            // 
            resources.ApplyResources(cbPlaySoundAfterUpload, "cbPlaySoundAfterUpload");
            cbPlaySoundAfterUpload.Name = "cbPlaySoundAfterUpload";
            cbPlaySoundAfterUpload.UseVisualStyleBackColor = true;
            cbPlaySoundAfterUpload.CheckedChanged += cbPlaySoundAfterUpload_CheckedChanged;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            ((System.ComponentModel.ISupportInitialize)nudToastWindowSizeHeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowSizeWidth).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowFadeDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudToastWindowDuration).EndInit();
            gbToastWindow.ResumeLayout(false);
            gbToastWindow.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        private void cbPlaySoundAfterCapture_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.PlaySoundAfterCapture = cbPlaySoundAfterCapture.Checked;
        }

        private void cbPlaySoundAfterUpload_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.PlaySoundAfterUpload = cbPlaySoundAfterUpload.Checked;
        }

        private void cbPlaySoundAfterAction_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.PlaySoundAfterAction = cbPlaySoundAfterAction.Checked;
        }

        private void cbShowToastNotificationAfterTaskCompleted_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ShowToastNotificationAfterTaskCompleted = cbShowToastNotificationAfterTaskCompleted.Checked;
            gbToastWindow.Enabled = Settings.ShowToastNotificationAfterTaskCompleted;
        }

        private void nudToastWindowDuration_ValueChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowDuration = (float)nudToastWindowDuration.Value;
        }

        private void nudToastWindowFadeDuration_ValueChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowFadeDuration = (float)nudToastWindowFadeDuration.Value;
        }

        private void cbToastWindowPlacement_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowPlacement = Helpers.GetEnumFromIndex<ContentAlignment>(cbToastWindowPlacement.SelectedIndex);
        }

        private sealed class PlacementComboBox : ComboBox
        {
            protected override void WndProc(ref Message m)
            {
                // A closed dropdown must not silently move BottomRight to the
                // adjacent BottomCenter while the user scrolls the settings page.
                if (m.Msg == 0x020A && !DroppedDown) return; // WM_MOUSEWHEEL
                base.WndProc(ref m);
            }
        }

        private void nudToastWindowSizeWidth_ValueChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowSize = new Size((int)nudToastWindowSizeWidth.Value, Settings.ToastWindowSize.Height);
        }

        private void nudToastWindowSizeHeight_ValueChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowSize = new Size(Settings.ToastWindowSize.Width, (int)nudToastWindowSizeHeight.Value);
        }

        private void cbToastWindowLeftClickAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowLeftClickAction = (ToastClickAction)cbToastWindowLeftClickAction.SelectedIndex;
        }

        private void cbToastWindowRightClickAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowRightClickAction = (ToastClickAction)cbToastWindowRightClickAction.SelectedIndex;
        }

        private void cbToastWindowMiddleClickAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowMiddleClickAction = (ToastClickAction)cbToastWindowMiddleClickAction.SelectedIndex;
        }

        private void cbToastWindowAutoHide_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.ToastWindowAutoHide = cbToastWindowAutoHide.Checked;
        }

        private void cbDisableNotificationsOnFullscreen_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.DisableNotificationsOnFullscreen = cbDisableNotificationsOnFullscreen.Checked;
        }

        private void cbUseCustomCaptureSound_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.UseCustomCaptureSound = cbUseCustomCaptureSound.Checked;
            txtCustomCaptureSoundPath.Enabled = btnCustomCaptureSoundPath.Enabled = Settings.UseCustomCaptureSound;
        }

        private void txtCustomCaptureSoundPath_TextChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.CustomCaptureSoundPath = txtCustomCaptureSoundPath.Text;
        }

        private void btnCustomCaptureSoundPath_Click(object sender, EventArgs e)
        {
            if (updating) return;

            FileHelpers.BrowseFile(txtCustomCaptureSoundPath, filter: "Audio file (*.wav)|*.wav");
        }

        private void cbUseCustomTaskCompletedSound_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.UseCustomTaskCompletedSound = cbUseCustomTaskCompletedSound.Checked;
            txtCustomTaskCompletedSoundPath.Enabled = btnCustomTaskCompletedSoundPath.Enabled = Settings.UseCustomTaskCompletedSound;
        }

        private void txtCustomTaskCompletedSoundPath_TextChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.CustomTaskCompletedSoundPath = txtCustomTaskCompletedSoundPath.Text;
        }

        private void btnCustomTaskCompletedSoundPath_Click(object sender, EventArgs e)
        {
            if (updating) return;

            FileHelpers.BrowseFile(txtCustomTaskCompletedSoundPath, filter: "Audio file (*.wav)|*.wav");
        }

        private void cbUseCustomActionCompletedSound_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.UseCustomActionCompletedSound = cbUseCustomActionCompletedSound.Checked;
            txtCustomActionCompletedSoundPath.Enabled = btnCustomActionCompletedSoundPath.Enabled = Settings.UseCustomActionCompletedSound;
        }

        private void txtCustomActionCompletedSoundPath_TextChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.CustomActionCompletedSoundPath = txtCustomActionCompletedSoundPath.Text;
        }

        private void btnCustomActionCompletedSoundPath_Click(object sender, EventArgs e)
        {
            if (updating) return;

            FileHelpers.BrowseFile(txtCustomActionCompletedSoundPath, filter: "Audio file (*.wav)|*.wav");
        }

        private void cbUseCustomErrorSound_CheckedChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.UseCustomErrorSound = cbUseCustomErrorSound.Checked;
            txtCustomErrorSoundPath.Enabled = btnCustomErrorSoundPath.Enabled = Settings.UseCustomErrorSound;
        }

        private void txtCustomErrorSoundPath_TextChanged(object sender, EventArgs e)
        {
            if (updating) return;

            Settings.CustomErrorSoundPath = txtCustomErrorSoundPath.Text;
        }

        private void btnCustomErrorSoundPath_Click(object sender, EventArgs e)
        {
            if (updating) return;

            FileHelpers.BrowseFile(txtCustomErrorSoundPath, filter: "Audio file (*.wav)|*.wav");
        }

        private System.Windows.Forms.Button btnCustomActionCompletedSoundPath;
        private System.Windows.Forms.TextBox txtCustomActionCompletedSoundPath;
        private System.Windows.Forms.CheckBox cbUseCustomActionCompletedSound;
        private System.Windows.Forms.CheckBox cbPlaySoundAfterAction;
        private System.Windows.Forms.CheckBox cbShowToastNotificationAfterTaskCompleted;
        private System.Windows.Forms.Button btnCustomErrorSoundPath;
        private System.Windows.Forms.Button btnCustomTaskCompletedSoundPath;
        private System.Windows.Forms.Button btnCustomCaptureSoundPath;
        private System.Windows.Forms.TextBox txtCustomErrorSoundPath;
        private System.Windows.Forms.TextBox txtCustomTaskCompletedSoundPath;
        private System.Windows.Forms.TextBox txtCustomCaptureSoundPath;
        private System.Windows.Forms.CheckBox cbUseCustomErrorSound;
        private System.Windows.Forms.CheckBox cbUseCustomTaskCompletedSound;
        private System.Windows.Forms.CheckBox cbUseCustomCaptureSound;
        private System.Windows.Forms.GroupBox gbToastWindow;
        private System.Windows.Forms.CheckBox cbToastWindowAutoHide;
        private System.Windows.Forms.Label lblToastWindowFadeDurationSeconds;
        private System.Windows.Forms.Label lblToastWindowDurationSeconds;
        private System.Windows.Forms.Label lblToastWindowSizeX;
        private System.Windows.Forms.ComboBox cbToastWindowMiddleClickAction;
        private System.Windows.Forms.ComboBox cbToastWindowRightClickAction;
        private System.Windows.Forms.ComboBox cbToastWindowLeftClickAction;
        private System.Windows.Forms.NumericUpDown nudToastWindowSizeHeight;
        private System.Windows.Forms.NumericUpDown nudToastWindowSizeWidth;
        private System.Windows.Forms.ComboBox cbToastWindowPlacement;
        private System.Windows.Forms.NumericUpDown nudToastWindowFadeDuration;
        private System.Windows.Forms.CheckBox cbDisableNotificationsOnFullscreen;
        private System.Windows.Forms.NumericUpDown nudToastWindowDuration;
        private System.Windows.Forms.Label lblToastWindowMiddleClickAction;
        private System.Windows.Forms.Label lblToastWindowRightClickAction;
        private System.Windows.Forms.Label lblToastWindowLeftClickAction;
        private System.Windows.Forms.Label lblToastWindowSize;
        private System.Windows.Forms.Label lblToastWindowPlacement;
        private System.Windows.Forms.Label lblToastWindowFadeDuration;
        private System.Windows.Forms.Label lblToastWindowDuration;
        private System.Windows.Forms.CheckBox cbPlaySoundAfterCapture;
        private System.Windows.Forms.CheckBox cbPlaySoundAfterUpload;
    }
}
