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
using ShareX.ScreenCaptureLib.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShareX.ScreenCaptureLib
{
    public sealed class RegionCaptureForm : Form
    {
        public static GraphicsPath LastRegionFillPath { get; private set; }

        public event Func<Bitmap, string, string> SaveImageRequested;
        public event Func<Bitmap, string, string> SaveImageAsRequested;
        public event Action<Bitmap> CopyImageRequested;
        public event Action<Bitmap> UploadImageRequested;
        public event Action<Bitmap> PrintImageRequested;

        public RegionCaptureOptions Options { get; set; }
        public Rectangle ScreenBounds { get; set; }
        public Rectangle ClientArea { get; private set; }
        public Bitmap Canvas { get; private set; }
        public RectangleF CanvasRectangle { get; internal set; }
        public RegionResult Result { get; private set; }
        public bool CopyCaptureRequested { get; private set; }
        public bool LongCaptureRequested { get; private set; }
        public bool PinCaptureRequested { get; private set; }
        public bool OCRCaptureRequested { get; private set; }
        public bool TodoCaptureRequested { get; private set; }
        public int MonitorIndex { get; set; }
        public string ImageFilePath { get; set; }
        public bool IsFullscreen { get; private set; }
        public bool RequiresCaptureConfirmation { get; }
        internal bool UseQuickCrop => Options.QuickCrop && !RequiresCaptureConfirmation;

        public RegionCaptureMode Mode { get; private set; }
        public bool IsEditorMode => Mode == RegionCaptureMode.Editor || Mode == RegionCaptureMode.TaskEditor;
        public bool IsAnnotationMode => Mode == RegionCaptureMode.Annotation || IsEditorMode;
        public bool IsImageModified => ShapeManager != null && ShapeManager.IsImageModified;

        public Point CurrentPosition { get; private set; }
        public SimpleWindowInfo SelectedWindow { get; private set; }

        internal Vector2 CanvasCenterOffset { get; set; } = new Vector2(0f, 0f);

        internal float ZoomFactor
        {
            get
            {
                return zoomFactor;
            }
            set
            {
                zoomFactor = value.Clamp(0.2f, 6f);
            }
        }

        internal bool IsZoomed => Math.Round(ZoomFactor * 100) != 100;
        internal PointF ScaledClientMousePosition => InputManager.ClientMousePosition.Scale(1 / ZoomFactor);
        internal PointF ScaledClientMouseVelocity => InputManager.MouseVelocity.Scale(1 / ZoomFactor);

        internal ShapeManager ShapeManager { get; private set; }
        internal bool IsClosing { get; private set; }
        internal FPSManager FPSManager { get; private set; }

        internal Bitmap DimmedCanvas;
        internal Image CustomNodeImage = Resources.CircleNode;
        internal int ToolbarHeight;

        private InputManager InputManager => ShapeManager.InputManager;
        private TextureBrush backgroundBrush, backgroundHighlightBrush;
        private GraphicsPath regionFillPath, regionDrawPath;
        private Pen borderPen, borderDotPen, borderDotStaticPen, textOuterBorderPen, textInnerBorderPen, markerPen, canvasBorderPen;
        private Brush textBrush, textShadowBrush, textBackgroundBrush;
        private Font infoFont, infoFontMedium, infoFontBig;
        private Stopwatch timerStart;
        private bool pause, isKeyAllowed, forceClose;
        private RectangleAnimation regionAnimation;
        private TextAnimation editorPanTipAnimation;
        private Cursor defaultCursor, openHandCursor, closedHandCursor;
        private Color canvasBackgroundColor, canvasBorderColor, textColor, textShadowColor, textBackgroundColor, textOuterBorderColor, textInnerBorderColor;
        private float zoomFactor = 1;
        private readonly RegionCaptureRefreshScheduler refreshScheduler = new RegionCaptureRefreshScheduler();
        private readonly Stopwatch refreshClock = Stopwatch.StartNew();
        private readonly Stopwatch paintClock = new Stopwatch();
        private System.Windows.Forms.Timer refreshTimer;
        private bool framePending;
        private long paintedFrames, slowFrames;
        private double paintMilliseconds, maximumPaintMilliseconds;
        private double backgroundMilliseconds, shapesMilliseconds, updateMilliseconds, paintedPixels;
        private double preparedUpdateMilliseconds;
        private long partialFrames;
        private bool framePrepared;
        private RegionCaptureDamage previousDamage, pendingDamage;
        private Region scheduledDamage;

        public RegionCaptureForm(RegionCaptureMode mode, RegionCaptureOptions options, Bitmap canvas = null)
            : this(mode, options, canvas, false)
        {
        }

        public RegionCaptureForm(RegionCaptureMode mode, RegionCaptureOptions options, Bitmap canvas, bool requireCaptureConfirmation)
        {
            Mode = mode;
            Options = options;
            RequiresCaptureConfirmation = requireCaptureConfirmation;

            IsFullscreen = !IsEditorMode || Options.ImageEditorStartMode == ImageEditorStartMode.Fullscreen;

            if (IsFullscreen && Options.ActiveMonitorMode)
            {
                ScreenBounds = CaptureHelpers.GetActiveScreenBounds();

                if (canvas == null)
                {
                    canvas = new Screenshot().CaptureRectangle(ScreenBounds);
                }

                Helpers.LockCursorToWindow(this);
            }
            else
            {
                ScreenBounds = CaptureHelpers.GetScreenBounds();

                if (canvas == null)
                {
                    canvas = new Screenshot().CaptureRectangle(ScreenBounds);
                }
            }

            ClientArea = new Rectangle(0, 0, ScreenBounds.Width, ScreenBounds.Height);
            CanvasRectangle = ClientArea;

            timerStart = new Stopwatch();
            FPSManager = new FPSManager(Options.FPSLimit);
            FPSManager.FPSUpdated += FpsManager_FPSChanged;
            regionAnimation = new RectangleAnimation()
            {
                Duration = TimeSpan.FromMilliseconds(200)
            };

            if (IsEditorMode && Options.ShowEditorPanTip)
            {
                editorPanTipAnimation = new TextAnimation()
                {
                    Duration = TimeSpan.FromMilliseconds(5000),
                    FadeOutDuration = TimeSpan.FromMilliseconds(1000),
                    Text = Resources.RegionCaptureForm_TipYouCanPanImageByHoldingMouseMiddleButtonAndDragging
                };
            }

            borderPen = new Pen(Color.Black);
            borderDotPen = new Pen(Color.White) { DashPattern = new float[] { 5, 5 } };
            borderDotStaticPen = new Pen(Color.White) { DashPattern = new float[] { 5, 5 } };
            infoFont = new Font("Verdana", 9);
            infoFontMedium = new Font("Verdana", 12);
            infoFontBig = new Font("Verdana", 16, FontStyle.Bold);
            markerPen = new Pen(Color.FromArgb(200, Color.Red));

            canvasBackgroundColor = ShareXResources.Theme.BackgroundColor;
            canvasBorderColor = ShareXResources.Theme.BorderColor;
            textColor = ShareXResources.Theme.TextColor;
            textShadowColor = ShareXResources.Theme.BorderColor;
            textBackgroundColor = Color.FromArgb(200, ShareXResources.Theme.BackgroundColor);
            textOuterBorderColor = Color.FromArgb(200, ShareXResources.Theme.SeparatorDarkColor);
            textInnerBorderColor = Color.FromArgb(200, ShareXResources.Theme.SeparatorLightColor);

            canvasBorderPen = new Pen(canvasBorderColor);
            textBrush = new SolidBrush(textColor);
            textShadowBrush = new SolidBrush(textShadowColor);
            textBackgroundBrush = new SolidBrush(textBackgroundColor);
            textOuterBorderPen = new Pen(textOuterBorderColor);
            textInnerBorderPen = new Pen(textInnerBorderColor);

            Prepare(canvas);

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = RegionCaptureRefreshScheduler.GetInterval(Options.FPSLimit);
            refreshTimer.Tick += RefreshTimer_Tick;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            AutoScaleMode = AutoScaleMode.None;
            defaultCursor = Helpers.CreateCursor(Resources.Crosshair);
            openHandCursor = Helpers.CreateCursor(Resources.openhand);
            closedHandCursor = Helpers.CreateCursor(Resources.closedhand);
            SetDefaultCursor();
            Icon = ShareXResources.Icon;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            UpdateTitle();
            StartPosition = FormStartPosition.Manual;

            if (IsFullscreen)
            {
                FormBorderStyle = FormBorderStyle.None;
                Bounds = ScreenBounds;
                ShowInTaskbar = false;
#if !DEBUG
                TopMost = true;
#endif
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                MinimumSize = new Size(800, 550);

                if (Options.ImageEditorStartMode == ImageEditorStartMode.PreviousState)
                {
                    Options.ImageEditorWindowState.ApplyFormState(this);
                }
                else
                {
                    Rectangle activeScreenWorkingArea = CaptureHelpers.GetActiveScreenWorkingArea();
                    Size size = new Size(900, 700);
                    bool isMaximized = Options.ImageEditorStartMode == ImageEditorStartMode.Maximized;

                    if (Options.ImageEditorStartMode == ImageEditorStartMode.AutoSize)
                    {
                        int margin = 100;
                        Size canvasWindowSize = new Size(Canvas.Width + (SystemInformation.BorderSize.Width * 2) + margin,
                            Canvas.Height + SystemInformation.CaptionHeight + (SystemInformation.BorderSize.Height * 2) + margin);
                        canvasWindowSize = new Size(Math.Max(MinimumSize.Width, canvasWindowSize.Width), Math.Max(MinimumSize.Height, canvasWindowSize.Height));

                        if (canvasWindowSize.Width < activeScreenWorkingArea.Width && canvasWindowSize.Height < activeScreenWorkingArea.Height)
                        {
                            size = canvasWindowSize;
                        }
                        else
                        {
                            isMaximized = true;
                        }
                    }

                    Bounds = new Rectangle(activeScreenWorkingArea.X + (activeScreenWorkingArea.Width / 2) - (size.Width / 2),
                        activeScreenWorkingArea.Y + (activeScreenWorkingArea.Height / 2) - (size.Height / 2), size.Width, size.Height);

                    if (isMaximized)
                    {
                        WindowState = FormWindowState.Maximized;
                    }
                    else
                    {
                        WindowState = FormWindowState.Normal;
                    }
                }

                ShowInTaskbar = true;
            }

            Shown += RegionCaptureForm_Shown;
            KeyDown += RegionCaptureForm_KeyDown;
            MouseDown += RegionCaptureForm_MouseDown;
            MouseWheel += RegionCaptureForm_MouseWheel;
            Resize += RegionCaptureForm_Resize;
            LocationChanged += RegionCaptureForm_LocationChanged;
            GotFocus += RegionCaptureForm_GotFocus;
            LostFocus += RegionCaptureForm_LostFocus;
            FormClosing += RegionCaptureForm_FormClosing;
            MouseDown += RequestInputRefresh;
            MouseUp += RequestInputRefresh;
            MouseWheel += RequestInputRefresh;
            KeyDown += RequestInputRefresh;
            KeyUp += RequestInputRefresh;

            ResumeLayout(false);
        }

        internal void UpdateTitle()
        {
            if (forceClose) return;

            StringBuilder title = new StringBuilder();

            if (IsEditorMode)
            {
                title.AppendFormat("小蟒 - {0}", Resources.RegionCaptureForm_InitializeComponent_ImageEditor);

                if (Canvas != null)
                {
                    title.AppendFormat(" - {0}x{1}", Canvas.Width, Canvas.Height);
                }

                if (IsZoomed)
                {
                    int zoomPercentage = (int)Math.Round(ZoomFactor * 100);
                    title.AppendFormat(" ({0}%)", zoomPercentage);
                }

                string fileName = FileHelpers.GetFileNameSafe(ImageFilePath);

                if (!string.IsNullOrEmpty(fileName))
                {
                    title.AppendFormat(" - {0}", fileName);
                }

                if (!IsFullscreen && Options.ShowFPS)
                {
                    title.AppendFormat(" - FPS: {0}", FPSManager.FPS.ToString());
                }
            }
            else
            {
                title.AppendFormat("小蟒 - {0}", Resources.BaseRegionForm_InitializeComponent_Region_capture);
            }

            Text = title.ToString();
        }

        private void Prepare(Bitmap canvas = null)
        {
            ShapeManager = new ShapeManager(this);
            ShapeManager.WindowCaptureMode = !IsEditorMode && Options.DetectWindows;
            ShapeManager.IncludeControls = Options.DetectControls;
            ShapeManager.ImageModified += ShapeManager_ImageModified;

            InitBackground(canvas);

            if (Mode == RegionCaptureMode.OneClick || ShapeManager.WindowCaptureMode)
            {
                IntPtr handle = Handle;

                Task.Run(() =>
                {
                    WindowsRectangleList wla = new WindowsRectangleList();
                    wla.IgnoreHandleList.Add(handle);
                    wla.IncludeChildWindows = ShapeManager.IncludeControls;
                    wla.Timeout = 5000;

                    ShapeManager.Windows = wla.GetWindowInfoList();
                });
            }
        }

        private void ShapeManager_ImageModified()
        {
            if (Options.EditorAutoCopyImage && !IsClosing && IsEditorMode)
            {
                using (Bitmap bmp = GetResultImage())
                {
                    OnCopyImageRequested(bmp);
                }
            }
        }

        internal void InitBackground(Bitmap canvas, bool centerCanvas = true)
        {
            ResetDamage();
            Canvas?.Dispose();
            backgroundBrush?.Dispose();
            backgroundHighlightBrush?.Dispose();

            Canvas = canvas;

            if (IsEditorMode)
            {
                UpdateTitle();

                CanvasRectangle = new RectangleF(CanvasRectangle.X, CanvasRectangle.Y, Canvas.Width, Canvas.Height);

                using (Bitmap background = new Bitmap(Canvas.Width, Canvas.Height))
                using (Graphics g = Graphics.FromImage(background))
                {
                    Rectangle sourceRect = new Rectangle(0, 0, Canvas.Width, Canvas.Height);

                    if (ShareXResources.Theme.CheckerSize > 0)
                    {
                        using (Bitmap checkers = ImageHelpers.DrawCheckers(Canvas.Width, Canvas.Height, ShareXResources.Theme.CheckerSize,
                            ShareXResources.Theme.CheckerColor, ShareXResources.Theme.CheckerColor2))
                        {
                            g.DrawImage(checkers, sourceRect);
                        }
                    }
                    else
                    {
                        using (Brush canvasBrush = new SolidBrush(ShareXResources.Theme.CheckerColor))
                        {
                            g.FillRectangle(canvasBrush, sourceRect);
                        }
                    }

                    g.DrawImage(Canvas, sourceRect);

                    backgroundBrush = new TextureBrush(background) { WrapMode = WrapMode.Clamp };
                    backgroundBrush.TranslateTransform(CanvasRectangle.X, CanvasRectangle.Y);
                }

                if (centerCanvas)
                {
                    CenterCanvas();
                }
            }
            else if (Options.BackgroundDimStrength > 0)
            {
                DimmedCanvas?.Dispose();
                DimmedCanvas = (Bitmap)Canvas.Clone();

                int alpha = (int)Math.Round(255 * (Options.BackgroundDimStrength / 100f));

                using (Graphics g = Graphics.FromImage(DimmedCanvas))
                using (Brush brush = new SolidBrush(Color.FromArgb(alpha, Color.Black)))
                {
                    g.FillRectangle(brush, 0, 0, DimmedCanvas.Width, DimmedCanvas.Height);

                    backgroundBrush = new TextureBrush(DimmedCanvas) { WrapMode = WrapMode.Clamp };
                }

                backgroundHighlightBrush = new TextureBrush(Canvas) { WrapMode = WrapMode.Clamp };
            }
            else
            {
                backgroundBrush = new TextureBrush(Canvas) { WrapMode = WrapMode.Clamp };
            }
        }

        private void OnMoved()
        {
            ResetDamage();
            if (ShapeManager != null)
            {
                UpdateCoordinates();

                if (IsAnnotationMode && ShapeManager.ToolbarCreated)
                {
                    ShapeManager.UpdateMenuMaxWidth(ClientSize.Width);
                    ShapeManager.UpdateMenuPosition();
                }
            }
        }

        private bool Pan(float deltaX, float deltaY)
        {
            SizeF panLimitSize = new SizeF(Math.Min(ClientArea.Width * 0.25f, CanvasRectangle.Width),
                Math.Min(ClientArea.Height * 0.25f, CanvasRectangle.Height));

            RectangleF limitRectangle = new RectangleF(ClientArea.X + panLimitSize.Width, ClientArea.Y + panLimitSize.Height,
                ClientArea.Width - (panLimitSize.Width * 2), ClientArea.Height - (panLimitSize.Height * 2));

            if (IsZoomed)
            {
                limitRectangle = limitRectangle.Scale(1 / ZoomFactor);
            }

            deltaX = Math.Max(deltaX, limitRectangle.Left - CanvasRectangle.Right);
            deltaX = Math.Min(deltaX, limitRectangle.Right - CanvasRectangle.Left);
            deltaY = Math.Max(deltaY, limitRectangle.Top - CanvasRectangle.Bottom);
            deltaY = Math.Min(deltaY, limitRectangle.Bottom - CanvasRectangle.Top);

            if (deltaX == 0 && deltaY == 0)
            {
                return false;
            }

            CanvasRectangle = CanvasRectangle.LocationOffset(deltaX, deltaY);

            if (backgroundBrush != null)
            {
                backgroundBrush.TranslateTransform(deltaX, deltaY);
            }

            if (ShapeManager != null)
            {
                ShapeManager.MoveAll(deltaX, deltaY);
            }

            return true;
        }

        public void PanToOffset(Vector2 centerOffset)
        {
            if (IsEditorMode)
            {
                RectangleF canvas = CanvasRectangle.Scale(ZoomFactor);
                float x = (ClientArea.Width / 2) + centerOffset.X;
                float y = (ClientArea.Height / 2) + centerOffset.Y;
                float newX = x - (canvas.Width / 2);
                float newY = y - (canvas.Height / 2);
                float deltaX = (newX - canvas.X) / ZoomFactor;
                float deltaY = (newY - canvas.Y) / ZoomFactor;
                if (Pan(deltaX, deltaY))
                {
                    CanvasCenterOffset = centerOffset;
                }
            }
        }

        public void CenterCanvas()
        {
            PanToOffset(new Vector2(0f, ToolbarHeight / 2f));
        }

        public void ZoomTransform(Graphics g, bool invertZoom = false)
        {
            if (IsZoomed)
            {
                float scale = invertZoom ? 1 / ZoomFactor : ZoomFactor;
                g.ScaleTransform(scale, scale);
            }
        }

        public void SetDefaultCursor()
        {
            if (Cursor != defaultCursor)
            {
                Cursor = defaultCursor;
            }
        }

        public void SetHandCursor(bool grabbing)
        {
            if (grabbing)
            {
                if (Cursor != closedHandCursor)
                {
                    Cursor = closedHandCursor;
                }
            }
            else
            {
                if (Cursor != openHandCursor)
                {
                    Cursor = openHandCursor;
                }
            }
        }

        private void RegionCaptureForm_Shown(object sender, EventArgs e)
        {
            this.ForceActivate();

            OnMoved();
            CenterCanvas();
            refreshTimer.Start();

            if (Options.EditorAutoCopyImage && IsEditorMode)
            {
                using (Bitmap bmp = Canvas.CloneSafe())
                {
                    OnCopyImageRequested(bmp);
                }
            }

            if (IsEditorMode)
            {
                if (Options.ShowEditorPanTip && editorPanTipAnimation != null)
                {
                    editorPanTipAnimation.Start();
                }

                if (Options.ZoomToFitOnOpen)
                {
                    ZoomToFit();
                }
            }
        }

        private void RegionCaptureForm_Resize(object sender, EventArgs e)
        {
            OnMoved();
            PanToOffset(CanvasCenterOffset);
        }

        private void RegionCaptureForm_LocationChanged(object sender, EventArgs e)
        {
            OnMoved();
        }

        private void RegionCaptureForm_GotFocus(object sender, EventArgs e)
        {
            Resume();
        }

        private void RegionCaptureForm_LostFocus(object sender, EventArgs e)
        {
            Pause();
        }

        private void RegionCaptureForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsEditorMode)
            {
                if (e.CloseReason == CloseReason.UserClosing && !forceClose && !IsFullscreen && !ShowExitConfirmation())
                {
                    e.Cancel = true;
                    return;
                }

                if (Options.ImageEditorStartMode == ImageEditorStartMode.PreviousState)
                {
                    Options.ImageEditorWindowState.UpdateFormState(this);
                }
            }
        }

        internal bool ShowExitConfirmation()
        {
            bool result = true;

            if (IsImageModified)
            {
                Pause();
                DialogResult dialogResult = MessageBox.Show(this, Resources.RegionCaptureForm_SaveChangesBeforeClosingEditor,
                    Resources.RegionCaptureForm_ShowExitConfirmation_ShareXImageEditor, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (dialogResult == DialogResult.Yes)
                {
                    OnSaveImageRequested();
                }

                result = dialogResult != DialogResult.Cancel;
                Resume();
            }

            return result;
        }

        internal void RegionCaptureForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Escape)
            {
                if (ShapeManager.HandleEscape())
                {
                    return;
                }

                if (!IsEditorMode || ShowExitConfirmation())
                {
                    CloseWindow();
                }

                return;
            }

            if (!isKeyAllowed && timerStart.ElapsedMilliseconds < Options.InputDelay)
            {
                return;
            }

            isKeyAllowed = true;

            switch (e.KeyData)
            {
                case Keys.Space:
                    CloseWindow(RegionResult.Fullscreen);
                    break;
                case Keys.Enter:
                    if (ShapeManager.IsCurrentShapeTypeRegion)
                    {
                        ShapeManager.StartRegionSelection();
                        ShapeManager.EndRegionSelection();
                    }

                    CloseWindow(RegionResult.Region);
                    break;
                case Keys.Oemtilde:
                    CloseWindow(RegionResult.ActiveMonitor);
                    break;
            }

            if (IsEditorMode)
            {
                switch (e.KeyData)
                {
                    case Keys.Control | Keys.Alt | Keys.D0:
                    case Keys.Control | Keys.Alt | Keys.NumPad0:
                        ZoomToFit();
                        break;
                    case Keys.Control | Keys.D0:
                    case Keys.Control | Keys.NumPad0:
                        ZoomFactor = 1;
                        CenterCanvas();
                        break;
                    case Keys.Control | Keys.Oemplus:
                    case Keys.Control | Keys.Add:
                        Zoom(true, false);
                        break;
                    case Keys.Control | Keys.OemMinus:
                    case Keys.Control | Keys.Subtract:
                        Zoom(false, false);
                        break;
                }
            }
            else
            {
                if (e.KeyData == (Keys.Control | Keys.C))
                {
                    CopyAreaInfo();
                }
                else if (e.KeyData >= Keys.D0 && e.KeyData <= Keys.D9)
                {
                    MonitorKey(e.KeyData - Keys.D0);
                    return;
                }
            }
        }

        private void RegionCaptureForm_MouseDown(object sender, MouseEventArgs e)
        {
            if ((Mode == RegionCaptureMode.OneClick || Mode == RegionCaptureMode.ScreenColorPicker) && e.Button == MouseButtons.Left)
            {
                CurrentPosition = InputManager.MousePosition;

                if (Mode == RegionCaptureMode.OneClick)
                {
                    SelectedWindow = ShapeManager.FindSelectedWindow();
                }

                CloseWindow(RegionResult.Region);
            }
        }

        private void RegionCaptureForm_MouseWheel(object sender, MouseEventArgs e)
        {
            if (IsEditorMode && ModifierKeys == Keys.Control)
            {
                Zoom(e.Delta > 0);
            }
        }

        private void Zoom(bool zoomIn, bool atMouse = true)
        {
            PointF clientCenter = new PointF(ClientArea.Width / 2f, ClientArea.Height / 2f);
            PointF scaledCenterBefore = atMouse ? ScaledClientMousePosition : clientCenter.Scale(1 / zoomFactor);

            if (zoomIn)
            {
                if (ZoomFactor >= 2f)
                {
                    ZoomFactor += 0.5f;
                }
                else if (ZoomFactor >= 1f)
                {
                    ZoomFactor += 0.25f;
                }
                else
                {
                    ZoomFactor += 0.1f;
                }
            }
            else
            {
                if (ZoomFactor <= 1f)
                {
                    ZoomFactor -= 0.1f;
                }
                else if (ZoomFactor <= 2f)
                {
                    ZoomFactor -= 0.25f;
                }
                else
                {
                    ZoomFactor -= 0.5f;
                }
            }

            PointF scaledCenterAfter = atMouse ? ScaledClientMousePosition : clientCenter.Scale(1 / zoomFactor);
            if (Pan(scaledCenterAfter.X - scaledCenterBefore.X, scaledCenterAfter.Y - scaledCenterBefore.Y))
            {
                CanvasCenterOffset = new Vector2((CanvasRectangle.X + CanvasRectangle.Width / 2f) * ZoomFactor - clientCenter.X,
                    (CanvasRectangle.Y + CanvasRectangle.Height / 2f) * ZoomFactor - clientCenter.Y);
            }

            UpdateTitle();
        }

        private void ZoomToFit()
        {
            ZoomFactor = Math.Min(ClientArea.Width / CanvasRectangle.Width, (ClientArea.Height - ToolbarHeight) / CanvasRectangle.Height);

            CenterCanvas();
        }

        private void MonitorKey(int index)
        {
            if (index == 0)
            {
                index = 10;
            }

            index--;

            MonitorIndex = index;

            CloseWindow(RegionResult.Monitor);
        }

        internal void CopyCapture()
        {
            if (IsEditorMode || ShapeManager.ValidRegions.Length == 0) return;

            UpdateRegionPath();
            CopyCaptureRequested = true;
            CloseWindow(RegionResult.Region);
        }

        internal void StartLongCapture()
        {
            if (IsEditorMode || ShapeManager.ValidRegions.Length != 1 || !ShapeManager.IsCurrentShapeValid ||
                ShapeManager.CurrentRectangle.Width < 120 || ShapeManager.CurrentRectangle.Height < 100) return;

            LongCaptureRequested = true;
            CloseWindow(RegionResult.Region);
        }

        internal void PinCapture()
        {
            if (IsEditorMode || ShapeManager.ValidRegions.Length == 0) return;

            UpdateRegionPath();
            PinCaptureRequested = true;
            CloseWindow(RegionResult.Region);
        }

        internal void AddCaptureToTodo()
        {
            if (IsEditorMode || ShapeManager.ValidRegions.Length == 0) return;
            UpdateRegionPath();
            TodoCaptureRequested = true;
            CloseWindow(RegionResult.Region);
        }

        internal void OCRCapture()
        {
            if (IsEditorMode || ShapeManager.ValidRegions.Length == 0) return;

            UpdateRegionPath();
            OCRCaptureRequested = true;
            CloseWindow(RegionResult.Region);
        }

        internal void CloseWindow(RegionResult result = RegionResult.Close)
        {
            Result = result;
            forceClose = true;
            Close();
        }

        internal void Pause()
        {
            pause = true;
            refreshTimer?.Stop();
        }

        internal void Resume()
        {
            ResetDamage();
            pause = false;
            refreshScheduler.RequestInputRefresh();
            refreshTimer?.Start();

            Invalidate();
        }

        private void CopyAreaInfo()
        {
            string clipboardText;

            if (ShapeManager.IsCurrentShapeValid)
            {
                clipboardText = GetAreaText(ShapeManager.CurrentRectangle);
            }
            else
            {
                CurrentPosition = InputManager.MousePosition;
                clipboardText = GetInfoText();
            }

            ClipboardHelpers.CopyText(clipboardText);
        }

        public WindowInfo GetWindowInfo()
        {
            return ShapeManager.FindSelectedWindowInfo(CurrentPosition);
        }

        public void AddCursor(Bitmap bmpCursor, Point position)
        {
            if (ShapeManager != null)
            {
                ShapeManager.AddCursor(bmpCursor, position);
            }
        }

        private void UpdateCoordinates()
        {
            ClientArea = ClientRectangle;

            InputManager.Update(this);
        }

        private new void Update()
        {
            if (!timerStart.IsRunning)
            {
                timerStart.Start();
            }

            FPSManager.Update(false);

            UpdateCoordinates();

            ShapeManager.UpdateObjects();

            if (ShapeManager.IsPanning)
            {
                Vector2 offset = new Vector2(CanvasCenterOffset.X + InputManager.MouseVelocity.X, CanvasCenterOffset.Y + InputManager.MouseVelocity.Y);
                PanToOffset(offset);
            }

            if (Options.EnableAnimations)
            {
                borderDotPen.DashOffset = (float)timerStart.Elapsed.TotalSeconds * -15;
            }

            ShapeManager.Update();
        }

        private void FpsManager_FPSChanged()
        {
            if (Options.ShowFPS && !IsFullscreen)
            {
                UpdateTitle();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            //base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            paintClock.Restart();
            bool prepared = framePrepared;
            if (!prepared) Update();
            framePrepared = false;
            framePending = false;

            Graphics g = e.Graphics;

            ShapeManager.CurrentDPI.X = g.DpiX;
            ShapeManager.CurrentDPI.Y = g.DpiY;

            ZoomTransform(g);

            if (IsEditorMode && !CanvasRectangle.Contains(ClientArea))
            {
                g.Clear(canvasBackgroundColor);
                g.DrawRectangleProper(canvasBorderPen, CanvasRectangle.Offset(1f));
            }

            GraphicsState frameState = g.Save();
            if (prepared && scheduledDamage != null) g.SetClip(scheduledDamage, CombineMode.Intersect);
            long phaseStart = Stopwatch.GetTimestamp();
            DrawBackground(g);
            backgroundMilliseconds += Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
            phaseStart = Stopwatch.GetTimestamp();
            DrawShapes(g);
            shapesMilliseconds += Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;

            if (Options.ShowFPS && IsFullscreen)
            {
                DrawFPS(g, 10);
            }

            g.Restore(frameState);
            double pixels = prepared && scheduledDamage != null ? RegionCaptureDamage.Area(scheduledDamage) :
                (double)e.ClipRectangle.Width * e.ClipRectangle.Height;
            paintedPixels += pixels;
            if (pixels < (double)ClientArea.Width * ClientArea.Height) partialFrames++;
            previousDamage = prepared ? pendingDamage : null;
            pendingDamage = null;
            scheduledDamage?.Dispose();
            scheduledDamage = null;

            paintClock.Stop();
            double elapsed = paintClock.Elapsed.TotalMilliseconds + (prepared ? preparedUpdateMilliseconds : 0);
            paintedFrames++;
            paintMilliseconds += elapsed;
            maximumPaintMilliseconds = Math.Max(maximumPaintMilliseconds, elapsed);
            if (elapsed > 33) slowFrames++;
        }

        private void ResetDamage()
        {
            previousDamage = null;
            pendingDamage = null;
            scheduledDamage?.Dispose();
            scheduledDamage = null;
            // Input handlers can change shapes between a scheduled frame and WM_PAINT.
            // Expand that paint to the full client instead of presenting an incomplete frame.
            if (framePending) { framePrepared = false; Invalidate(); }
        }

        private void RequestInputRefresh(object sender, EventArgs e)
        {
            refreshScheduler.RequestInputRefresh();
            ResetDamage();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (pause || IsClosing || !Visible || framePending) return;
            refreshTimer.Interval = RegionCaptureRefreshScheduler.GetInterval(Options.FPSLimit);
            if (!refreshScheduler.ShouldRender(refreshClock.ElapsedMilliseconds, Control.MousePosition,
                Control.MouseButtons, Control.ModifierKeys, Options.FPSLimit,
                IsEditorMode || ShapeManager.HasActiveInput || regionAnimation.IsActive)) return;
            long phaseStart = Stopwatch.GetTimestamp();
            Update();
            using (Graphics measurement = CreateGraphics()) pendingDamage = CaptureDamage(measurement);
            preparedUpdateMilliseconds = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
            updateMilliseconds += preparedUpdateMilliseconds;
            scheduledDamage?.Dispose();
            scheduledDamage = pendingDamage?.Difference(previousDamage);
            if (scheduledDamage != null && RegionCaptureDamage.Area(scheduledDamage) == 0) return;
            framePrepared = true;
            framePending = true;
            if (scheduledDamage == null) Invalidate();
            else Invalidate(scheduledDamage);
        }

        private void DrawBackground(Graphics g)
        {
            using (GraphicsQualityManager quality = new GraphicsQualityManager(g, false))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                if (!IsEditorMode && !IsZoomed && CanvasRectangle.Location == PointF.Empty &&
                    CanvasRectangle.Size == backgroundBrush.Image.Size)
                {
                    // Copy only visible source pixels. DrawImage(fullCanvas) still pays for
                    // an enormous source rectangle even when Windows clips the destination.
                    Rectangle visible = Rectangle.Intersect(Rectangle.Ceiling(g.VisibleClipBounds), Rectangle.Ceiling(CanvasRectangle));
                    using (Region clipRegion = g.Clip)
                    using (Matrix identity = new Matrix())
                    {
                        RectangleF[] scans = clipRegion.GetRegionScans(identity);
                        // Region scans retain the thin borders and disjoint old/new cursor
                        // areas; using their bounding box would undo the large-selection saving.
                        if (scans.Length > 256) scans = new[] { (RectangleF)visible };
                        foreach (RectangleF scan in scans)
                        {
                            Rectangle clip = Rectangle.Intersect(Rectangle.Ceiling(scan), visible);
                            if (clip.Width > 0 && clip.Height > 0)
                                g.DrawImage(backgroundBrush.Image, clip, clip.X, clip.Y, clip.Width, clip.Height, GraphicsUnit.Pixel);
                        }
                    }
                }
                else g.DrawImage(backgroundBrush.Image, CanvasRectangle);
                g.CompositingMode = CompositingMode.SourceOver;
            }
        }

        // Fast path deliberately excludes shapes whose pixels cannot be described by
        // rectangular selections and small overlays. They keep the established full paint.
        private RegionCaptureDamage CaptureDamage(Graphics g)
        {
            if (IsEditorMode || IsZoomed || ShapeManager.IsPanning || Options.ShowCrosshair || Options.ShowFPS ||
                Options.DetectWindows || (Mode != RegionCaptureMode.Default && Mode != RegionCaptureMode.Annotation) ||
                !ShapeManager.IsCurrentShapeTypeRegion || ShapeManager.IsSnapResizing ||
                CanvasRectangle.Location != PointF.Empty || CanvasRectangle.Size != ClientArea.Size || CanvasRectangle.Size != Canvas.Size ||
                ShapeManager.DrawingShapes.Length > 0 || ShapeManager.EffectShapes.Length > 0 || ShapeManager.ToolShapes.Length > 0 ||
                regionAnimation.IsActive) return null;

            BaseShape[] selections = ShapeManager.ValidRegions;
            if (selections.Length > 32 || selections.Any(shape => shape is not RectangleRegionShape rectangle || rectangle.CornerRadius != 0)) return null;
            BaseShape hover = ShapeManager.IsCurrentHoverShapeValid ? ShapeManager.CurrentHoverShape : null;
            if (hover != null && (hover is not RectangleRegionShape rectangle || rectangle.CornerRadius != 0 ||
                (Options.EnableAnimations && ShapeManager.PreviousHoverRectangle != hover.Rectangle))) return null;

            var damage = new RegionCaptureDamage(ClientArea);
            foreach (BaseShape selection in selections)
            {
                // DrawShapes uses the -1 path for the restored selection background.
                RectangleF rect = selection.Rectangle;
                if (Options.BackgroundDimStrength > 0 && rect.Width > 1 && rect.Height > 1)
                    damage.Selections.Add(new RectangleF(rect.X, rect.Y, rect.Width - 1, rect.Height - 1));
                damage.AddBorder(rect);
                if (Options.ShowInfo) damage.AddOverlay(GetAreaTextBounds(g, GetAreaText(rect), rect));
                if (Options.ShowCenterCrosshair) damage.AddOverlay(new RectangleF(rect.X + rect.Width / 2 - 12, rect.Y + rect.Height / 2 - 12, 25, 25));
            }
            if (hover != null)
            {
                damage.AddBorder(hover.Rectangle);
                if (Options.ShowInfo) damage.AddOverlay(GetAreaTextBounds(g, GetAreaText(hover.Rectangle), hover.Rectangle));
            }
            if (!ShapeManager.IsCtrlModifier)
                foreach (ImageEditorControl control in ShapeManager.DrawableObjects.Where(control => control.Visible)) damage.AddOverlay(control.Rectangle);
            if (Options.ShowMagnifier || Options.ShowInfo) damage.AddOverlay(GetCursorOverlayBounds(g));
            if (IsAnnotationMode && ShapeManager.MenuTextAnimation.IsActive)
            {
                Size size = g.MeasureString(ShapeManager.MenuTextAnimation.Text, infoFontMedium).ToSize();
                damage.AddOverlay(new Rectangle(ShapeManager.MenuTextAnimation.Position, new Size(size.Width + 6, size.Height + 6)));
            }
            return damage;
        }

        private Rectangle GetCursorOverlayBounds(Graphics g)
        {
            int width = 0, height = 0;
            if (Options.ShowMagnifier)
            {
                int count = (Options.MagnifierPixelCount | 1).Clamp(1, 101), size = Options.MagnifierPixelSize.Clamp(1, 1000);
                if (count * size > ClientArea.Width || count * size > ClientArea.Height) { count = 15; size = 10; }
                width = height = count * size - 1;
            }
            if (Options.ShowInfo)
            {
                CurrentPosition = InputManager.MousePosition;
                Size textSize = g.MeasureString(GetInfoText(), infoFont).ToSize();
                width = Math.Max(width, textSize.Width + 6);
                height += (Options.ShowMagnifier ? 10 : 0) + textSize.Height + 6;
            }
            Point mouse = InputManager.ClientMousePosition;
            Rectangle screen = RectangleToClient(Screen.FromPoint(InputManager.MousePosition).Bounds);
            int x = mouse.X + 10, y = mouse.Y + 10;
            if (x + width > screen.Right) x = mouse.X - 10 - width;
            if (y + height > screen.Bottom) y = mouse.Y - 10 - height;
            return new Rectangle(x, y, width, height);
        }

        private void DrawShapes(Graphics g)
        {
            // Draw snap rectangles
            if (ShapeManager.IsCreating && ShapeManager.IsSnapResizing)
            {
                BaseShape shape = ShapeManager.CurrentShape;

                if (shape != null && shape.ShapeType != ShapeType.RegionFreehand && shape.ShapeType != ShapeType.DrawingFreehand)
                {
                    foreach (Size size in Options.SnapSizes)
                    {
                        RectangleF snapRect = CaptureHelpers.CalculateNewRectangle(shape.StartPosition, shape.EndPosition, size);
                        g.DrawRectangleProper(markerPen, snapRect);
                    }
                }
            }

            List<BaseShape> areas = ShapeManager.ValidRegions.ToList();

            if (areas.Count > 0)
            {
                // Create graphics path from all regions
                UpdateRegionPath();

                // If background is dimmed then draw non dimmed background to region selections
                if (!IsEditorMode && Options.BackgroundDimStrength > 0 && backgroundHighlightBrush != null)
                {
                    using (Region region = new Region(regionDrawPath))
                    {
                        GraphicsState selectionState = g.Save();
                        g.SetClip(region, CombineMode.Intersect);
                        g.FillRectangle(backgroundHighlightBrush, ClientArea);
                        g.Restore(selectionState);
                    }
                }

                g.DrawPath(borderPen, regionDrawPath);
                g.DrawPath(borderDotStaticPen, regionDrawPath);
            }

            // Draw effect shapes
            foreach (BaseEffectShape effectShape in ShapeManager.EffectShapes)
            {
                effectShape.OnDraw(g);
            }

            // Draw drawing shapes
            foreach (BaseDrawingShape drawingShape in ShapeManager.DrawingShapes)
            {
                drawingShape.OnDraw(g);
            }

            // Draw tools
            foreach (BaseTool toolShape in ShapeManager.ToolShapes)
            {
                toolShape.OnDraw(g);
            }

            // Draw animated rectangle on hover area
            if (ShapeManager.IsCurrentHoverShapeValid)
            {
                if (Options.EnableAnimations)
                {
                    if (!ShapeManager.PreviousHoverRectangle.IsEmpty && ShapeManager.CurrentHoverShape.Rectangle != ShapeManager.PreviousHoverRectangle)
                    {
                        if (regionAnimation.CurrentRectangle.Width > 2 && regionAnimation.CurrentRectangle.Height > 2)
                        {
                            regionAnimation.FromRectangle = regionAnimation.CurrentRectangle;
                        }
                        else
                        {
                            regionAnimation.FromRectangle = ShapeManager.PreviousHoverRectangle;
                        }

                        regionAnimation.ToRectangle = ShapeManager.CurrentHoverShape.Rectangle;
                        regionAnimation.Start();
                    }

                    regionAnimation.Update();
                }

                using (GraphicsPath hoverDrawPath = new GraphicsPath { FillMode = FillMode.Winding })
                {
                    if (Options.EnableAnimations && regionAnimation.IsActive && regionAnimation.CurrentRectangle.Width > 2 && regionAnimation.CurrentRectangle.Height > 2)
                    {
                        ShapeManager.CurrentHoverShape.OnShapePathRequested(hoverDrawPath, regionAnimation.CurrentRectangle.SizeOffset(-1));
                    }
                    else
                    {
                        ShapeManager.CurrentHoverShape.AddShapePath(hoverDrawPath, -1);
                    }

                    g.DrawPath(borderPen, hoverDrawPath);
                    g.DrawPath(borderDotPen, hoverDrawPath);
                }
            }

            // Draw animated rectangle on selection area
            if (ShapeManager.IsCurrentShapeTypeRegion && ShapeManager.IsCurrentShapeValid)
            {
                if (Mode == RegionCaptureMode.Ruler)
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                    {
                        g.FillRectangle(brush, ShapeManager.CurrentRectangle);
                    }

                    DrawRuler(g, ShapeManager.CurrentRectangle, borderPen, 5, 10);
                    DrawRuler(g, ShapeManager.CurrentRectangle, borderPen, 15, 100);
                }

                if (Options.ShowCenterCrosshair)
                {
                    g.DrawCross(Pens.Black, ShapeManager.CurrentRectangle.Center().Add(-1, -1), 10);
                    g.DrawCross(Pens.White, ShapeManager.CurrentRectangle.Center(), 10);
                }

                DrawRegionArea(g, ShapeManager.CurrentRectangle, true);
            }

            // Draw all regions rectangle info
            if (Options.ShowInfo)
            {
                // Add hover area to list so rectangle info can be shown
                if (ShapeManager.IsCurrentShapeTypeRegion && ShapeManager.IsCurrentHoverShapeValid && areas.All(area => area.Rectangle != ShapeManager.CurrentHoverShape.Rectangle))
                {
                    areas.Add(ShapeManager.CurrentHoverShape);
                }

                foreach (BaseShape regionInfo in areas)
                {
                    if (regionInfo.Rectangle.IsValid())
                    {
                        string areaText = GetAreaText(regionInfo.Rectangle);
                        DrawAreaText(g, areaText, regionInfo.Rectangle);
                    }
                }
            }

            // Draw resize nodes
            ShapeManager.DrawObjects(g);

            // Draw magnifier
            if (Options.ShowMagnifier || Options.ShowInfo)
            {
                DrawCursorGraphics(g);
            }

            // Draw screen wide crosshair
            if (Options.ShowCrosshair)
            {
                DrawCrosshair(g);
            }

            // Draw image editor bottom tip
            if (IsEditorMode && Options.ShowEditorPanTip && editorPanTipAnimation != null && editorPanTipAnimation.Update())
            {
                DrawBottomTipAnimation(g, editorPanTipAnimation);
            }

            // Draw menu tooltips
            if (IsAnnotationMode && ShapeManager.MenuTextAnimation.Update())
            {
                DrawTextAnimation(g, ShapeManager.MenuTextAnimation);
            }
        }

        internal void DrawRegionArea(Graphics g, RectangleF rect, bool isAnimated)
        {
            g.DrawRectangleProper(borderPen, rect);

            if (isAnimated)
            {
                g.DrawRectangleProper(borderDotPen, rect);
            }
            else
            {
                g.DrawRectangleProper(borderDotStaticPen, rect);
            }
        }

        internal void DrawRegionAreaEllipse(Graphics g, RectangleF rect, bool isAnimated)
        {
            g.DrawEllipse(borderPen, rect);

            if (isAnimated)
            {
                g.DrawEllipse(borderDotPen, rect);
            }
            else
            {
                g.DrawEllipse(borderDotStaticPen, rect);
            }
        }

        private void DrawFPS(Graphics g, int offset)
        {
            Point textPosition = new Point(offset, offset);

            if (IsFullscreen)
            {
                Rectangle rectScreen = CaptureHelpers.GetActiveScreenBounds();
                rectScreen = RectangleToClient(rectScreen);
                textPosition = textPosition.Add(rectScreen.Location);
            }

            g.DrawTextWithShadow(FPSManager.FPS.ToString(), textPosition, infoFontBig, Brushes.White, Brushes.Black, new Point(0, 1));
        }

        private void DrawInfoText(Graphics g, string text, RectangleF rect, Font font, int padding)
        {
            DrawInfoText(g, text, rect, font, new Point(padding, padding));
        }

        private void DrawInfoText(Graphics g, string text, RectangleF rect, Font font, Point padding)
        {
            DrawInfoText(g, text, rect, font, padding, textBackgroundBrush, textOuterBorderPen, textInnerBorderPen, textBrush, textShadowBrush);
        }

        private void DrawInfoText(Graphics g, string text, RectangleF rect, Font font, int padding,
            Brush backgroundBrush, Pen outerBorderPen, Pen innerBorderPen, Brush textBrush, Brush textShadowBrush)
        {
            DrawInfoText(g, text, rect, font, new Point(padding, padding), backgroundBrush, outerBorderPen, innerBorderPen, textBrush, textShadowBrush);
        }

        private void DrawInfoText(Graphics g, string text, RectangleF rect, Font font, Point padding,
            Brush backgroundBrush, Pen outerBorderPen, Pen innerBorderPen, Brush textBrush, Brush textShadowBrush)
        {
            g.FillRectangle(backgroundBrush, rect.Offset(-2));
            g.DrawRectangleProper(innerBorderPen, rect.Offset(-1));
            g.DrawRectangleProper(outerBorderPen, rect);

            g.DrawTextWithShadow(text, rect.LocationOffset(padding.X, padding.Y).Location, font, textBrush, textShadowBrush);
        }

        internal void DrawAreaText(Graphics g, string text, RectangleF area)
        {
            DrawInfoText(g, text, GetAreaTextBounds(g, text, area), infoFont, 3);
        }

        private RectangleF GetAreaTextBounds(Graphics g, string text, RectangleF area)
        {
            int offset = 6;
            int backgroundPadding = 3;
            Size textSize = g.MeasureString(text, infoFont).ToSize();
            PointF textPos;

            if (area.Y - offset - textSize.Height - (backgroundPadding * 2) < ClientArea.Y)
            {
                textPos = new PointF(area.X + offset + backgroundPadding, area.Y + offset + backgroundPadding);
            }
            else
            {
                textPos = new PointF(area.X + backgroundPadding, area.Y - offset - backgroundPadding - textSize.Height);
            }

            if (textPos.X + textSize.Width + backgroundPadding >= ClientArea.Width)
            {
                textPos.X = ClientArea.Width - textSize.Width - backgroundPadding;
            }

            RectangleF backgroundRect = new RectangleF(textPos.X - backgroundPadding, textPos.Y - backgroundPadding, textSize.Width + (backgroundPadding * 2), textSize.Height + (backgroundPadding * 2));

            return backgroundRect;
        }

        private void DrawTextAnimation(Graphics g, TextAnimation textAnimation)
        {
            Size textSize = g.MeasureString(textAnimation.Text, infoFontMedium).ToSize();
            int padding = 3;
            textSize.Width += padding * 2;
            textSize.Height += padding * 2;
            Rectangle textRectangle = new Rectangle(textAnimation.Position.X, textAnimation.Position.Y, textSize.Width, textSize.Height);
            DrawTextAnimation(g, textAnimation, textRectangle, padding);
        }

        private void DrawTextAnimation(Graphics g, TextAnimation textAnimation, Rectangle textRectangle, int padding)
        {
            using (Brush backgroundBrush = new SolidBrush(Color.FromArgb((int)(textAnimation.Opacity * 200), textBackgroundColor)))
            using (Pen outerBorderPen = new Pen(Color.FromArgb((int)(textAnimation.Opacity * 200), textOuterBorderColor)))
            using (Pen innerBorderPen = new Pen(Color.FromArgb((int)(textAnimation.Opacity * 200), textInnerBorderColor)))
            using (Brush textBrush = new SolidBrush(Color.FromArgb((int)(textAnimation.Opacity * 255), textColor)))
            using (Brush textShadowBrush = new SolidBrush(Color.FromArgb((int)(textAnimation.Opacity * 255), textShadowColor)))
            {
                Matrix transform = g.Transform;
                ZoomTransform(g, true);
                DrawInfoText(g, textAnimation.Text, textRectangle, infoFontMedium, padding, backgroundBrush, outerBorderPen, innerBorderPen, textBrush, textShadowBrush);
                g.Transform = transform;
            }
        }

        private void DrawBottomTipAnimation(Graphics g, TextAnimation textAnimation)
        {
            Size textSize = g.MeasureString(textAnimation.Text, infoFontMedium).ToSize();
            int padding = 5;
            textSize.Width += padding * 2;
            textSize.Height += padding * 2;
            int margin = 20;
            Rectangle textRectangle = new Rectangle((ClientArea.Width / 2) - (textSize.Width / 2), ClientArea.Height - textSize.Height - margin, textSize.Width, textSize.Height);
            DrawTextAnimation(g, textAnimation, textRectangle, padding);
        }

        internal string GetAreaText(RectangleF rect)
        {
            if (IsEditorMode)
            {
                rect = new RectangleF(rect.X - CanvasRectangle.X, rect.Y - CanvasRectangle.Y, rect.Width, rect.Height);
            }
            else if (Mode == RegionCaptureMode.Ruler)
            {
                PointF endLocation = new PointF(rect.Right - 1, rect.Bottom - 1);
                string text = $"X: {rect.X} | Y: {rect.Y} | {Resources.RulerRight}: {endLocation.X} | {Resources.RulerBottom}: {endLocation.Y}\r\n" +
                    $"{Resources.RulerWidth}: {rect.Width} px | {Resources.RulerHeight}: {rect.Height} px | {Resources.RulerArea}: {rect.Area()} px | {Resources.RulerPerimeter}: {rect.Perimeter()} px\r\n" +
                    $"{Resources.RulerDistance}: {MathHelpers.Distance(rect.Location, endLocation):0.00} px | {Resources.RulerAngle}: {MathHelpers.LookAtDegree(rect.Location, endLocation):0.00}°";
                return text;
            }

            Rectangle area = rect.Round();
            return string.Format(Resources.RectangleRegion_GetAreaText_Area, area.X, area.Y, area.Width, area.Height);
        }

        private string GetInfoText()
        {
            if (IsEditorMode)
            {
                Point canvasRelativePosition = new PointF(ScaledClientMousePosition.X - CanvasRectangle.X, ScaledClientMousePosition.Y - CanvasRectangle.Y).Round();
                return $"X: {canvasRelativePosition.X} Y: {canvasRelativePosition.Y}";
            }
            else if (Mode == RegionCaptureMode.ScreenColorPicker || Options.UseCustomInfoText)
            {
                Color color = ShapeManager.GetCurrentColor();

                if (Mode == RegionCaptureMode.ScreenColorPicker)
                {
                    if (!string.IsNullOrEmpty(Options.ScreenColorPickerInfoText))
                    {
                        return CodeMenuEntryPixelInfo.Parse(Options.ScreenColorPickerInfoText, color, CurrentPosition);
                    }
                }
                else if (!string.IsNullOrEmpty(Options.CustomInfoText))
                {
                    return CodeMenuEntryPixelInfo.Parse(Options.CustomInfoText, color, CurrentPosition);
                }

                return string.Format(Resources.RectangleRegion_GetColorPickerText, color.R, color.G, color.B, ColorHelpers.ColorToHex(color), CurrentPosition.X, CurrentPosition.Y);
            }

            return $"X: {CurrentPosition.X} Y: {CurrentPosition.Y}";
        }

        private void DrawCrosshair(Graphics g)
        {
            int offset = 5;
            PointF mousePos = ScaledClientMousePosition;
            PointF left = new PointF(mousePos.X - offset, mousePos.Y), left2 = new PointF(0, mousePos.Y);
            PointF right = new PointF(mousePos.X + offset, mousePos.Y), right2 = new PointF((ClientArea.Width - 1) / ZoomFactor, mousePos.Y);
            PointF top = new PointF(mousePos.X, mousePos.Y - offset), top2 = new PointF(mousePos.X, 0);
            PointF bottom = new PointF(mousePos.X, mousePos.Y + offset), bottom2 = new PointF(mousePos.X, (ClientArea.Height - 1) / ZoomFactor);

            if (left.X - left2.X > 10)
            {
                g.DrawLine(borderPen, left, left2);
                g.DrawLine(borderDotPen, left, left2);
            }

            if (right2.X - right.X > 10)
            {
                g.DrawLine(borderPen, right, right2);
                g.DrawLine(borderDotPen, right, right2);
            }

            if (top.Y - top2.Y > 10)
            {
                g.DrawLine(borderPen, top, top2);
                g.DrawLine(borderDotPen, top, top2);
            }

            if (bottom2.Y - bottom.Y > 10)
            {
                g.DrawLine(borderPen, bottom, bottom2);
                g.DrawLine(borderDotPen, bottom, bottom2);
            }
        }

        private void DrawCursorGraphics(Graphics g)
        {
            int cursorOffsetX = 10, cursorOffsetY = 10, itemGap = 10, itemCount = 0;
            Size totalSize = Size.Empty;

            int magnifierPosition = 0;
            Bitmap magnifier = null;

            if (Options.ShowMagnifier)
            {
                if (itemCount > 0) totalSize.Height += itemGap;
                magnifierPosition = totalSize.Height;

                magnifier = Magnifier(Canvas, ScaledClientMousePosition, Options.MagnifierPixelCount, Options.MagnifierPixelCount, Options.MagnifierPixelSize);
                totalSize.Width = Math.Max(totalSize.Width, magnifier.Width);

                totalSize.Height += magnifier.Height;
                itemCount++;
            }

            int infoTextPadding = 3;
            int infoTextPosition = 0;
            Rectangle infoTextRect = Rectangle.Empty;
            string infoText = "";

            if (Options.ShowInfo)
            {
                if (itemCount > 0) totalSize.Height += itemGap;
                infoTextPosition = totalSize.Height;

                CurrentPosition = InputManager.MousePosition;
                infoText = GetInfoText();
                Size textSize = g.MeasureString(infoText, infoFont).ToSize();
                infoTextRect.Size = new Size(textSize.Width + (infoTextPadding * 2), textSize.Height + (infoTextPadding * 2));
                totalSize.Width = Math.Max(totalSize.Width, infoTextRect.Width);

                totalSize.Height += infoTextRect.Height;
                //itemCount++;
            }

            Point mousePos = InputManager.ClientMousePosition;
            Rectangle activeScreenClientRect = RectangleToClient(Screen.FromPoint(InputManager.MousePosition).Bounds);
            int x = mousePos.X + cursorOffsetX;

            if (x + totalSize.Width > activeScreenClientRect.Right)
            {
                x = mousePos.X - cursorOffsetX - totalSize.Width;
            }

            int y = mousePos.Y + cursorOffsetY;

            if (y + totalSize.Height > activeScreenClientRect.Bottom)
            {
                y = mousePos.Y - cursorOffsetY - totalSize.Height;
            }

            Matrix initialTranform = g.Transform;
            if (Options.ShowMagnifier)
            {
                ZoomTransform(g, true);
                if (Options.UseSquareMagnifier)
                {
                    g.DrawImage(magnifier, x, y + magnifierPosition, magnifier.Width, magnifier.Height);
                    g.DrawRectangleProper(Pens.White, x - 1, y + magnifierPosition - 1, magnifier.Width + 2, magnifier.Height + 2);
                    g.DrawRectangleProper(Pens.Black, x, y + magnifierPosition, magnifier.Width, magnifier.Height);
                }
                else
                {
                    using (GraphicsQualityManager quality = new GraphicsQualityManager(g, true))
                    using (TextureBrush brush = new TextureBrush(magnifier))
                    {
                        brush.TranslateTransform(x, y + magnifierPosition);

                        g.FillEllipse(brush, x, y + magnifierPosition, magnifier.Width, magnifier.Height);
                        g.DrawEllipse(Pens.White, x - 1, y + magnifierPosition - 1, magnifier.Width + 2 - 1, magnifier.Height + 2 - 1);
                        g.DrawEllipse(Pens.Black, x, y + magnifierPosition, magnifier.Width - 1, magnifier.Height - 1);
                    }
                }
                g.Transform = initialTranform;
            }

            if (Options.ShowInfo)
            {
                if (Mode == RegionCaptureMode.ScreenColorPicker)
                {
                    int colorBoxOffset = 2;
                    int colorBoxSize = infoTextRect.Height - (colorBoxOffset * 2);
                    int textOffset = 4;
                    int colorBoxExtraWidth = colorBoxSize + textOffset;
                    infoTextRect.Width += colorBoxExtraWidth;
                    infoTextRect.Location = new Point(x + (totalSize.Width / 2) - (infoTextRect.Width / 2), y + infoTextPosition);
                    Point padding = new Point(infoTextPadding + colorBoxExtraWidth, infoTextPadding);

                    Rectangle colorRect = new Rectangle(infoTextRect.X + colorBoxOffset, infoTextRect.Y + colorBoxOffset, colorBoxSize, colorBoxSize);

                    DrawInfoText(g, infoText, infoTextRect, infoFont, padding);

                    using (Brush colorBrush = new SolidBrush(ShapeManager.GetCurrentColor()))
                    {
                        g.FillRectangle(colorBrush, colorRect);
                    }

                    g.DrawLine(textInnerBorderPen, colorRect.Right, colorRect.Top, colorRect.Right, colorRect.Bottom - 1);
                }
                else
                {
                    infoTextRect.Location = new Point(x + (totalSize.Width / 2) - (infoTextRect.Width / 2), y + infoTextPosition);
                    Point padding = new Point(infoTextPadding, infoTextPadding);

                    ZoomTransform(g, true);
                    DrawInfoText(g, infoText, infoTextRect, infoFont, padding);
                    g.Transform = initialTranform;
                }
            }
        }

        private Bitmap Magnifier(Image img, PointF position, int horizontalPixelCount, int verticalPixelCount, int pixelSize)
        {
            horizontalPixelCount = (horizontalPixelCount | 1).Clamp(1, 101);
            verticalPixelCount = (verticalPixelCount | 1).Clamp(1, 101);
            pixelSize = pixelSize.Clamp(1, 1000);

            if (horizontalPixelCount * pixelSize > ClientArea.Width || verticalPixelCount * pixelSize > ClientArea.Height)
            {
                horizontalPixelCount = verticalPixelCount = 15;
                pixelSize = 10;
            }

            RectangleF srcRect = new RectangleF(position.X - (horizontalPixelCount / 2) - CanvasRectangle.X,
                position.Y - (verticalPixelCount / 2) - CanvasRectangle.Y, horizontalPixelCount, verticalPixelCount).Round();

            int width = horizontalPixelCount * pixelSize;
            int height = verticalPixelCount * pixelSize;
            Bitmap bmp = new Bitmap(width - 1, height - 1);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

                if (!new RectangleF(0, 0, img.Width, img.Height).Contains(srcRect))
                {
                    g.Clear(canvasBackgroundColor);
                }

                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(img, new Rectangle(0, 0, width, height), srcRect, GraphicsUnit.Pixel);
                g.PixelOffsetMode = PixelOffsetMode.None;

                using (SolidBrush crosshairBrush = new SolidBrush(Color.FromArgb(125, Color.LightBlue)))
                {
                    g.FillRectangle(crosshairBrush, new Rectangle(0, (height - pixelSize) / 2, (width - pixelSize) / 2, pixelSize)); // Left
                    g.FillRectangle(crosshairBrush, new Rectangle((width + pixelSize) / 2, (height - pixelSize) / 2, (width - pixelSize) / 2, pixelSize)); // Right
                    g.FillRectangle(crosshairBrush, new Rectangle((width - pixelSize) / 2, 0, pixelSize, (height - pixelSize) / 2)); // Top
                    g.FillRectangle(crosshairBrush, new Rectangle((width - pixelSize) / 2, (height + pixelSize) / 2, pixelSize, (height - pixelSize) / 2)); // Bottom
                }

                using (Pen pen = new Pen(Color.FromArgb(75, Color.Black)))
                {
                    for (int x = 1; x < horizontalPixelCount; x++)
                    {
                        g.DrawLine(pen, new Point((x * pixelSize) - 1, 0), new Point((x * pixelSize) - 1, height - 1));
                    }

                    for (int y = 1; y < verticalPixelCount; y++)
                    {
                        g.DrawLine(pen, new Point(0, (y * pixelSize) - 1), new Point(width - 1, (y * pixelSize) - 1));
                    }
                }

                g.DrawRectangle(Pens.Black, ((width - pixelSize) / 2) - 1, ((height - pixelSize) / 2) - 1, pixelSize, pixelSize);

                if (pixelSize >= 6)
                {
                    g.DrawRectangle(Pens.White, (width - pixelSize) / 2, (height - pixelSize) / 2, pixelSize - 2, pixelSize - 2);
                }
            }

            return bmp;
        }

        private void DrawRuler(Graphics g, RectangleF rect, Pen pen, int rulerSize, int rulerWidth)
        {
            if (rect.Width >= rulerSize && rect.Height >= rulerSize)
            {
                for (int x = 1; x <= rect.Width / rulerWidth; x++)
                {
                    g.DrawLine(pen, new PointF(rect.X + (x * rulerWidth), rect.Y), new PointF(rect.X + (x * rulerWidth), rect.Y + rulerSize));
                    g.DrawLine(pen, new PointF(rect.X + (x * rulerWidth), rect.Bottom), new PointF(rect.X + (x * rulerWidth), rect.Bottom - rulerSize));
                }

                for (int y = 1; y <= rect.Height / rulerWidth; y++)
                {
                    g.DrawLine(pen, new PointF(rect.X, rect.Y + (y * rulerWidth)), new PointF(rect.X + rulerSize, rect.Y + (y * rulerWidth)));
                    g.DrawLine(pen, new PointF(rect.Right, rect.Y + (y * rulerWidth)), new PointF(rect.Right - rulerSize, rect.Y + (y * rulerWidth)));
                }
            }
        }

        internal void UpdateRegionPath()
        {
            if (regionFillPath != null)
            {
                regionFillPath.Dispose();
                regionFillPath = null;
            }

            if (regionDrawPath != null)
            {
                regionDrawPath.Dispose();
                regionDrawPath = null;
            }

            BaseShape[] areas = ShapeManager.ValidRegions;

            if (areas != null && areas.Length > 0)
            {
                regionFillPath = new GraphicsPath { FillMode = FillMode.Winding };
                regionDrawPath = new GraphicsPath { FillMode = FillMode.Winding };

                foreach (BaseShape regionShape in ShapeManager.ValidRegions)
                {
                    regionShape.AddShapePath(regionFillPath);
                    regionShape.AddShapePath(regionDrawPath, -1);
                }
            }
        }

        public Point? ResultScreenLocation { get; private set; }

        public Bitmap GetResultImage()
        {
            ResultScreenLocation = null;

            if (IsEditorMode)
            {
                return ShapeManager.RenderOutputImage(Canvas, CanvasRectangle.Location);
            }
            else if (Result == RegionResult.Region || Result == RegionResult.LastRegion)
            {
                GraphicsPath gp;

                if (Result == RegionResult.LastRegion)
                {
                    gp = LastRegionFillPath;
                }
                else
                {
                    gp = regionFillPath;
                }

                if (gp != null)
                {
                    using (Bitmap bmp = RegionCaptureTasks.ApplyRegionPathToImage(Canvas, gp, out Rectangle rect))
                    {
                        // Use the actual output crop, including multiple regions, rather than the active annotation shape.
                        ResultScreenLocation = new Point(ScreenBounds.Left + rect.X, ScreenBounds.Top + rect.Y);
                        return ShapeManager.RenderOutputImage(bmp, rect.Location);
                    }
                }
            }
            else if (Result == RegionResult.Fullscreen)
            {
                ResultScreenLocation = ScreenBounds.Location;
                return ShapeManager.RenderOutputImage(Canvas);
            }
            else if (Result == RegionResult.Monitor)
            {
                Screen[] screens = Screen.AllScreens;

                if (MonitorIndex < screens.Length)
                {
                    Screen screen = screens[MonitorIndex];
                    ResultScreenLocation = screen.Bounds.Location;
                    Rectangle screenRect = RectangleToClient(screen.Bounds);

                    using (Bitmap bmp = ShapeManager.RenderOutputImage(Canvas))
                    {
                        return ImageHelpers.CropBitmap(bmp, screenRect);
                    }
                }
            }
            else if (Result == RegionResult.ActiveMonitor)
            {
                Rectangle activeScreenBounds = CaptureHelpers.GetActiveScreenBounds();
                ResultScreenLocation = activeScreenBounds.Location;
                Rectangle activeScreenRect = RectangleToClient(activeScreenBounds);

                using (Bitmap bmp = ShapeManager.RenderOutputImage(Canvas))
                {
                    return ImageHelpers.CropBitmap(bmp, activeScreenRect);
                }
            }

            return null;
        }

        private Bitmap ReceiveImageForTask()
        {
            Bitmap bmp = GetResultImage();

            if (Options.AutoCloseEditorOnTask)
            {
                CloseWindow();
            }

            return bmp;
        }

        public Rectangle GetSelectedRectangle()
        {
            Rectangle rect = Rectangle.Empty;

            if (Result == RegionResult.Region)
            {
                if (ShapeManager.IsCurrentShapeValid)
                {
                    rect = CaptureHelpers.ClientToScreen(ShapeManager.CurrentRectangle.Round());
                }
            }
            else if (Result == RegionResult.Fullscreen)
            {
                rect = CaptureHelpers.GetScreenBounds();
            }
            else if (Result == RegionResult.Monitor)
            {
                Screen[] screens = Screen.AllScreens;

                if (MonitorIndex < screens.Length)
                {
                    Screen screen = screens[MonitorIndex];
                    rect = screen.Bounds;
                }
            }
            else if (Result == RegionResult.ActiveMonitor)
            {
                rect = CaptureHelpers.GetActiveScreenBounds();
            }

            return rect;
        }

        internal void OnSaveImageRequested()
        {
            if (SaveImageRequested != null)
            {
                Bitmap bmp = ReceiveImageForTask();

                string imageFilePath = SaveImageRequested(bmp, ImageFilePath);

                if (!string.IsNullOrEmpty(imageFilePath))
                {
                    ImageFilePath = imageFilePath;
                    UpdateTitle();
                    ShapeManager.ShowMenuTooltip(Resources.ImageSaved);
                    ShapeManager.IsImageModified = false;
                }
            }
        }

        internal void OnSaveImageAsRequested()
        {
            if (SaveImageAsRequested != null)
            {
                Bitmap bmp = ReceiveImageForTask();

                string imageFilePath = SaveImageAsRequested(bmp, ImageFilePath);

                if (!string.IsNullOrEmpty(imageFilePath))
                {
                    ImageFilePath = imageFilePath;
                    UpdateTitle();
                    ShapeManager.ShowMenuTooltip(Resources.ImageSavedAs);
                    ShapeManager.IsImageModified = false;
                }
            }
        }

        internal void OnCopyImageRequested()
        {
            if (CopyImageRequested != null)
            {
                using (Bitmap bmp = ReceiveImageForTask())
                {
                    if (bmp != null)
                    {
                        CopyImageRequested(bmp);

                        ShapeManager.ShowMenuTooltip(Resources.ImageCopied);
                        ShapeManager.IsImageModified = false;
                    }
                }
            }
        }

        internal void OnCopyImageRequested(Bitmap bmp)
        {
            if (CopyImageRequested != null && bmp != null)
            {
                CopyImageRequested(bmp);
            }
        }

        internal void OnUploadImageRequested()
        {
            if (UploadImageRequested != null)
            {
                Bitmap bmp = ReceiveImageForTask();

                UploadImageRequested(bmp);
                ShapeManager.ShowMenuTooltip(Resources.ImageUploading);
                ShapeManager.IsImageModified = false;
            }
        }

        internal void OnPrintImageRequested()
        {
            if (PrintImageRequested != null)
            {
                Bitmap bmp = ReceiveImageForTask();

                PrintImageRequested(bmp);
                ShapeManager.IsImageModified = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            IsClosing = true;
            refreshTimer?.Stop();
            refreshTimer?.Dispose();
            refreshTimer = null;
            if (disposing && paintedFrames > 0)
            {
                DebugHelper.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[RegionCapturePerformance] mode={0}; canvas={1}x{2}; monitors={3}; requestedFPS={4}; activeFPSLimit={5}; frames={6}; averagePaintMs={7:F2}; maxPaintMs={8:F2}; slowFramesOver33ms={9}; sessionMs={10}; averageBackgroundMs={11:F2}; averageShapesMs={12:F2}; updateTotalMs={13:F2}; partialFrames={14}; averagePaintPixels={15:F0}",
                    Mode, Canvas?.Width ?? 0, Canvas?.Height ?? 0, Screen.AllScreens.Length, Options.FPSLimit,
                    Math.Clamp(Options.FPSLimit > 0 ? Options.FPSLimit : 60, 1, 60), paintedFrames,
                    paintMilliseconds / paintedFrames, maximumPaintMilliseconds, slowFrames, refreshClock.ElapsedMilliseconds,
                    backgroundMilliseconds / paintedFrames, shapesMilliseconds / paintedFrames, updateMilliseconds, partialFrames, paintedPixels / paintedFrames));
                paintedFrames = 0;
            }

            ShapeManager?.Dispose();
            scheduledDamage?.Dispose();
            backgroundBrush?.Dispose();
            backgroundHighlightBrush?.Dispose();
            borderPen?.Dispose();
            borderDotPen?.Dispose();
            borderDotStaticPen?.Dispose();
            infoFont?.Dispose();
            infoFontMedium?.Dispose();
            infoFontBig?.Dispose();
            textBrush?.Dispose();
            textShadowBrush?.Dispose();
            textBackgroundBrush?.Dispose();
            textOuterBorderPen?.Dispose();
            textInnerBorderPen?.Dispose();
            markerPen?.Dispose();
            canvasBorderPen?.Dispose();
            defaultCursor?.Dispose();
            openHandCursor?.Dispose();
            closedHandCursor?.Dispose();
            CustomNodeImage?.Dispose();

            if (regionFillPath != null)
            {
                if (Result == RegionResult.Region)
                {
                    LastRegionFillPath?.Dispose();
                    LastRegionFillPath = regionFillPath;
                }
                else
                {
                    regionFillPath.Dispose();
                }
            }

            regionDrawPath?.Dispose();
            DimmedCanvas?.Dispose();
            Canvas?.Dispose();

            base.Dispose(disposing);
        }
    }
}
