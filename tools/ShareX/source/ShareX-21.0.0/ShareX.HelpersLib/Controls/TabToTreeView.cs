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

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

namespace ShareX.HelpersLib
{
    public partial class TabToTreeView : UserControl
    {
        public delegate void TabChangedEventHandler(TabPage tabPage);
        public event TabChangedEventHandler TabChanged;

        private TabControl mainTabControl;
        private readonly Dictionary<TabPage, (Size Size, Font Font)> completedLayouts = new();

        [Browsable(false)]
        public TabControl MainTabControl
        {
            get
            {
                return mainTabControl;
            }
            set
            {
                if (mainTabControl != value)
                {
                    mainTabControl = value;
                    FillTreeView(tvMain.Nodes, mainTabControl);
                    tvMain.ExpandAll();
                }
            }
        }

        private int treeViewSize;

        public int TreeViewSize
        {
            get
            {
                return treeViewSize;
            }
            set
            {
                treeViewSize = value;
                scMain.SplitterDistance = treeViewSize;
            }
        }

        public Font TreeViewFont
        {
            get
            {
                return tvMain.Font;
            }
            set
            {
                tvMain.Font = value;
            }
        }

        public ImageList ImageList
        {
            get
            {
                return tvMain.ImageList;
            }
            set
            {
                tvMain.ImageList = value;
            }
        }

        public Color LeftPanelBackColor
        {
            get
            {
                return scMain.Panel1.BackColor;
            }
            set
            {
                pLeft.BackColor = value;
                tvMain.BackColor = value;
            }
        }

        public Color SeparatorColor
        {
            get
            {
                return pSeparator.BackColor;
            }
            set
            {
                pSeparator.BackColor = value;
            }
        }

        [DefaultValue(false)]
        public bool AutoSelectChild { get; set; }

        [DefaultValue(false)]
        public bool BatchPageLayout { get; set; }

        public TabToTreeView()
        {
            InitializeComponent();
            TreeViewSize = 150;
        }

        private void FillTreeView(TreeNodeCollection nodeCollection, TabControl tab, TreeNode parent = null)
        {
            if (nodeCollection != null && tab != null)
            {
                foreach (TabPage tabPage in tab.TabPages)
                {
                    if (parent != null && string.IsNullOrEmpty(tabPage.Text))
                    {
                        parent.Tag = tabPage;
                        continue;
                    }

                    TreeNode treeNode = new TreeNode(tabPage.Text);
                    if (!string.IsNullOrEmpty(tabPage.ImageKey))
                    {
                        treeNode.ImageKey = treeNode.SelectedImageKey = tabPage.ImageKey;
                    }
                    treeNode.Tag = tabPage;
                    nodeCollection.Add(treeNode);

                    foreach (Control control in tabPage.Controls)
                    {
                        if (control is TabControl)
                        {
                            FillTreeView(treeNode.Nodes, control as TabControl, treeNode);
                            break;
                        }
                    }
                }
            }
        }

        private void tvMain_BeforeCollapse(object sender, TreeViewCancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void tvMain_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is TabPage tabPage)
            {
                if (AutoSelectChild && tabPage.Controls.Count == 1 && tabPage.Controls[0] is TabControl)
                {
                    SelectChildNode();
                }
                else
                {
                    SelectTabPage(tabPage);
                }
            }
        }

        private void SelectTabPage(TabPage tabPage)
        {
            if (tabPage != null)
            {
                Control root = FindForm() ?? (Control)this;
                Control[] layouts = BatchPageLayout
                    ? LayoutControls(root).Concat(LayoutControls(tabPage)).Distinct().ToArray()
                    : Array.Empty<Control>();
                foreach (Control control in layouts) control.SuspendLayout();
                try
                {
                    tcMain.Visible = true;
                    tcMain.TabPages.Clear();
                    tcMain.TabPages.Add(tabPage);
                    tvMain.Focus();

                    OnTabChanged(tabPage);
                }
                finally
                {
                    for (int i = layouts.Length - 1; i >= 0; i--)
                        if (!layouts[i].IsDisposed) layouts[i].ResumeLayout(false);
                    if (layouts.Length > 0 && !root.IsDisposed)
                    {
                        root.PerformLayout();
                        // AutoSize containers need one final pass after their new width
                        // is settled, including pages resized while they were hidden.
                        var geometry = (tabPage.ClientSize, tabPage.Font);
                        if (!completedLayouts.TryGetValue(tabPage, out var previous) || previous != geometry ||
                            LayoutControls(tabPage).Any(control => control.AutoSize && (control.Width == 0 || control.Height == 0)))
                        {
                            foreach (Control control in LayoutControls(tabPage).Reverse())
                                if (control is TableLayoutPanel || control is FlowLayoutPanel) control.PerformLayout();
                            tabPage.PerformLayout();
                            completedLayouts[tabPage] = geometry;
                        }
                    }
                }
            }
        }

        private static IEnumerable<Control> LayoutControls(Control parent)
        {
            yield return parent;
            foreach (Control child in parent.Controls)
                foreach (Control control in LayoutControls(child)) yield return control;
        }

        public void NavigateToTabPage(TabPage tabPage)
        {
            if (tabPage != null)
            {
                foreach (TreeNode node in tvMain.Nodes.All())
                {
                    TabPage nodeTabPage = node.Tag as TabPage;

                    if (nodeTabPage == tabPage)
                    {
                        tvMain.SelectedNode = node;
                        return;
                    }
                }
            }
        }

        public void SelectChildNode()
        {
            TreeNode node = tvMain.SelectedNode;

            if (node != null && node.Nodes.Count > 0)
            {
                tvMain.SelectedNode = node.Nodes[0];
            }
        }

        protected void OnTabChanged(TabPage tabPage)
        {
            TabChanged?.Invoke(tabPage);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified);
            scMain.SplitterDistance = (int)Math.Round(scMain.SplitterDistance * factor.Width);
        }
    }
}
