using System.Collections.Generic;
using System.Drawing;

namespace ShareX.ScreenCaptureLib
{
    internal static class EffectRenderPipeline
    {
        // Preview and export process the same mutable image, in effect order.
        internal static void Render(Graphics graphics, Bitmap image, IEnumerable<BaseEffectShape> effects)
        {
            foreach (BaseEffectShape effect in effects) effect.OnDrawFinal(graphics, image);
        }
    }
}
