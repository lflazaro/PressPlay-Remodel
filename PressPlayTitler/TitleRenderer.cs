using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
// SVG support via SharpVectors
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using Newtonsoft.Json;

namespace PressPlayTitler
{
    public abstract class TitleElement : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private double _opacity = 1.0;
        public double Opacity
        {
            get => _opacity;
            set => SetField(ref _opacity, value);
        }

        private double _rotation = 0.0;
        public double Rotation
        {
            get => _rotation;
            set => SetField(ref _rotation, value);
        }

        public abstract Rect Bounds { get; }
        public abstract void Draw(DrawingContext dc, int canvasWidth, int canvasHeight);

        protected bool SetField<T>(ref T field, T val, [CallerMemberName] string? propName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, val)) return false;
            field = val;
            OnPropertyChanged(propName);
            if (propName != nameof(Bounds))
                OnPropertyChanged(nameof(Bounds));
            return true;
        }
        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }
    }

    public class TextElement : TitleElement
    {
        private string _text = "Text";
        public string Text { get => _text; set => SetField(ref _text, value); }

        private string _fontFamily = "Segoe UI";
        public string FontFamily { get => _fontFamily; set => SetField(ref _fontFamily, value); }

        private double _fontSize = 48;
        public double FontSize { get => _fontSize; set => SetField(ref _fontSize, value); }

        private Color _fontColor = Colors.White;
        public Color FontColor { get => _fontColor; set => SetField(ref _fontColor, value); }

        private TextAlignment _alignment = TextAlignment.Left;
        public TextAlignment Alignment { get => _alignment; set => SetField(ref _alignment, value); }

        private Point _position = new Point(0, 0);
        public Point Position { get => _position; set => SetField(ref _position, value); }

        private bool _dropShadow = false;
        public bool DropShadow { get => _dropShadow; set => SetField(ref _dropShadow, value); }

        private Color _shadowColor = Colors.Black;
        public Color ShadowColor { get => _shadowColor; set => SetField(ref _shadowColor, value); }

        private Vector _shadowOffset = new Vector(2, 2);
        public Vector ShadowOffset { get => _shadowOffset; set => SetField(ref _shadowOffset, value); }

        public override Rect Bounds
        {
            get
            {
                var ft = new FormattedText(
                    Text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(FontFamily),
                    FontSize,
                    Brushes.Black,
                    96);
                double w = ft.Width, h = ft.Height;
                double x = Position.X, y = Position.Y;
                if (Alignment == TextAlignment.Center) x -= w / 2;
                else if (Alignment == TextAlignment.Right) x -= w;
                return new Rect(x, y, w, h);
            }
        }

        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            var typeface = new Typeface(FontFamily);
            var ft = new FormattedText(
                Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                new SolidColorBrush(FontColor) { Opacity = Opacity },
                96
            )
            { TextAlignment = Alignment };

            // compute X based on alignment
            double drawX = Position.X;
            if (Alignment == TextAlignment.Center) drawX -= ft.Width / 2;
            else if (Alignment == TextAlignment.Right) drawX -= ft.Width;

            // Y is always the baseline starting point
            double drawY = Position.Y;

            dc.DrawText(ft, new Point(drawX, drawY));
        }

    }

    public class RectangleElement : TitleElement
    {
        private Rect _rect = new Rect(0, 0, 100, 100);
        public Rect Rect { get => _rect; set => SetField(ref _rect, value); }
        public override Rect Bounds => Rect;
        public Color FillColor { get; set; } = Colors.Transparent;
        public Color? StrokeColor { get; set; }
        public double StrokeThickness { get; set; } = 1;
        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            var fill = new SolidColorBrush(FillColor) { Opacity = Opacity };
            dc.DrawRectangle(fill, null, Rect);
            if (StrokeColor.HasValue)
            {
                var pen = new Pen(new SolidColorBrush(StrokeColor.Value) { Opacity = Opacity }, StrokeThickness);
                dc.DrawRectangle(null, pen, Rect);
            }
        }
    }

    public class EllipseElement : TitleElement
    {
        private Point _center = new Point(50, 50);
        public Point Center { get => _center; set => SetField(ref _center, value); }
        private double _radiusX = 50;
        public double RadiusX { get => _radiusX; set => SetField(ref _radiusX, value); }
        private double _radiusY = 50;
        public double RadiusY { get => _radiusY; set => SetField(ref _radiusY, value); }
        public override Rect Bounds => new Rect(
            Center.X - RadiusX,
            Center.Y - RadiusY,
            RadiusX * 2,
            RadiusY * 2);
        public Color FillColor { get; set; } = Colors.Transparent;
        public Color? StrokeColor { get; set; }
        public double StrokeThickness { get; set; } = 1;
        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            var fill = new SolidColorBrush(FillColor) { Opacity = Opacity };
            dc.DrawEllipse(fill, null, Center, RadiusX, RadiusY);
            if (StrokeColor.HasValue)
            {
                var pen = new Pen(new SolidColorBrush(StrokeColor.Value) { Opacity = Opacity }, StrokeThickness);
                dc.DrawEllipse(null, pen, Center, RadiusX, RadiusY);
            }
        }
    }

    public class LineElement : TitleElement
    {
        private Point _start = new Point(0, 0);
        public Point Start { get => _start; set => SetField(ref _start, value); }

        // ← change this from (100,100) to (100,0)
        private Point _end = new Point(100, 0);
        public Point End { get => _end; set => SetField(ref _end, value); }

        public override Rect Bounds
        {
            get
            {
                double x = Math.Min(Start.X, End.X);
                double y = Math.Min(Start.Y, End.Y);
                double w = Math.Abs(End.X - Start.X);
                double h = Math.Abs(End.Y - Start.Y);
                return new Rect(x, y, w, h);
            }
        }

        public Color StrokeColor { get; set; } = Colors.White;
        public double StrokeThickness { get; set; } = 5;

        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            var pen = new Pen(new SolidColorBrush(StrokeColor) { Opacity = Opacity }, StrokeThickness);
            // always draw from Start to a point directly to the right of it
            dc.DrawLine(pen, Start, new Point(End.X, Start.Y));
        }
    }



    public class GradientRectangleElement : TitleElement
    {
        // 1) The real WPF collection—ignored by JSON.NET
        [JsonIgnore]
        public GradientStopCollection GradientStops { get; set; }
            = new GradientStopCollection
            {
            new GradientStop(Colors.Red, 0.0),
            new GradientStop(Colors.Yellow, 1.0)
            };

        // 2) A private DTO type that only holds the data we care about
        public class SerializableStop
        {
            public Color Color { get; set; }
            public double Offset { get; set; }
        }

        // 3) The JSON‐facing property
        [JsonProperty(nameof(GradientStops))]
        private List<SerializableStop> GradientStopsData
        {
            get
            {
                // serialize each GradientStop into our DTO
                return GradientStops
                    .Select(gs => new SerializableStop
                    {
                        Color = gs.Color,
                        Offset = gs.Offset
                    })
                    .ToList();
            }
            set
            {
                // rehydrate a fresh WPF collection from the DTOs
                GradientStops = new GradientStopCollection(
                    value.Select(dto => new GradientStop(dto.Color, dto.Offset))
                );
            }
        }

        // your other properties...
        private Rect _rect = new Rect(0, 0, 100, 100);
        public Rect Rect { get => _rect; set => SetField(ref _rect, value); }
        public override Rect Bounds => Rect;

        public Point StartPoint { get; set; } = new Point(0, 0);
        public Point EndPoint { get; set; } = new Point(1, 1);

        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            var brush = new LinearGradientBrush(GradientStops, StartPoint, EndPoint)
            {
                Opacity = Opacity
            };
            dc.DrawRectangle(brush, null, Rect);
        }
    }

    public class ImageElement : TitleElement
    {
        private Rect _rect = new Rect(0, 0, 100, 100);
        public Rect Rect { get => _rect; set => SetField(ref _rect, value); }
        public override Rect Bounds => Rect;
        public string ImagePath { get; set; } = string.Empty;
        private BitmapSource? _bitmap;
        private void EnsureLoaded()
        {
            if (_bitmap != null || string.IsNullOrEmpty(ImagePath)) return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(ImagePath, UriKind.RelativeOrAbsolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _bitmap = bmp;
        }
        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            EnsureLoaded();
            if (_bitmap != null)
                dc.DrawImage(_bitmap, Rect);
        }
    }

    public class SvgElement : TitleElement
    {
        private Rect _rect = new Rect(0, 0, 100, 100);
        public Rect Rect { get => _rect; set => SetField(ref _rect, value); }
        public override Rect Bounds => Rect;
        public string SvgPath { get; set; } = string.Empty;
        private DrawingGroup? _drawing;
        private void EnsureLoaded()
        {
            if (_drawing != null || string.IsNullOrEmpty(SvgPath)) return;
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true };
            var reader = new FileSvgReader(settings);
            _drawing = reader.Read(SvgPath);
        }
        public override void Draw(DrawingContext dc, int canvasWidth, int canvasHeight)
        {
            EnsureLoaded();
            if (_drawing == null) return;
            var b = _drawing.Bounds;
            if (b.Width <= 0 || b.Height <= 0)
            {
                dc.DrawDrawing(_drawing);
                return;
            }
            double sx = Rect.Width / b.Width;
            double sy = Rect.Height / b.Height;
            dc.PushTransform(new TranslateTransform(Rect.X - b.X * sx,
                                                   Rect.Y - b.Y * sy));
            dc.PushTransform(new ScaleTransform(sx, sy));
            dc.DrawDrawing(_drawing);
            dc.Pop(); dc.Pop();
        }
    }

    public class TitleComposition
    {
        public int Width { get; set; } = 1448;
        public int Height { get; set; } = 995;
        public Color? BackgroundColor { get; set; } = null;
        public List<TitleElement> Elements { get; set; } = new();
    }

    public static class TitleRenderer
    {
        public static RenderTargetBitmap RenderComposition(TitleComposition comp)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (comp.BackgroundColor.HasValue)
                    dc.DrawRectangle(new SolidColorBrush(comp.BackgroundColor.Value),
                                     null,
                                     new Rect(0, 0, comp.Width, comp.Height));
                foreach (var elem in comp.Elements)
                {
                    var center = elem.Bounds.Location + new Vector(elem.Bounds.Width / 2, elem.Bounds.Height / 2);
                    dc.PushTransform(new RotateTransform(elem.Rotation, center.X, center.Y));
                    elem.Draw(dc, comp.Width, comp.Height);
                    dc.Pop();
                }
            }
            var bmp = new RenderTargetBitmap(comp.Width, comp.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            return bmp;
        }

        public static void SavePng(RenderTargetBitmap bitmap, string outputPath)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fs = new FileStream(outputPath, FileMode.Create);
            encoder.Save(fs);
        }
    }
}
