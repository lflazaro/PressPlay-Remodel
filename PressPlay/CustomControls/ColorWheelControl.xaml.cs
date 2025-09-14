using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PressPlay.CustomControls
{
    public partial class ColorWheelControl : UserControl
    {
        public static readonly DependencyProperty HueProperty =
            DependencyProperty.Register(nameof(Hue), typeof(double), typeof(ColorWheelControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHueChanged));

        public static readonly DependencyProperty SaturationProperty =
            DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(ColorWheelControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSaturationChanged));

        public double Hue
        {
            get => (double)GetValue(HueProperty);
            set => SetValue(HueProperty, value);
        }

        public double Saturation
        {
            get => (double)GetValue(SaturationProperty);
            set => SetValue(SaturationProperty, value);
        }

        private bool _dragging;

        public ColorWheelControl()
        {
            InitializeComponent();
            Loaded += (_, _) => RenderWheel();
            SizeChanged += (_, _) => RenderWheel();
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += (_, _) => { _dragging = false; ReleaseMouseCapture(); };
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            CaptureMouse();
            UpdateFromPoint(e.GetPosition(this));
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
                UpdateFromPoint(e.GetPosition(this));
        }

        private void UpdateFromPoint(Point p)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            double cx = size / 2;
            double cy = size / 2;
            double dx = p.X - cx;
            double dy = p.Y - cy;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            double maxRadius = size / 2;
            Saturation = Math.Min(1.0, radius / maxRadius);
            Hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            UpdateSelector();
        }

        private void RenderWheel()
        {
            int size = (int)Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            int stride = size * 4;
            byte[] pixels = new byte[size * stride];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x - size / 2.0;
                    double dy = y - size / 2.0;
                    double radius = Math.Sqrt(dx * dx + dy * dy);
                    if (radius > size / 2.0)
                        continue;
                    double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                    double sat = radius / (size / 2.0);
                    var c = ColorFromHSV(hue, sat, 1.0);
                    int index = y * stride + x * 4;
                    pixels[index] = c.B;
                    pixels[index + 1] = c.G;
                    pixels[index + 2] = c.R;
                    pixels[index + 3] = 255;
                }
            }
            var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, stride, 0);
            WheelImage.Width = size;
            WheelImage.Height = size;
            WheelImage.Source = wb;
            UpdateSelector();
        }

        private static Color ColorFromHSV(double hue, double saturation, double value)
        {
            double c = value * saturation;
            double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
            double m = value - c;
            double r = 0, g = 0, b = 0;
            if (hue < 60) { r = c; g = x; }
            else if (hue < 120) { r = x; g = c; }
            else if (hue < 180) { g = c; b = x; }
            else if (hue < 240) { g = x; b = c; }
            else if (hue < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private void UpdateSelector()
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            double radius = Saturation * size / 2;
            double rad = Hue * Math.PI / 180;
            double x = size / 2 + radius * Math.Cos(rad);
            double y = size / 2 + radius * Math.Sin(rad);
            Canvas.SetLeft(Selector, x - Selector.Width / 2);
            Canvas.SetTop(Selector, y - Selector.Height / 2);
        }

        private static void OnHueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ColorWheelControl)d).UpdateSelector();
        }

        private static void OnSaturationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ColorWheelControl)d).UpdateSelector();
        }
    }
}
