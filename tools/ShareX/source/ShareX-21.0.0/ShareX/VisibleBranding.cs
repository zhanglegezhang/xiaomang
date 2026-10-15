using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ShareX
{
    internal static class VisibleBranding
    {
        private static readonly HashSet<IntPtr> brandedHandles = new HashSet<IntPtr>();
        internal static readonly object PreserveText = new object();

        public static string Replace(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Paths and links are data, even when their directory or host includes a brand.
            if (text.Contains(@":\") || text.Contains("://") || text.StartsWith(@"\\", StringComparison.Ordinal)) return text;
            return text.Replace("ShareX", Program.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        public static void ApplyOpenForms()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form.IsDisposed || !form.IsHandleCreated || !brandedHandles.Add(form.Handle)) continue;
                ApplyControl(form);
            }
        }

        private static void ApplyControl(Control control)
        {
            if (control is not TextBoxBase && !ReferenceEquals(control.Tag, PreserveText))
            {
                control.Text = Replace(control.Text);
            }

            if (control is ToolStrip toolStrip)
            {
                ApplyItems(toolStrip.Items);
            }

            if (control.ContextMenuStrip != null)
            {
                ApplyItems(control.ContextMenuStrip.Items);
            }

            foreach (Control child in control.Controls)
            {
                ApplyControl(child);
            }
        }

        private static void ApplyItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.Text = Replace(item.Text);
                item.ToolTipText = Replace(item.ToolTipText);

                if (item is ToolStripDropDownItem dropDownItem)
                {
                    ApplyItems(dropDownItem.DropDownItems);
                }
            }
        }
    }
}
