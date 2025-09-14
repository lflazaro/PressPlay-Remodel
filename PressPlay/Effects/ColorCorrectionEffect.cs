using System;
using System.Collections.ObjectModel;
using System.Linq;
using OpenCvSharp;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using System.Windows.Media;

namespace PressPlay.Effects
{
    public class ColorCorrectionEffect : IEffect
    {
        public string Name => "Color Correction";

        private MediaColor _tintColor = Colors.White;
        private double _brightness = 0.0; // -100..100
        private double _contrast = 1.0;   // 0..3
        private double _gamma = 1.0;      // 0.1..5
        private double _saturation = 1.0; // 0..3

        public bool Enabled { get; set; } = true;

        public MediaColor TintColor
        {
            get => _tintColor;
            set
            {
                _tintColor = value;
                UpdateParameter("TintColor", _tintColor);
            }
        }

        public double Brightness
        {
            get => _brightness;
            set
            {
                _brightness = value;
                UpdateParameter("Brightness", _brightness);
            }
        }

        public double Contrast
        {
            get => _contrast;
            set
            {
                _contrast = value;
                UpdateParameter("Contrast", _contrast);
            }
        }

        public double Gamma
        {
            get => _gamma;
            set
            {
                _gamma = value;
                UpdateParameter("Gamma", _gamma);
            }
        }

        public double Saturation
        {
            get => _saturation;
            set
            {
                _saturation = value;
                UpdateParameter("Saturation", _saturation);
            }
        }

        public ObservableCollection<EffectParameter> Parameters { get; }

        public ColorCorrectionEffect()
        {
            Parameters = new ObservableCollection<EffectParameter>
            {
                new EffectParameter("TintColor", _tintColor),
                new EffectParameter("Brightness", _brightness, -100, 100),
                new EffectParameter("Contrast", _contrast, 0, 3),
                new EffectParameter("Gamma", _gamma, 0.1, 5),
                new EffectParameter("Saturation", _saturation, 0, 3)
            };
        }

        public void ProcessFrame(Mat inputFrame, Mat outputFrame)
        {
            if (!Enabled)
            {
                inputFrame.CopyTo(outputFrame);
                return;
            }

            inputFrame.CopyTo(outputFrame);

            // brightness and contrast
            outputFrame.ConvertTo(outputFrame, -1, _contrast, _brightness);

            // gamma correction
            if (Math.Abs(_gamma - 1.0) > 0.001)
            {
                byte[] lut = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    lut[i] = (byte)Math.Clamp(Math.Pow(i / 255.0, _gamma) * 255.0, 0, 255);
                }
                using var lutIA = InputArray.Create(lut);
                Cv2.LUT(outputFrame, lutIA, outputFrame);
            }

            // saturation adjustment
            if (Math.Abs(_saturation - 1.0) > 0.001)
            {
                using var hsv = new Mat();
                Cv2.CvtColor(outputFrame, hsv, ColorConversionCodes.BGR2HSV);
                var channels = Cv2.Split(hsv);
                channels[1].ConvertTo(channels[1], channels[1].Type(), _saturation, 0);
                using (var maxMat = new Mat(channels[1].Size(), channels[1].Type(), new Scalar(255)))
                {
                    Cv2.Min(channels[1], maxMat, channels[1]);
                }
                Cv2.Merge(channels, hsv);
                Cv2.CvtColor(hsv, outputFrame, ColorConversionCodes.HSV2BGR);
                foreach (var ch in channels)
                    ch.Dispose();
            }

            // color tint using saturation as strength. Instead of simply
            // cross-fading to a solid color (which results in a flat overlay),
            // multiply the frame with the tint color and blend the result. This
            // preserves the luminance details of the original frame while
            // shifting the hue toward the tint color.
            var drawingColor = DrawingColor.FromArgb(_tintColor.A, _tintColor.R, _tintColor.G, _tintColor.B);
            double tintSat = drawingColor.GetSaturation();
            if (tintSat > 0.001)
            {
                using var colorMat = new Mat(outputFrame.Size(), outputFrame.Type(), new Scalar(_tintColor.B, _tintColor.G, _tintColor.R));
                using var tinted = new Mat();
                Cv2.Multiply(outputFrame, colorMat, tinted, 1.0 / 255.0);
                Cv2.AddWeighted(outputFrame, 1.0 - tintSat, tinted, tintSat, 0, outputFrame);
            }
        }

        private void UpdateParameter(string name, object value)
        {
            var param = Parameters.FirstOrDefault(p => p.Name == name);
            if (param != null)
                param.Value = value;
        }
    }
}
