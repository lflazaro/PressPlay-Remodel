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

        public ObservableCollection<EffectParameter> Parameters { get; }

        public ColorCorrectionEffect()
        {
            Parameters = new ObservableCollection<EffectParameter>
            {
                new EffectParameter("TintColor", _tintColor),
                new EffectParameter("Brightness", _brightness, -100, 100),
                new EffectParameter("Contrast", _contrast, 0, 3),
                new EffectParameter("Gamma", _gamma, 0.1, 5)
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
                double invGamma = 1.0 / _gamma;
                for (int i = 0; i < 256; i++)
                {
                    lut[i] = (byte)Math.Clamp(Math.Pow(i / 255.0, invGamma) * 255.0, 0, 255);
                }
                using var lutIA = InputArray.Create(lut);
                Cv2.LUT(outputFrame, lutIA, outputFrame);
            }

            // color tint using saturation as strength
            var drawingColor = DrawingColor.FromArgb(_tintColor.A, _tintColor.R, _tintColor.G, _tintColor.B);
            double sat = drawingColor.GetSaturation();
            if (sat > 0.001)
            {
                using var overlay = new Mat(outputFrame.Size(), outputFrame.Type(), new Scalar(_tintColor.B, _tintColor.G, _tintColor.R));
                Cv2.AddWeighted(outputFrame, 1.0 - sat, overlay, sat, 0, outputFrame);
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
