using ShareX.HelpersLib;
namespace ShareX
{
    partial class ApplicationSettingsForm
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ApplicationSettingsForm));
            this.tcSettings = new System.Windows.Forms.TabControl();
            this.tpGeneral = new System.Windows.Forms.TabPage();
            this.cbStartWithWindows = new System.Windows.Forms.CheckBox();
            this.tpPaths = new System.Windows.Forms.TabPage();
            this.tpAdvanced = new System.Windows.Forms.TabPage();
            this.tttvMain = new ShareX.HelpersLib.TabToTreeView();
            this.tcSettings.SuspendLayout();
            this.tpGeneral.SuspendLayout();
            this.tpPaths.SuspendLayout();
            this.tpAdvanced.SuspendLayout();
            this.SuspendLayout();
            // 
            // tcSettings
            // 
            resources.ApplyResources(this.tcSettings, "tcSettings");
            this.tcSettings.Controls.Add(this.tpGeneral);
            this.tcSettings.Controls.Add(this.tpPaths);
            this.tcSettings.Controls.Add(this.tpAdvanced);
            this.tcSettings.Name = "tcSettings";
            this.tcSettings.SelectedIndex = 0;
            // 
            // tpGeneral
            // 
            this.tpGeneral.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(this.tpGeneral, "tpGeneral");
            this.tpGeneral.Name = "tpGeneral";
            // 
            // cbStartWithWindows
            // 
            resources.ApplyResources(this.cbStartWithWindows, "cbStartWithWindows");
            this.cbStartWithWindows.Name = "cbStartWithWindows";
            this.cbStartWithWindows.UseVisualStyleBackColor = true;
            this.cbStartWithWindows.CheckedChanged += new System.EventHandler(this.cbStartWithWindows_CheckedChanged);
            // 
            // tpPaths
            // 
            this.tpPaths.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(this.tpPaths, "tpPaths");
            this.tpPaths.Name = "tpPaths";
            // 
            // tpAdvanced
            // 
            this.tpAdvanced.BackColor = System.Drawing.SystemColors.Window;
            resources.ApplyResources(this.tpAdvanced, "tpAdvanced");
            this.tpAdvanced.Name = "tpAdvanced";
            // 
            // tttvMain
            // 
            resources.ApplyResources(this.tttvMain, "tttvMain");
            this.tttvMain.ImageList = null;
            this.tttvMain.LeftPanelBackColor = System.Drawing.SystemColors.Window;
            this.tttvMain.MainTabControl = null;
            this.tttvMain.Name = "tttvMain";
            this.tttvMain.SeparatorColor = System.Drawing.SystemColors.ControlDark;
            this.tttvMain.TreeViewFont = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.tttvMain.TreeViewSize = 175;
            this.tttvMain.TabChanged += new ShareX.HelpersLib.TabToTreeView.TabChangedEventHandler(this.tttvMain_TabChanged);
            // 
            // ApplicationSettingsForm
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.tcSettings);
            this.Controls.Add(this.tttvMain);
            this.Name = "ApplicationSettingsForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Shown += new System.EventHandler(this.SettingsForm_Shown);
            this.Resize += new System.EventHandler(this.SettingsForm_Resize);
            this.tcSettings.ResumeLayout(false);
            this.tpGeneral.ResumeLayout(false);
            this.tpGeneral.PerformLayout();
            this.tpPaths.ResumeLayout(false);
            this.tpPaths.PerformLayout();
            this.tpAdvanced.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        private void InitializeAdvancedPageComponents()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(ApplicationSettingsForm));
            this.pgSettings = new System.Windows.Forms.PropertyGrid();
            this.tpAdvanced.SuspendLayout();
            this.SuspendLayout();
            // 
            // tpAdvanced
            // 
            this.tpAdvanced.BackColor = System.Drawing.SystemColors.Window;
            this.tpAdvanced.Controls.Add(this.pgSettings);
            resources.ApplyResources(this.tpAdvanced, "tpAdvanced");
            this.tpAdvanced.Name = "tpAdvanced";
            // 
            // pgSettings
            // 
            resources.ApplyResources(this.pgSettings, "pgSettings");
            this.pgSettings.Name = "pgSettings";
            this.pgSettings.PropertySort = System.Windows.Forms.PropertySort.Categorized;
            this.pgSettings.ToolbarVisible = false;
            this.tpAdvanced.ResumeLayout(false);
            this.ResumeLayout(false);
        }


   

        #endregion Windows Form Designer generated code

        private System.Windows.Forms.TabControl tcSettings;
        private System.Windows.Forms.TabPage tpGeneral;
        private System.Windows.Forms.CheckBox cbStartWithWindows;
        private System.Windows.Forms.TabPage tpPaths;
        private System.Windows.Forms.TabPage tpAdvanced;
        private System.Windows.Forms.PropertyGrid pgSettings;
        private TabToTreeView tttvMain;
    }
}
