#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfDocuments = System.Windows.Documents;
using WpfInk = System.Windows.Ink;
using WpfInput = System.Windows.Input;
using WpfInterop = System.Windows.Interop;
using WpfMedia = System.Windows.Media;
using WpfThreading = System.Windows.Threading;

namespace ShareX
{
    internal sealed class PresentationCanvasWindow : Wpf.Window
    {
        private enum CanvasTool
        {
            Pen,
            Eraser,
            Line,
            Arrow,
            Rectangle,
            Ellipse,
            Text
        }

        private sealed class HistoryEntry
        {
            public Action Undo { get; init; }
            public Action Redo { get; init; }
        }

        private const double ToolbarScale = 0.75;
        private readonly Rectangle screenBounds = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        private readonly WpfControls.Grid root = new WpfControls.Grid();
        private readonly WpfControls.Border canvasBorder = new WpfControls.Border();
        private readonly WpfControls.InkCanvas canvas = new WpfControls.InkCanvas();
        private readonly WpfControls.Border toolbar = new WpfControls.Border();
        private readonly WpfControls.WrapPanel toolbarItems = new WpfControls.WrapPanel();
        private readonly WpfControls.ComboBox backgroundSelector = new WpfControls.ComboBox();
        private readonly WpfControls.ComboBox colorSelector = new WpfControls.ComboBox();
        private readonly WpfControls.ComboBox widthSelector = new WpfControls.ComboBox();
        private readonly Dictionary<CanvasTool, WpfControls.Button> toolButtons = new Dictionary<CanvasTool, WpfControls.Button>();
        private readonly Stack<HistoryEntry> undoHistory = new Stack<HistoryEntry>();
        private readonly Stack<HistoryEntry> redoHistory = new Stack<HistoryEntry>();
        private readonly List<WpfInk.Stroke> previewStrokes = new List<WpfInk.Stroke>();
        private readonly (string Name, WpfMedia.Color Color)[] colors =
        {
            ("红", WpfMedia.Colors.Red),
            ("黄", WpfMedia.Colors.Gold),
            ("蓝", WpfMedia.Colors.DeepSkyBlue),
            ("白", WpfMedia.Colors.White),
            ("黑", WpfMedia.Colors.Black)
        };

        private CanvasTool currentTool = CanvasTool.Pen;
        private WpfMedia.Color currentColor = WpfMedia.Colors.Red;
        private double currentWidth = 4;
        private bool shapeDrawing;
        private Wpf.Point shapeStart;
        private bool canvasDirty;
        private bool allowClose;
        private bool saveOnClosePending;

        public PresentationCanvasWindow()
        {
            Title = "幕布";
            WindowStyle = Wpf.WindowStyle.None;
            ResizeMode = Wpf.ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = WpfMedia.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Focusable = true;
            // The production host uses a WinForms message loop. Without this bridge
            // WPF editors miss keyboard preprocessing, including spaces and Enter.
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);

            BuildCanvas();
            BuildToolbar();
            SetBackgroundMode(1);
            SelectTool(CanvasTool.Pen);

            Content = root;
            SourceInitialized += (sender, e) => ApplySelectedScreenBounds();
            Loaded += async (sender, e) =>
            {
                await Task.Delay(120);
                ApplySelectedScreenBounds();
            };
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void BuildCanvas()
        {
            canvas.Background = WpfMedia.Brushes.White;
            canvas.EditingMode = WpfControls.InkCanvasEditingMode.Ink;
            canvas.UseCustomCursor = true;
            canvas.Cursor = WpfInput.Cursors.Pen;
            canvas.StrokeCollected += Canvas_StrokeCollected;
            canvas.StrokeErasing += Canvas_StrokeErasing;
            canvas.PreviewMouseLeftButtonDown += Canvas_PreviewMouseLeftButtonDown;
            canvas.PreviewMouseMove += Canvas_PreviewMouseMove;
            canvas.PreviewMouseLeftButtonUp += Canvas_PreviewMouseLeftButtonUp;
            root.Children.Add(canvas);
            canvasBorder.BorderThickness = new Wpf.Thickness(1);
            canvasBorder.IsHitTestVisible = false;
            WpfControls.Panel.SetZIndex(canvasBorder, 90);
            root.Children.Add(canvasBorder);
        }

