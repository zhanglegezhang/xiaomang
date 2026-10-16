using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX
{
    public partial class TaskSettingsForm
    {
        private void SimplifyCaptureSettings()
        {
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskSettingsForm));
            var location = (System.Drawing.Point)resources.GetObject("cbCaptureClientArea.Location");
            cbCaptureAutoHideDesktopIcons.Top = location.Y;
        }

        private void LayoutCaptureSettings()
        {
            int dpi = deferredPageDpi > 0 ? deferredPageDpi : DeviceDpi;
            int Px(int value) => (int)Math.Round(value * dpi / 96f);
            pCapture.SuspendLayout();
            var rows = new TableLayoutPanel
            {
                Name = "captureSettingsRows", ColumnCount = 1, RowCount = 0,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, Margin = Padding.Empty, Size = new Size(1, 1)
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rows.SuspendLayout();

            void AddRow(Control control, bool indent = false)
            {
                int row = rows.RowCount++;
                rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                control.TabIndex = row;
                control.Margin = new Padding(indent ? Px(20) : 0, Px(4), 0, Px(4));
                control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                control.MinimumSize = new Size(0, Px(28));
                if (control is CheckBox check) check.AutoSize = true;
                rows.Controls.Add(control, 0, row);
            }

            Control NumberRow(Label label, NumericUpDown number, Label unit = null)
            {
                var row = new TableLayoutPanel
                {
                    ColumnCount = 3, RowCount = 1, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Size = new Size(1, 1),
                    Margin = Padding.Empty
                };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(150)));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(100)));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.SuspendLayout();
                label.AutoSize = true;
                label.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                label.Margin = new Padding(0, 0, Px(10), 0);
                number.Width = Px(90);
                number.Anchor = AnchorStyles.Left;
                number.Margin = Padding.Empty;
                row.Controls.Add(label, 0, 0);
                row.Controls.Add(number, 1, 0);
                if (unit != null)
                {
                    unit.AutoSize = true;
                    unit.Anchor = AnchorStyles.Left;
                    unit.Margin = Padding.Empty;
                    row.Controls.Add(unit, 2, 0);
                }
                row.ResumeLayout(false);
                return row;
            }

            AddRow(cbShowCursor);
            AddRow(NumberRow(lblScreenshotDelay, nudScreenshotDelay, lblScreenshotDelayInfo));
            AddRow(cbCaptureTransparent);
            AddRow(cbCaptureShadow, true);
            AddRow(NumberRow(lblCaptureShadowOffset, nudCaptureShadowOffset), true);
            AddRow(cbCaptureAutoHideDesktopIcons);
            pCapture.Padding = new Padding(Px(20));
            pCapture.AutoSize = true;
            pCapture.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pCapture.Dock = DockStyle.Top;
            pCapture.Controls.Add(rows);
            tpCaptureGeneral.AutoScroll = true;
            rows.ResumeLayout(false);
            pCapture.ResumeLayout(true);
        }

        private void SimplifyRegionCaptureSettings()
        {
            int displayDpi = deferredPageDpi > 0 ? deferredPageDpi : DeviceDpi;
            int top = (int)Math.Round(16 * displayDpi / 96f);
            int rowSpacing = Math.Max(cbRegionCaptureMouseRightClickAction.Height, nudRegionCaptureBackgroundDimStrength.Height) +
                (int)Math.Round(12 * displayDpi / 96f);
            cbRegionCaptureMouseRightClickAction.Top = top;
            lblRegionCaptureMouseRightClickAction.Top = top +
                (cbRegionCaptureMouseRightClickAction.Height - lblRegionCaptureMouseRightClickAction.Height) / 2;
            nudRegionCaptureBackgroundDimStrength.Top = top + rowSpacing;
            lblRegionCaptureBackgroundDimStrength.Top = nudRegionCaptureBackgroundDimStrength.Top +
                (nudRegionCaptureBackgroundDimStrength.Height - lblRegionCaptureBackgroundDimStrength.Height) / 2;
            lblRegionCaptureBackgroundDimStrengthHint.Top = lblRegionCaptureBackgroundDimStrength.Top;
            cbRegionCaptureShowInfo.Top = top + rowSpacing * 2;
        }
    }
}
