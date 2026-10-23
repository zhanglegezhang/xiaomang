using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;

namespace ShareX.ScreenCaptureLib
{
    // One ordered composition per capture, published atomically on the UI thread.
    internal sealed class EffectPreviewCache : IDisposable
    {
        private readonly ShapeManager manager;
        private readonly RegionCaptureForm form;
        private readonly record struct Key(BaseEffectShape Shape, Rectangle Area, int Parameter, bool Stable, long Revision);
        private Key[] keys = Array.Empty<Key>();
        private Bitmap source, rendered;
        private Rectangle renderedBounds;
        private long version, canvasRevision, observedCanvasRevision;
        private bool queued, pending, disposed, failed;
        private PreviewWork active;
        internal bool IsReady => !queued && !pending && active == null && !failed;
        internal int StartedRenders { get; private set; }

        internal EffectPreviewCache(ShapeManager manager) { this.manager = manager; form = manager.Form; }

        internal void CanvasChanged() { canvasRevision++; Request(); }

        internal void Request()
        {
            if (disposed || queued || form.IsClosing || form.IsDisposed || !form.IsHandleCreated) return;
            queued = true;
            form.BeginInvoke((Action)(() =>
            {
                queued = false;
                if (disposed || form.IsClosing || form.IsDisposed) return;
                Refresh();
                StartPending();
                form.InvalidateEffectPreview();
            }));
        }

        private void Refresh()
        {
            if (disposed) return;
            PointF origin = form.CanvasRectangle.Location;
            var canvasBounds = new Rectangle(Point.Empty, form.Canvas.Size);
            var next = manager.EffectShapes.Where(s => !s.PreviewDisposed).Select(shape =>
            {
                RectangleF relative = shape.Rectangle;
                relative.Offset(-origin.X, -origin.Y);
                Rectangle area = Rectangle.Intersect(canvasBounds, Rectangle.Round(relative));
                if (area.Width <= 0 || area.Height <= 0) area = Rectangle.Empty;
                int parameter = shape switch
                {
                    BlurEffectShape blur => blur.BlurRadius,
                    HighlightEffectShape highlight => highlight.HighlightColor.ToArgb(),
                    PixelateEffectShape pixelate => pixelate.PixelSize,
                    _ => 0
                };
                return new Key(shape, area, parameter, shape.PreviewStable, shape.PreviewRevision);
            }).ToArray();
            if (ReferenceEquals(source, form.Canvas) && observedCanvasRevision == canvasRevision && keys.SequenceEqual(next)) return;
            source = form.Canvas;
            observedCanvasRevision = canvasRevision;
            keys = next;
            version++;
            failed = false;
            rendered?.Dispose();
            rendered = null;
            pending = keys.Any(k => !k.Area.IsEmpty) && keys.All(k => k.Area.IsEmpty || k.Stable);
        }

        private void StartPending()
        {
            if (disposed || active != null || !pending || form.IsClosing) return;
            pending = false;
            Bitmap image = null;
            var snapshots = new List<BaseEffectShape>();
            try
            {
                Rectangle bounds = keys.Where(k => !k.Area.IsEmpty).Select(k => k.Area).Aggregate(Rectangle.Union);
                image = ImageHelpers.CropBitmap(form.Canvas, bounds);
                foreach (Key key in keys.Where(k => !k.Area.IsEmpty))
                {
                    var snapshot = (BaseEffectShape)key.Shape.Duplicate();
                    snapshot.Manager = null;
                    // Clip in source pixel coordinates before rebasing, exactly
                    // as export does. Never crop a blur's sampling neighborhood.
                    Rectangle area = key.Area;
                    area.Offset(-bounds.X, -bounds.Y);
                    snapshot.Rectangle = area;
                    snapshots.Add(snapshot);
                }
                var work = new PreviewWork();
                active = work;
                StartedRenders++;
                long generation = version;
                Bitmap input = image;
                BaseEffectShape[] parameters = snapshots.ToArray();
                Task.Run(() => Process(work, generation, bounds, input, parameters));
                image = null;
                snapshots.Clear();
            }
            catch (Exception exception)
            {
                failed = true;
                DebugHelper.WriteException(exception, "Preparing composed effect preview");
            }
            finally { image?.Dispose(); foreach (var snapshot in snapshots) snapshot.Dispose(); }
        }

