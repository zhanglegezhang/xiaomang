using System.Drawing;

namespace ShareX.ScreenCaptureLib
{
    internal partial class ShapeManager
    {
        private EffectPreviewCache effectPreview;
        private bool effectPreviewClosed;
        internal bool EffectPreviewReady => effectPreview?.IsReady ?? true;
        internal void RequestEffectPreview()
        {
            if (effectPreviewClosed || IsRenderingOutput || Form.IsClosing || Form.IsDisposed) return;
            (effectPreview ??= new EffectPreviewCache(this)).Request();
        }
        internal void DrawEffectPreviews(Graphics graphics)
        {
            if (effectPreviewClosed) return;
            if (effectPreview == null && EffectShapes.Length == 0) return;
            (effectPreview ??= new EffectPreviewCache(this)).Draw(graphics);
        }
    }
}
