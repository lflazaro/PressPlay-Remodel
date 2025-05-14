using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;             // JSON serialization for undo/redo & open/save
using System.Windows.Shapes;
using System.Windows.Forms;                   // for PropertyGrid
using System.Windows.Forms.Integration;       // for WindowsFormsHost
using Microsoft.Win32;                        // for OpenFileDialog / SaveFileDialog
using PressPlayTitler;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.IO;

namespace PressPlayTitler
{
    public partial class MainWindow : Window
    {
        private readonly TitleComposition _composition;
        private readonly ObservableCollection<TitleElement> _elements;
        private readonly Rectangle _selectionRect;
        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private readonly TitlerLauncher.TitleExportedCallback? _exportCallback;

        // Drag state
        private Point _moveStartMouse;
        private Point _moveStartPos;
        private Rect _moveStartRect;

        private Point _resizeStartMouse;
        private Rect _resizeStartRect;

        private Point _rotateCenter;
        private double _rotateStartRotation;
        private double _rotateStartMouseAngle;

        public MainWindow(TitlerLauncher.TitleExportedCallback? exportCallback = null)
        {
            InitializeComponent();
            DataContext = this;
            _exportCallback = exportCallback;

            _composition = new TitleComposition { Width = 1448, Height = 995 };
            _elements = new ObservableCollection<TitleElement>();
            ElementsControl.ItemsSource = _elements;
            ElementsCanvas.ItemsSource = _elements;

            _selectionRect = new Rectangle
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = Brushes.Transparent,
                Visibility = Visibility.Collapsed
            };
            DesignCanvas.Children.Add(_selectionRect);