        private void Process(PreviewWork work, long generation, Rectangle bounds, Bitmap image, BaseEffectShape[] snapshots)
        {
            try
            {
                using (Graphics graphics = Graphics.FromImage(image)) EffectRenderPipeline.Render(graphics, image, snapshots);
                work.SetResult(image);
                image = null;
            }
            catch (Exception exception) { DebugHelper.WriteException(exception, "Rendering composed effect preview"); }
            finally { image?.Dispose(); foreach (var snapshot in snapshots) snapshot.Dispose(); }
            try { form.BeginInvoke((Action)(() => Complete(work, generation, bounds))); }
            catch (InvalidOperationException) { work.Abandon(); }
        }

        private void Complete(PreviewWork work, long generation, Rectangle bounds)
        {
            Bitmap result = work.TakeResult();
            if (disposed || form.IsClosing || form.IsDisposed || active != work) { result?.Dispose(); return; }
            active = null;
            Refresh();
            if (generation == version && !pending)
            {
                rendered?.Dispose();
                rendered = result;
                renderedBounds = bounds;
                failed = result == null;
                result = null;
            }
            result?.Dispose();
            StartPending();
            form.InvalidateEffectPreview();
        }

        internal void Draw(Graphics graphics)
        {
            if (disposed || form.IsClosing) return;
            Refresh();
            StartPending();
            if (rendered != null)
            {
                PointF origin = form.CanvasRectangle.Location;
                using var clip = new Region();
                clip.MakeEmpty();
                foreach (Key key in keys.Where(k => !k.Area.IsEmpty))
                    clip.Union(new RectangleF(origin.X + key.Area.X, origin.Y + key.Area.Y, key.Area.Width, key.Area.Height));
                GraphicsState state = graphics.Save();
                try
                {
                    // The bounding box can contain unaffected gaps. Only paint
                    // the actual union, retaining selection borders and dimming.
                    graphics.SetClip(clip, CombineMode.Intersect);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.DrawImage(rendered, new RectangleF(origin.X + renderedBounds.X, origin.Y + renderedBounds.Y,
                        renderedBounds.Width, renderedBounds.Height), new RectangleF(0, 0, renderedBounds.Width, renderedBounds.Height), GraphicsUnit.Pixel);
                }
                finally { graphics.Restore(state); }
            }
            else foreach (Key key in keys.Where(k => !k.Area.IsEmpty))
                key.Shape.OnDrawOverlay(graphics, !key.Stable ? key.Shape.OverlayText : failed ? "预览失败，调整后重试" : Resources.Processing);

            // Editor-only guides: never included in the effect cache or final image.
            foreach (Key key in keys.Where(k => !k.Area.IsEmpty))
                if (key.Shape is BlurEffectShape blur) blur.DrawEditBounds(graphics);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            rendered?.Dispose();
            rendered = null;
            active?.Abandon();
            active = null;
            keys = Array.Empty<Key>();
        }

        private sealed class PreviewWork
        {
            private readonly object gate = new object();
            private Bitmap result;
            private bool abandoned;
            internal void SetResult(Bitmap image)
            {
                lock (gate) { if (!abandoned) { result = image; return; } }
                image.Dispose();
            }
            internal Bitmap TakeResult() { lock (gate) { var image = result; result = null; return image; } }
            internal void Abandon()
            {
                Bitmap image;
                lock (gate) { abandoned = true; image = result; result = null; }
                image?.Dispose();
            }
        }
    }
}
