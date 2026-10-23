using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using Newtonsoft.Json;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        private bool updatingHistoryMenu;

        private static string ShapeOptionState(BaseShape shape) => JsonConvert.SerializeObject(
            shape.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 &&
                !typeof(Image).IsAssignableFrom(p.PropertyType)).ToDictionary(p => p.Name, p => p.GetValue(shape)));

        private void UpdateShapeFromMenu()
        {
            if (updatingHistoryMenu) return;
            BaseShape shape = CurrentShape;
            if (shape == null || !shape.IsHandledBySelectTool) { UpdateCurrentShape(); return; }
            string before = ShapeOptionState(shape);
            ImageEditorMemento snapshot = history.CaptureShapes();
            try
            {
                UpdateCurrentShape();
                bool changed = before != ShapeOptionState(shape);
                history.CompleteShapes(ref snapshot, changed);
                if (changed) OnImageModified();
            }
            finally { snapshot?.Dispose(); }
        }

        private sealed class CaptureMenuForm : Form
        {
            private readonly ShapeManager manager;
            internal CaptureMenuForm(ShapeManager manager) => this.manager = manager;

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (!manager.IsEditingText && (keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y)))
                {
                    manager.CommitArrowWidth();
                    manager.CommitBlurStrength();
                    manager.CommitInlineTextSize();
                    if (keyData == (Keys.Control | Keys.Z)) manager.history.Undo();
                    else manager.history.Redo();
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }
    }
}
