using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PressPlay.CustomControls
{
    public partial class ColorWheel : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(
                nameof(SelectedColor),
                typeof(Color),
                typeof(ColorWheel),
                new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        private int _size = 200;
        private Point _center;

        public ColorWheel()
        {
            InitializeComponent();
            Loaded += ColorWheel_Loaded;
            WheelImage.MouseDown += WheelImage_MouseDown;
            WheelImage.MouseMove += WheelImage_MouseMove;
        }

        private void ColorWheel_Loaded(object sender, RoutedEventArgs e)
        {
            _center = new Point(_size / 2.0, _size / 2.0);
            GenerateWheel();
        }

        private void GenerateWheel()
        {
            int stride = _size * 4;
            byte[] pixels = new byte[stride * _size];
            for (int y = 0; y < _size; y++)
            {
                for (int x = 0; x < _size; x++)
                {
                    double dx = x - _center.X;
                    double dy = y - _center.Y;
                    double r = Math.Sqrt(dx * dx + dy * dy) / (_size / 2.0);
                    int index = y * stride + x * 4;
                    if (r <= 1)
                    {
                        double angle = Math.Atan2(dy, dx);
                        double hue = (angle * 180 / Math.PI + 360) % 360;
                        Color c = HsvToColor(hue, r, 1);
                        pixels[index] = c.B;
                        pixels[index + 1] = c.G;
                        pixels[index + 2] = c.R;
                        pixels[index + 3] = 255;
                    }
                    else
                    {
                        pixels[index + 3] = 0;
                    }
                }
            }
            var bmp = BitmapSource.Create(_size, _size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            WheelImage.Source = bmp;
        }

        private void WheelImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(WheelImage);
            PickColor(pos);
        }

        private void WheelImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(WheelImage);
                PickColor(pos);
            }
        }

        private void PickColor(Point pos)
        {
            double dx = pos.X - _center.X;
            double dy = pos.Y - _center.Y;
            double r = Math.Sqrt(dx * dx + dy * dy) / (_size / 2.0);
            if (r > 1) return;
            double angle = Math.Atan2(dy, dx);
            double hue = (angle * 180 / Math.PI + 360) % 360;
            SelectedColor = HsvToColor(hue, r, 1);
            Canvas.SetLeft(Selector, pos.X - Selector.Width / 2);
            Canvas.SetTop(Selector, pos.Y - Selector.Height / 2);
            Selector.Visibility = Visibility.Visible;
        }

        private Color HsvToColor(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r1 = 0, g1 = 0, b1 = 0;
            if (h < 60) { r1 = c; g1 = x; }
            else if (h < 120) { r1 = x; g1 = c; }
            else if (h < 180) { g1 = c; b1 = x; }
            else if (h < 240) { g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; b1 = c; }
            else { r1 = c; b1 = x; }
            byte r = (byte)Math.Round((r1 + m) * 255);
            byte g = (byte)Math.Round((g1 + m) * 255);
            byte b = (byte)Math.Round((b1 + m) * 255);
            return Color.FromRgb(r, g, b);
        }
    }
}