        private void BuildToolbar()
        {
            toolbar.HorizontalAlignment = Wpf.HorizontalAlignment.Center;
            toolbar.VerticalAlignment = Wpf.VerticalAlignment.Top;
            toolbar.Margin = new Wpf.Thickness(8);
            toolbar.Padding = new Wpf.Thickness(7, 5, 7, 5);
            toolbar.CornerRadius = new Wpf.CornerRadius(6);
            toolbar.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(128, 37, 37, 37));
            toolbar.LayoutTransform = new WpfMedia.ScaleTransform(ToolbarScale, ToolbarScale);
            toolbar.BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(90, 255, 255, 255));
            toolbar.BorderThickness = new Wpf.Thickness(1);
            WpfControls.Panel.SetZIndex(toolbar, 100);

            toolbarItems.Orientation = WpfControls.Orientation.Horizontal;
            toolbarItems.VerticalAlignment = Wpf.VerticalAlignment.Center;
            toolbar.Child = toolbarItems;

            AddToolButton(CanvasTool.Pen, "✎", "画笔");
            AddToolButton(CanvasTool.Eraser, "⌫", "橡皮擦");
            AddSeparator();
            AddToolButton(CanvasTool.Line, "╱", "直线");
            AddToolButton(CanvasTool.Arrow, "➜", "箭头");
            AddToolButton(CanvasTool.Rectangle, "□", "矩形");
            AddToolButton(CanvasTool.Ellipse, "○", "椭圆");
            AddToolButton(CanvasTool.Text, "T", "文字；Ctrl+Enter 完成输入");
            AddSeparator();

            ConfigureSelector(colorSelector, 52);
            foreach ((string name, WpfMedia.Color color) in colors)
            {
                colorSelector.Items.Add(CreateSelectorText(name, color));
            }
            colorSelector.SelectedIndex = 0;
            colorSelector.ToolTip = "颜色";
            colorSelector.SelectionChanged += (sender, e) =>
            {
                if (colorSelector.SelectedIndex >= 0)
                {
                    currentColor = colors[colorSelector.SelectedIndex].Color;
                    UpdateDrawingAttributes();
                }
            };
            toolbarItems.Children.Add(colorSelector);

            ConfigureSelector(widthSelector, 54);
            foreach (int width in new[] { 2, 4, 6, 10, 16 })
            {
                widthSelector.Items.Add(CreateSelectorText(width.ToString(), currentColor));
            }
            widthSelector.SelectedIndex = 1;
            widthSelector.ToolTip = "线宽";
            widthSelector.SelectionChanged += (sender, e) =>
            {
                if (double.TryParse((widthSelector.SelectedItem as WpfControls.TextBlock)?.Text, out double width))
                {
                    currentWidth = width;
                    UpdateDrawingAttributes();
                }
            };
            toolbarItems.Children.Add(widthSelector);
            AddSeparator();

            ConfigureSelector(backgroundSelector, 76);
            backgroundSelector.Items.Add(CreateSelectorText("透明", WpfMedia.Colors.LightGray));
            backgroundSelector.Items.Add(CreateSelectorText("白板", WpfMedia.Colors.White));
            backgroundSelector.Items.Add(CreateSelectorText("黑板", WpfMedia.Colors.Black));
            backgroundSelector.Items.Add(CreateSelectorText("网格", WpfMedia.Colors.LightSteelBlue));
            backgroundSelector.SelectedIndex = 1;
            backgroundSelector.ToolTip = "幕布背景";
            backgroundSelector.SelectionChanged += (sender, e) =>
            {
                SetBackgroundMode(backgroundSelector.SelectedIndex);
                canvasDirty = true;
            };
            toolbarItems.Children.Add(backgroundSelector);

            AddSeparator();

