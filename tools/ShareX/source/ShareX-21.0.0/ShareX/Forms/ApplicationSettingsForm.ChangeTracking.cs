using System.Collections.Generic;
using System.Windows.Forms;

namespace ShareX
{
    public partial class ApplicationSettingsForm
    {
        private readonly HashSet<Control> trackedSettingsControls = new();
        private bool trackingSettingsChanges;
        private bool loadingSettingsPage;
        internal bool HasPendingChanges { get; private set; }
        internal void AcceptChanges() => HasPendingChanges = false;

        private void MarkSettingsChanged()
        {
            if (ready && !loadingSettingsPage) HasPendingChanges = true;
        }

        private void TrackSettingsControls(Control control)
        {
            if (!trackedSettingsControls.Add(control)) return;
            control.ControlAdded += (sender, e) => TrackSettingsControls(e.Control);
            switch (control)
            {
                case CheckBox check: check.CheckedChanged += (sender, e) => MarkSettingsChanged(); break;
                case RadioButton radio: radio.CheckedChanged += (sender, e) => MarkSettingsChanged(); break;
                case ComboBox combo: combo.SelectedIndexChanged += (sender, e) => MarkSettingsChanged(); break;
                case NumericUpDown number: number.ValueChanged += (sender, e) => MarkSettingsChanged(); break;
                case TextBoxBase text when !text.ReadOnly: text.TextChanged += (sender, e) => MarkSettingsChanged(); break;
                case PropertyGrid grid: grid.PropertyValueChanged += (sender, e) => MarkSettingsChanged(); break;
                // Dialog-backed settings do not necessarily change an editable control.
                case Button button: button.Click += (sender, e) => MarkSettingsChanged(); break;
            }
            foreach (Control child in control.Controls) TrackSettingsControls(child);
        }

        private void InitializeChangeTracking()
        {
            trackingSettingsChanges = true;
            TrackSettingsControls(this);
            // TabToTreeView detaches inactive pages from the control tree.
            foreach (Control control in tttvMain.Controls.Find("tvMain", true))
                foreach (TreeNode node in ((TreeView)control).Nodes) TrackNode(node);
            foreach (TabPage page in ownedSettingsPages) TrackSettingsControls(page);
        }

        private void TrackNode(TreeNode node)
        {
            if (node.Tag is TabPage page) TrackSettingsControls(page);
            foreach (TreeNode child in node.Nodes) TrackNode(child);
        }
    }
}