            CaptureState();
        }
        private void CaptureState()
        {
            // push a JSON snapshot of the elements
            var json = JsonConvert.SerializeObject(
                _elements,
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }
            );
            _undoStack.Push(json);
            _redoStack.Clear();
        }
        private void RestoreState(string json)
        {
            var list = JsonConvert.DeserializeObject<ObservableCollection<TitleElement>>(
                json,
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }
            )!;
            _elements.Clear();
            foreach (var e in list) _elements.Add(e);
            ElementsControl.Items.Refresh();
            HighlightSelection(ElementsControl.SelectedItem as TitleElement);
        }
        // Add this method to the MainWindow class to respond to TimeLine selection changes
        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count < 2) return;   // keep at least one state
            _redoStack.Push(_undoStack.Pop());
            RestoreState(_undoStack.Peek());
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count == 0) return;
            var json = _redoStack.Pop();
            _undoStack.Push(json);
            RestoreState(json);
        }
        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Title JSON|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = File.ReadAllText(dlg.FileName);
                RestoreState(json);
                _undoStack.Clear();
                _redoStack.Clear();
                _undoStack.Push(json);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "Title JSON|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = JsonConvert.SerializeObject(
                    _elements,
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All }
                );
                File.WriteAllText(dlg.FileName, json);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }


        // Add elements
        private void AddText_Click(object s, RoutedEventArgs e)
        {
            var txt = new TextElement { Text = "New Text", Position = new Point(10, 10) };
            txt.Name = $"Text {_elements.Count + 1}";
            _elements.Add(txt);
            ElementsControl.SelectedItem = txt;
            CaptureState();
        }
        private void AddRectangle_Click(object s, RoutedEventArgs e)
        {
            var r = new RectangleElement { Rect = new Rect(10, 10, 100, 50), FillColor = Colors.Gray };
            r.Name = $"Rectangle {_elements.Count + 1}";
            _elements.Add(r);
            ElementsControl.SelectedItem = r;
            CaptureState();
        }
        private void AddEllipse_Click(object s, RoutedEventArgs e)
        {
            var el = new EllipseElement { Center = new Point(100, 100), RadiusX = 50, RadiusY = 25, FillColor = Colors.Blue };
            el.Name = $"Ellipse {_elements.Count + 1}";
            _elements.Add(el);
            ElementsControl.SelectedItem = el;
            CaptureState();
        }
        private void AddLine_Click(object s, RoutedEventArgs e)
        {
            var line = new LineElement
            {
                Start = new Point(10, 10),
                End = new Point(110, 10),  // ← 100px to the right, same Y
                StrokeColor = Colors.White,
                StrokeThickness = 2,
                Name = $"Line {_elements.Count + 1}"
            };
            _elements.Add(line);
            ElementsControl.SelectedItem = line;
            CaptureState();
        }
        private void AddGradient_Click(object s, RoutedEventArgs e)
        {
            var g = new GradientRectangleElement
            {
                Rect = new Rect(0, 0, 200, 100),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Colors.Red,   0.0),
                    new GradientStop(Colors.Yellow,1.0)
                }
            };
            g.Name = $"Gradient {_elements.Count + 1}";
            _elements.Add(g);
            ElementsControl.SelectedItem = g;
            CaptureState();
        }
        private void AddImage_Click(object s, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.bmp" };
            if (dlg.ShowDialog() == true)
            {
                var img = new ImageElement { ImagePath = dlg.FileName, Rect = new Rect(0, 0, 200, 200) };
                img.Name = $"Image {_elements.Count + 1}";
                _elements.Add(img);
                ElementsControl.SelectedItem = img;
            }
            CaptureState();
        }
        private void AddSvg_Click(object s, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "SVG Files|*.svg" };
            if (dlg.ShowDialog() == true)
            {
                var svg = new SvgElement { SvgPath = dlg.FileName, Rect = new Rect(0, 0, 200, 200) };
                svg.Name = $"SVG {_elements.Count + 1}";
                _elements.Add(svg);
                ElementsControl.SelectedItem = svg;
            }
            CaptureState();
        }
        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (ElementsControl.SelectedItem is TitleElement el)
            {
                var i = _elements.IndexOf(el);
                if (i > 0)
                {
                    _elements.Move(i, i - 1);
                    ElementsControl.SelectedItem = el;
                }
            }
            CaptureState();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (ElementsControl.SelectedItem is TitleElement el)
            {
                var i = _elements.IndexOf(el);
                if (i >= 0 && i < _elements.Count - 1)
                {
                    _elements.Move(i, i + 1);
                    ElementsControl.SelectedItem = el;
                }
            }
            CaptureState();
        }

        // Remove & Select
        private void Remove_Click(object s, RoutedEventArgs e)
        {
            if (ElementsControl.SelectedItem is TitleElement el)
            {
                _elements.Remove(el);
                WpfPropertyGrid.SelectedObject = null;
                HighlightSelection(null);
            }
            CaptureState();
        }
        private void ElementsControl_SelectionChanged(object sender, EventArgs e)
        {
            var el = ElementsControl.SelectedItem;
            // this updates the WPF PropertyGrid
            WpfPropertyGrid.SelectedObject = el;
            HighlightSelection(el as TitleElement);
        }
        private void PropertyGrid_PropertyValueChanged(object s, System.Windows.Forms.PropertyValueChangedEventArgs e)
        {
            HighlightSelection(ElementsControl.SelectedItem as TitleElement);
        }

        // Move
        private void Move_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (!(sender is Thumb tb && tb.DataContext is TitleElement el)) return;
            _moveStartMouse = Mouse.GetPosition(DesignCanvas);
            if (el is TextElement te)
                _moveStartPos = te.Position;
            else
                _moveStartRect = el.Bounds;
        }
        private void Element_Move_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!(sender is Thumb tb && tb.DataContext is TitleElement el))
                return;

            double dx = e.HorizontalChange;
            double dy = e.VerticalChange;

            switch (el)
            {
                case TextElement te:
                    te.Position = new Point(te.Position.X + dx,
                                            te.Position.Y + dy);
                    break;

                case RectangleElement re:
                    re.Rect = new Rect(
                        re.Rect.X + dx,
                        re.Rect.Y + dy,
                        re.Rect.Width,
                        re.Rect.Height);
                    break;

                case EllipseElement ee:
                    ee.Center = new Point(
                        ee.Center.X + dx,
                        ee.Center.Y + dy);
                    break;

                case LineElement le:
                    le.Start = new Point(
                        le.Start.X + dx,
                        le.Start.Y + dy);
                    le.End = new Point(
                        le.End.X + dx,
                        le.End.Y + dy);
                    break;

                case ImageElement ie:
                    ie.Rect = new Rect(
                        ie.Rect.X + dx,
                        ie.Rect.Y + dy,
                        ie.Rect.Width,
                        ie.Rect.Height);
                    break;

                case SvgElement se:
                    se.Rect = new Rect(
                        se.Rect.X + dx,
                        se.Rect.Y + dy,
                        se.Rect.Width,
                        se.Rect.Height);
                    break;

                case GradientRectangleElement ge:
                    ge.Rect = new Rect(
                        ge.Rect.X + dx,
                        ge.Rect.Y + dy,
                        ge.Rect.Width,
                        ge.Rect.Height);
                    break;
            }

            HighlightSelection(el);
        }


        // ───── Resize: DragStarted + DragDelta ─────
        private void Resize_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (!(sender is Thumb tb && tb.DataContext is TitleElement el)) return;
            // Record where the mouse and element bounds were at the start
            _resizeStartMouse = Mouse.GetPosition(DesignCanvas);
            _resizeStartRect = el.Bounds;
        }

        private void Element_Resize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!(sender is Thumb tb && tb.DataContext is TitleElement el)) return;

            // Compute total drag offset
            var now = Mouse.GetPosition(DesignCanvas);
            var dx = now.X - _resizeStartMouse.X;
            var dy = now.Y - _resizeStartMouse.Y;
            var r = _resizeStartRect;

            // New size = original size + offset
            double newW = Math.Max(1, r.Width + dx);
            double newH = Math.Max(1, r.Height + dy);

            switch (el)
            {
                case RectangleElement re:
                    // keep the same origin, just width/height
                    re.Rect = new Rect(r.X, r.Y, newW, newH);
                    break;

                case EllipseElement ee:
                    // recompute center from new size
                    ee.RadiusX = newW / 2;
                    ee.RadiusY = newH / 2;
                    ee.Center = new Point(r.X + newW / 2, r.Y + newH / 2);
                    break;

                case LineElement le:
                    // stretch line end instead of recreating Bounds
                    le.Start = new Point(r.X, r.Y);
                    le.End = new Point(r.X + newW, r.Y + newH);
                    break;

                case ImageElement ie:
                    ie.Rect = new Rect(r.X, r.Y, newW, newH);
                    break;

                case SvgElement se:
                    se.Rect = new Rect(r.X, r.Y, newW, newH);
                    break;

                case GradientRectangleElement ge:
                    ge.Rect = new Rect(r.X, r.Y, newW, newH);
                    break;
            }

            HighlightSelection(el);
        }


        // ───── Rotate: DragStarted + DragDelta ─────
        private void Rotate_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (!(sender is Thumb tb && tb.DataContext is TitleElement el)) return;

            // Record element center and starting rotation
            _rotateCenter = GetElementCenter(el);
            _rotateStartRotation = el.Rotation;

            // Record starting mouse angle relative to center
            var m = Mouse.GetPosition(DesignCanvas);
            var vx = m.X - _rotateCenter.X;
            var vy = m.Y - _rotateCenter.Y;
            _rotateStartMouseAngle = Math.Atan2(vy, vx) * 180 / Math.PI;
        }

        private void Element_Rotate_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb tb && tb.DataContext is TitleElement el)
            {
                // compute delta‐angle just once:
                var mouse = Mouse.GetPosition(DesignCanvas);
                var center = GetElementCenter(el);
                var vx = mouse.X - center.X;
                var vy = mouse.Y - center.Y;
                var angle = Math.Atan2(vy, vx) * 180 / Math.PI;
                el.Rotation = _rotateStartRotation + (angle - _rotateStartMouseAngle);
            }
        }

        private void ElementControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TitleElement el)
            {
                ElementsControl.SelectedItem = el;             // update ListBox
                WpfPropertyGrid.SelectedObject = el;             // update property grid
                HighlightSelection(el);                          // move the yellow box
                e.Handled = true;           // don’t let the event fall through
            }
        }

        // Helpers
        private Point GetElementCenter(TitleElement el) => el switch
        {
            TextElement te => te.Position,
            RectangleElement re => new Point(re.Rect.X + re.Rect.Width / 2, re.Rect.Y + re.Rect.Height / 2),
            EllipseElement ee => ee.Center,
            LineElement le => new Point((le.Start.X + le.End.X) / 2, (le.Start.Y + le.End.Y) / 2),
            ImageElement ie => new Point(ie.Rect.X + ie.Rect.Width / 2, ie.Rect.Y + ie.Rect.Height / 2),
            SvgElement se => new Point(se.Rect.X + se.Rect.Width / 2, se.Rect.Y + se.Rect.Height / 2),
            GradientRectangleElement ge => new Point(ge.Rect.X + ge.Rect.Width / 2, ge.Rect.Y + ge.Rect.Height / 2),
            _ => new Point(0, 0)
        };

        private void HighlightSelection(TitleElement el)
        {
            if (el == null)
            {
                _selectionRect.Visibility = Visibility.Collapsed;
                return;
            }
            var b = el.Bounds;
            _selectionRect.Width = b.Width;
            _selectionRect.Height = b.Height;
            Canvas.SetLeft(_selectionRect, b.X);
            Canvas.SetTop(_selectionRect, b.Y);
            _selectionRect.Visibility = Visibility.Visible;
        }

        private void Export_Click(object s, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "composition.png" };
            if (dlg.ShowDialog() == true)
            {
                _composition.Elements = _elements.ToList();
                var bmp = TitleRenderer.RenderComposition(_composition);
                TitleRenderer.SavePng(bmp, dlg.FileName);

                // Call the callback if provided
                _exportCallback?.Invoke(dlg.FileName);

                MessageBox.Show($"Exported to {dlg.FileName}", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void Element_Move_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            CaptureState();
        }

        // called once, after you finish dragging to resize
        private void Element_Resize_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            CaptureState();
        }

        // called once, after you finish dragging to rotate
        private void Element_Rotate_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            CaptureState();
        }
        private void CanvasElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ContentPresenter cp && cp.DataContext is TitleElement el)
            {
                ElementsControl.SelectedItem = el;
                WpfPropertyGrid.SelectedObject = el;
                HighlightSelection(el);
            }
        }
        public static class TitlerLauncher
        {
            // Add a delegate type for the callback
            public delegate void TitleExportedCallback(string filePath);

            public static void ShowTitler(Window? owner = null, TitleExportedCallback? exportCallback = null)
            {
                var win = new MainWindow(exportCallback);
                if (owner != null)
                    win.Owner = owner;

                win.ShowDialog(); // or Show() if you want modeless
            }
        }
        
    }
}