            toolbarItems.Children.Add(CreateCommandButton("↶", "撤销", (sender, e) => Undo()));
            toolbarItems.Children.Add(CreateCommandButton("↷", "重做", (sender, e) => Redo()));
            WpfControls.Button clearButton = CreateCommandButton("清", "清空", (sender, e) => ClearCanvas());
            clearButton.FontFamily = new WpfMedia.FontFamily("Microsoft YaHei");
            toolbarItems.Children.Add(clearButton);
            toolbarItems.Children.Add(CreateCommandButton("▣", "保存为 PNG", async (sender, e) => await SaveCanvasAsync()));
            toolbarItems.Children.Add(CreateCommandButton("⧉", "复制整个幕布", async (sender, e) => await CopyCanvasAsync()));

            WpfControls.Button closeButton = CreateCommandButton("×", "关闭幕布（Esc）", (sender, e) => Close());
            closeButton.Foreground = WpfMedia.Brushes.White;
            closeButton.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(176, 48, 48));
            toolbarItems.Children.Add(closeButton);

            root.Children.Add(toolbar);
        }

        private void AddToolButton(CanvasTool tool, string glyph, string tooltip)
        {
            WpfControls.Button button = CreateCommandButton(glyph, tooltip, (sender, e) => SelectTool(tool));
            toolButtons.Add(tool, button);
            toolbarItems.Children.Add(button);
        }

        private WpfControls.Button CreateCommandButton(string text, string tooltip, Wpf.RoutedEventHandler click)
        {
            WpfControls.Button button = new WpfControls.Button
            {
                Content = text,
                Width = 34,
                Height = 30,
                Margin = new Wpf.Thickness(2, 0, 2, 0),
                Padding = new Wpf.Thickness(0),
                FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
                FontSize = 16,
                Foreground = WpfMedia.Brushes.White,
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(55, 55, 55)),
                BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Wpf.Thickness(1),
                ToolTip = tooltip,
                Focusable = false
            };
            button.Click += click;
            return button;
        }

        private void ConfigureSelector(WpfControls.ComboBox selector, double width)
        {
            selector.Width = width;
            selector.Height = 30;
            selector.Margin = new Wpf.Thickness(2, 0, 2, 0);
            selector.Padding = new Wpf.Thickness(3, 0, 3, 0);
            selector.FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI");
            selector.FontSize = 13;
            selector.Foreground = WpfMedia.Brushes.Black;
            selector.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(142, 142, 142));
            selector.VerticalContentAlignment = Wpf.VerticalAlignment.Center;
            selector.HorizontalContentAlignment = Wpf.HorizontalAlignment.Center;
            selector.Template = (WpfControls.ControlTemplate)Wpf.Markup.XamlReader.Parse(SelectorTemplate);
        }

        private static WpfControls.TextBlock CreateSelectorText(string text, WpfMedia.Color color)
        {
            return new WpfControls.TextBlock
            {
                Text = text,
                Foreground = new WpfMedia.SolidColorBrush(color),
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                TextAlignment = Wpf.TextAlignment.Center
            };
        }

        // A centered content presenter avoids the stock Windows template's top-aligned
        // selection and white-on-white text. Neutral gray keeps both black and white legible.
        private const string SelectorTemplate = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
  <Grid>
    <ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
      <ToggleButton.Template>
        <ControlTemplate TargetType='ToggleButton'>
          <Border Background='#8E8E8E' BorderBrush='#80909090' BorderThickness='1' CornerRadius='3'>
            <Path Data='M 0,0 L 4,4 L 8,0' Stroke='#DDFFFFFF' StrokeThickness='1.2'
                  HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,5,0'/>
          </Border>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Content='{TemplateBinding SelectionBoxItem}'
        ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' IsHitTestVisible='False'
        HorizontalAlignment='Center' VerticalAlignment='Center' Margin='3,0,17,0'/>
    <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom'
           AllowsTransparency='True' Focusable='False'>
      <Border Background='#8E8E8E' BorderBrush='#FFBBBBBB' BorderThickness='1' Padding='2'
              MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
        <ScrollViewer MaxHeight='260' CanContentScroll='True'><ItemsPresenter/></ScrollViewer>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";

        private void AddSeparator()
        {
            toolbarItems.Children.Add(new WpfControls.Border
            {
                Width = 1,
                Height = 22,
                Margin = new Wpf.Thickness(4, 0, 4, 0),
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(70, 255, 255, 255))
            });
        }

        private void ApplySelectedScreenBounds()
        {
            IntPtr handle = new WpfInterop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            Rectangle bounds = screenBounds;
            int windowWidth = Math.Max(1, (int)Math.Round(bounds.Width * 0.6));
            int windowHeight = Math.Max(1, (int)Math.Round(bounds.Height * 0.6));
            int windowLeft = bounds.Left + (bounds.Width - windowWidth) / 2;
            int windowTop = bounds.Top + (bounds.Height - windowHeight) / 2;
            WpfMedia.Matrix deviceTransform = Wpf.PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
                ?? WpfMedia.Matrix.Identity;
            // Move first so Windows applies the target monitor DPI, then express the
            // WPF layout size in DIPs while keeping the native window pixel-perfect.
            SetWindowPos(handle, HWND_TOPMOST, windowLeft, windowTop, Math.Min(100, windowWidth), Math.Min(100, windowHeight),
                SWP_NOACTIVATE);
            uint dpi = GetDpiForWindow(handle);
            double scale = dpi > 0 ? dpi / 96d : 1d;
            Width = windowWidth / scale;
            Height = windowHeight / scale;
            // InkCanvas is hosted by a WinForms process. On high-DPI displays the
            // native window can report a virtualized WPF width after SetWindowPos,
            // so MaxWidth alone does not give WrapPanel a finite measure width.
            // Give both elements an explicit DIP width derived from the monitor's
            // physical bounds; this keeps every command visible and lets it wrap.
            double wpfDeviceScale = deviceTransform.M11 > 0 ? deviceTransform.M11 : scale;
            double visibleWidth = windowWidth / wpfDeviceScale;
            double maximumToolbarWidth = Math.Max(1, (visibleWidth - 16) / ToolbarScale);
            toolbar.Width = double.NaN;
            toolbar.MaxWidth = maximumToolbarWidth;
            toolbarItems.Width = double.NaN;
            toolbarItems.MaxWidth = Math.Max(1, maximumToolbarWidth - toolbar.Padding.Left - toolbar.Padding.Right - 2);
            toolbar.HorizontalAlignment = Wpf.HorizontalAlignment.Center;
            toolbar.Margin = new Wpf.Thickness(8);
            SetWindowPos(handle, HWND_TOPMOST, windowLeft, windowTop, windowWidth, windowHeight,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void SelectTool(CanvasTool tool)
        {
            currentTool = tool;
            RemovePreviewStrokes();
            shapeDrawing = false;

            foreach (KeyValuePair<CanvasTool, WpfControls.Button> pair in toolButtons)
            {
                pair.Value.Background = new WpfMedia.SolidColorBrush(
                    pair.Key == tool ? WpfMedia.Color.FromRgb(0, 120, 215) : WpfMedia.Color.FromRgb(55, 55, 55));
            }

            switch (tool)
            {
                case CanvasTool.Pen:
                    canvas.EditingMode = WpfControls.InkCanvasEditingMode.Ink;
                    canvas.Cursor = WpfInput.Cursors.Pen;
                    break;
                case CanvasTool.Eraser:
                    canvas.EditingMode = WpfControls.InkCanvasEditingMode.EraseByStroke;
                    canvas.Cursor = WpfInput.Cursors.Cross;
                    break;
                default:
                    canvas.EditingMode = WpfControls.InkCanvasEditingMode.None;
                    canvas.Cursor = WpfInput.Cursors.Cross;
                    break;
            }

            UpdateDrawingAttributes();
        }

        private void UpdateDrawingAttributes()
        {
            foreach (WpfControls.TextBlock item in widthSelector.Items)
            {
                item.Foreground = new WpfMedia.SolidColorBrush(currentColor);
            }
            canvas.DefaultDrawingAttributes = new WpfInk.DrawingAttributes
            {
                Color = currentColor,
                Width = currentWidth,
                Height = currentWidth,
                FitToCurve = true,
                IgnorePressure = false,
                IsHighlighter = false
            };
        }

        private void Canvas_StrokeCollected(object sender, WpfControls.InkCanvasStrokeCollectedEventArgs e)
        {
            WpfInk.Stroke stroke = e.Stroke;
            PushHistory(
                () => canvas.Strokes.Remove(stroke),
                () => canvas.Strokes.Add(stroke));
        }

        private void Canvas_StrokeErasing(object sender, WpfControls.InkCanvasStrokeErasingEventArgs e)
        {
            WpfInk.Stroke stroke = e.Stroke;
            PushHistory(
                () => canvas.Strokes.Add(stroke),
                () => canvas.Strokes.Remove(stroke));
        }

        private void Canvas_PreviewMouseLeftButtonDown(object sender, WpfInput.MouseButtonEventArgs e)
        {
            // TextBox hit tests usually return its internal ScrollViewer/TextBoxView.
            // Let the complete text subtree handle clicks and selection itself.
            if (FindTextEditor(e.OriginalSource as Wpf.DependencyObject) != null)
            {
                return;
            }

            if (currentTool == CanvasTool.Text)
            {
                AddTextEditor(e.GetPosition(canvas));
                e.Handled = true;
                return;
            }

            if (!IsShapeTool(currentTool))
            {
                return;
            }

            shapeDrawing = true;
            shapeStart = e.GetPosition(canvas);
            canvas.CaptureMouse();
            e.Handled = true;
        }

        private void Canvas_PreviewMouseMove(object sender, WpfInput.MouseEventArgs e)
        {
            if (!shapeDrawing || e.LeftButton != WpfInput.MouseButtonState.Pressed)
            {
                return;
            }

            UpdateShapePreview(shapeStart, e.GetPosition(canvas));
            e.Handled = true;
        }

        private void Canvas_PreviewMouseLeftButtonUp(object sender, WpfInput.MouseButtonEventArgs e)
        {
            if (!shapeDrawing)
            {
                return;
            }

            shapeDrawing = false;
            canvas.ReleaseMouseCapture();
            Wpf.Point end = e.GetPosition(canvas);

            if (Distance(shapeStart, end) < 3)
            {
                RemovePreviewStrokes();
                return;
            }

            UpdateShapePreview(shapeStart, end);
            WpfInk.Stroke[] committed = previewStrokes.ToArray();
            previewStrokes.Clear();

            PushHistory(
                () => RemoveStrokes(committed),
                () => AddStrokes(committed));
            e.Handled = true;
        }

        private static bool IsShapeTool(CanvasTool tool)
        {
            return tool == CanvasTool.Line || tool == CanvasTool.Arrow || tool == CanvasTool.Rectangle || tool == CanvasTool.Ellipse;
        }

        private void UpdateShapePreview(Wpf.Point start, Wpf.Point end)
        {
            RemovePreviewStrokes();
            IEnumerable<WpfInk.Stroke> strokes = CreateShapeStrokes(start, end);

            foreach (WpfInk.Stroke stroke in strokes)
            {
                previewStrokes.Add(stroke);
                canvas.Strokes.Add(stroke);
            }
        }

        private IEnumerable<WpfInk.Stroke> CreateShapeStrokes(Wpf.Point start, Wpf.Point end)
        {
            WpfInk.DrawingAttributes attributes = CreateShapeAttributes();

            if (currentTool == CanvasTool.Line)
            {
                yield return CreateStroke(new[] { start, end }, attributes);
            }
            else if (currentTool == CanvasTool.Arrow)
            {
                yield return CreateStroke(new[] { start, end }, attributes);
                double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
                double headLength = Math.Max(12, Math.Min(30, Distance(start, end) * 0.22));
                Wpf.Point wing1 = new Wpf.Point(
                    end.X - headLength * Math.Cos(angle - Math.PI / 6),
                    end.Y - headLength * Math.Sin(angle - Math.PI / 6));
                Wpf.Point wing2 = new Wpf.Point(
                    end.X - headLength * Math.Cos(angle + Math.PI / 6),
                    end.Y - headLength * Math.Sin(angle + Math.PI / 6));
                yield return CreateStroke(new[] { wing1, end, wing2 }, attributes);
            }
            else if (currentTool == CanvasTool.Rectangle)
            {
                yield return CreateStroke(new[]
                {
                    new Wpf.Point(start.X, start.Y),
                    new Wpf.Point(end.X, start.Y),
                    new Wpf.Point(end.X, end.Y),
                    new Wpf.Point(start.X, end.Y),
                    new Wpf.Point(start.X, start.Y)
                }, attributes);
            }
            else if (currentTool == CanvasTool.Ellipse)
            {
                double left = Math.Min(start.X, end.X);
                double top = Math.Min(start.Y, end.Y);
                double radiusX = Math.Abs(end.X - start.X) / 2;
                double radiusY = Math.Abs(end.Y - start.Y) / 2;
                double centerX = left + radiusX;
                double centerY = top + radiusY;
                List<Wpf.Point> points = new List<Wpf.Point>();

                for (int i = 0; i <= 64; i++)
                {
                    double angle = Math.PI * 2 * i / 64;
                    points.Add(new Wpf.Point(centerX + radiusX * Math.Cos(angle), centerY + radiusY * Math.Sin(angle)));
                }

                yield return CreateStroke(points, attributes);
            }
        }

        private WpfInk.DrawingAttributes CreateShapeAttributes()
        {
            return new WpfInk.DrawingAttributes
            {
                Color = currentColor,
                Width = currentWidth,
                Height = currentWidth,
                FitToCurve = false,
                IgnorePressure = true
            };
        }

        private static WpfInk.Stroke CreateStroke(IEnumerable<Wpf.Point> points, WpfInk.DrawingAttributes attributes)
        {
            WpfInput.StylusPointCollection stylusPoints = new WpfInput.StylusPointCollection(
                points.Select(point => new WpfInput.StylusPoint(point.X, point.Y)));
            return new WpfInk.Stroke(stylusPoints, attributes.Clone());
        }

        private void RemovePreviewStrokes()
        {
            if (previewStrokes.Count == 0)
            {
                return;
            }

            RemoveStrokes(previewStrokes);
            previewStrokes.Clear();
        }

        private void AddTextEditor(Wpf.Point location)
        {
            WpfControls.TextBox editor = new WpfControls.TextBox
            {
                Width = 320,
                MinHeight = 42,
                MaxHeight = 220,
                AcceptsReturn = true,
                TextWrapping = Wpf.TextWrapping.Wrap,
                FontFamily = new WpfMedia.FontFamily("Microsoft YaHei UI"),
                FontSize = 22,
                Foreground = new WpfMedia.SolidColorBrush(currentColor),
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(180, 32, 32, 32)),
                BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(150, 255, 255, 255)),
                BorderThickness = new Wpf.Thickness(1),
                Padding = new Wpf.Thickness(5),
                VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
                ToolTip = "Ctrl+Enter 完成输入；双击文字可再次编辑"
            };

            WpfControls.InkCanvas.SetLeft(editor, Math.Clamp(location.X, 0, Math.Max(0, canvas.ActualWidth - editor.Width)));
            WpfControls.InkCanvas.SetTop(editor, Math.Clamp(location.Y, 48, Math.Max(48, canvas.ActualHeight - editor.MinHeight)));
            canvas.Children.Add(editor);

            editor.PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == WpfInput.Key.Enter && (WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Control) != 0)
                {
                    canvas.Focus();
                    e.Handled = true;
                }
            };
            editor.GotKeyboardFocus += (sender, e) =>
            {
                editor.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(180, 32, 32, 32));
                editor.BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(150, 255, 255, 255));
            };
            editor.LostKeyboardFocus += (sender, e) =>
            {
                editor.Background = WpfMedia.Brushes.Transparent;
                editor.BorderBrush = WpfMedia.Brushes.Transparent;
            };
            editor.TextChanged += (sender, e) => canvasDirty = true;

            PushHistory(
                () => canvas.Children.Remove(editor),
                () => canvas.Children.Add(editor));

            // Wait until the newly added editor has a presentation source, and the
            // initiating mouse event has finished, before assigning keyboard focus.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (editor.IsLoaded && canvas.Children.Contains(editor))
                {
                    editor.Focus();
                    WpfInput.Keyboard.Focus(editor);
                }
            }), WpfThreading.DispatcherPriority.Input);
        }

        private static WpfControls.TextBox FindTextEditor(Wpf.DependencyObject source)
        {
            while (source != null)
            {
                if (source is WpfControls.TextBox editor) return editor;
                source = source is WpfMedia.Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? WpfMedia.VisualTreeHelper.GetParent(source) : Wpf.LogicalTreeHelper.GetParent(source);
            }
            return null;
        }

        private void PushHistory(Action undo, Action redo)
        {
            undoHistory.Push(new HistoryEntry { Undo = undo, Redo = redo });
            redoHistory.Clear();
            canvasDirty = true;
        }

        private void Undo()
        {
            if (undoHistory.Count == 0)
            {
                return;
            }

            HistoryEntry entry = undoHistory.Pop();
            entry.Undo();
            canvasDirty = true;
            redoHistory.Push(entry);
        }

        private void Redo()
        {
            if (redoHistory.Count == 0)
            {
                return;
            }

            HistoryEntry entry = redoHistory.Pop();
            entry.Redo();
            canvasDirty = true;
            undoHistory.Push(entry);
        }

        private void ClearCanvas()
        {
            WpfInk.Stroke[] strokes = canvas.Strokes.ToArray();
            Wpf.UIElement[] elements = canvas.Children.Cast<Wpf.UIElement>().ToArray();
            if (strokes.Length == 0 && elements.Length == 0)
            {
                return;
            }

            canvas.Strokes.Clear();
            canvas.Children.Clear();
            PushHistory(
                () =>
                {
                    AddStrokes(strokes);
                    foreach (Wpf.UIElement element in elements) canvas.Children.Add(element);
                },
                () =>
                {
                    RemoveStrokes(strokes);
                    foreach (Wpf.UIElement element in elements) canvas.Children.Remove(element);
                });
        }

        private void AddStrokes(IEnumerable<WpfInk.Stroke> strokes)
        {
            foreach (WpfInk.Stroke stroke in strokes)
            {
                if (!canvas.Strokes.Contains(stroke)) canvas.Strokes.Add(stroke);
            }
        }

        private void RemoveStrokes(IEnumerable<WpfInk.Stroke> strokes)
        {
            foreach (WpfInk.Stroke stroke in strokes.ToArray())
            {
                if (canvas.Strokes.Contains(stroke)) canvas.Strokes.Remove(stroke);
            }
        }

        private void SetBackgroundMode(int index)
        {
            canvasBorder.BorderBrush = index == 2 || index == 0
                ? WpfMedia.Brushes.White : WpfMedia.Brushes.Black;
            switch (index)
            {
                case 0:
                    canvas.Background = WpfMedia.Brushes.Transparent;
                    break;
                case 2:
                    canvas.Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(22, 25, 28));
                    break;
                case 3:
                    canvas.Background = CreateGridBrush();
                    break;
                default:
                    canvas.Background = WpfMedia.Brushes.White;
                    break;
            }
        }

        private static WpfMedia.Brush CreateGridBrush()
        {
            WpfMedia.DrawingGroup group = new WpfMedia.DrawingGroup();
            WpfMedia.Pen pen = new WpfMedia.Pen(
                new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(80, 70, 90, 110)), 1);
            group.Children.Add(new WpfMedia.GeometryDrawing(
                new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(242, 246, 249)), null,
                new WpfMedia.RectangleGeometry(new Wpf.Rect(0, 0, 24, 24))));
            group.Children.Add(new WpfMedia.GeometryDrawing(null, pen,
                WpfMedia.Geometry.Parse("M 24,0 L 24,24 M 0,24 L 24,24")));

            return new WpfMedia.DrawingBrush(group)
            {
                TileMode = WpfMedia.TileMode.Tile,
                Viewport = new Wpf.Rect(0, 0, 24, 24),
                ViewportUnits = WpfMedia.BrushMappingMode.Absolute,
                Stretch = WpfMedia.Stretch.None
            };
        }

        private async Task CopyCanvasAsync()
        {
            try
            {
                using Bitmap bitmap = await CaptureCanvasAsync();
                Forms.Clipboard.SetImage(bitmap);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                Forms.MessageBox.Show("复制幕布失败：" + ex.Message, "幕布", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
            }
        }

        private async Task<bool> SaveCanvasAsync()
        {
            using Forms.SaveFileDialog dialog = new Forms.SaveFileDialog
            {
                Filter = "PNG 图片|*.png",
                DefaultExt = "png",
                AddExtension = true,
                FileName = "幕布_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                return false;
            }

            try
            {
                using Bitmap bitmap = await CaptureCanvasAsync();
                bitmap.Save(dialog.FileName, ImageFormat.Png);
                canvasDirty = false;
                return true;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex);
                Forms.MessageBox.Show("保存幕布失败：" + ex.Message, "幕布", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
                return false;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!allowClose && canvasDirty)
            {
                Forms.DialogResult result = Forms.MessageBox.Show("幕布内容尚未保存。是否先保存为 PNG？",
                    "关闭幕布", Forms.MessageBoxButtons.YesNoCancel, Forms.MessageBoxIcon.Warning);
                if (result == Forms.DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == Forms.DialogResult.Yes)
                {
                    e.Cancel = true;
                    if (!saveOnClosePending)
                    {
                        saveOnClosePending = true;
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            bool saved = await SaveCanvasAsync();
                            saveOnClosePending = false;
                            if (saved)
                            {
                                allowClose = true;
                                Close();
                            }
                        }));
                    }
                    return;
                }
                allowClose = true;
            }
            base.OnClosing(e);
        }

        private async Task<Bitmap> CaptureCanvasAsync()
        {
            Wpf.Visibility previousVisibility = toolbar.Visibility;
            toolbar.Visibility = Wpf.Visibility.Collapsed;

            try
            {
                await Dispatcher.InvokeAsync(() => { }, WpfThreading.DispatcherPriority.Render);
                await Task.Delay(70);

                IntPtr handle = new WpfInterop.WindowInteropHelper(this).Handle;
                if (!GetWindowRect(handle, out NativeRect rect))
                {
                    throw new InvalidOperationException("无法读取幕布范围。");
                }

                int width = Math.Max(1, rect.Right - rect.Left);
                int height = Math.Max(1, rect.Bottom - rect.Top);
                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                return bitmap;
            }
            finally
            {
                toolbar.Visibility = previousVisibility;
            }
        }

        private void Window_PreviewKeyDown(object sender, WpfInput.KeyEventArgs e)
        {
            if (FindTextEditor(WpfInput.Keyboard.FocusedElement as Wpf.DependencyObject) != null && e.Key != WpfInput.Key.Escape)
            {
                return; // Preserve text editing shortcuts, including Enter and Ctrl+Z.
            }
            if (e.Key == WpfInput.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == WpfInput.Key.Z && (WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Control) != 0)
            {
                Undo();
                e.Handled = true;
            }
            else if (e.Key == WpfInput.Key.Y && (WpfInput.Keyboard.Modifiers & WpfInput.ModifierKeys.Control) != 0)
            {
                Redo();
                e.Handled = true;
            }
        }

        private static double Distance(Wpf.Point first, Wpf.Point second)
        {
            double x = second.X - first.X;
            double y = second.Y - first.Y;
            return Math.Sqrt(x * x + y * y);
        }

        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);
    }
}
