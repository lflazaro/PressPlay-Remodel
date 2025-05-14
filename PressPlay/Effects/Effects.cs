using System.Collections.ObjectModel;
using OpenCvSharp;
using System.Windows.Media;
using System.Linq;
using System.Diagnostics;

namespace PressPlay.Effects
{
    /// <summary>
    /// A simple interface for frame-by-frame effects.
    /// </summary>
    public interface IEffect
    {
        /// <summary>
        /// Display name of the effect.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Process one frame.
        /// </summary>
        /// <param name="inputFrame">Source BGR frame</param>
        /// <param name="outputFrame">Destination BGR frame (may be same as input)</param>
        void ProcessFrame(Mat inputFrame, Mat outputFrame);

        /// <summary>
        /// Editable parameters exposed to the UI.
        /// </summary>
        ObservableCollection<EffectParameter> Parameters { get; }
    }

    /// <summary>
    /// Represents a single effect parameter for data binding.
    /// </summary>
    public class EffectParameter
    {
        public string Name { get; }
        public object Value { get; set; }
        public double Minimum { get; }
        public double Maximum { get; }

        public EffectParameter(string name, object initialValue, double min = 0, double max = 1)
        {
            Name = name;
            Value = initialValue;
            Minimum = min;
            Maximum = max;
        }
    }

    /// <summary>
    /// Chroma-key effect: removes a target color and makes it transparent against a background.
    /// </summary>
    public class ChromaKeyEffect : IEffect
    {
        public string Name => "Chroma Key";

        // Underlying Mats for processing
        private Mat _hsvMat = new Mat();
        private Mat _mask = new Mat();

        // Default properties (backing fields)
        private Color _keyColor = Colors.Green;
        private double _tolerance = 0.3;

        /// <summary>
        /// The chroma key target color (in WPF Color space).
        /// </summary>
        public Color KeyColor
        {
            get => _keyColor;
            set
            {
                _keyColor = value;
                UpdateParameter("KeyColor", _keyColor);
            }
        }

        /// <summary>
        /// Tolerance around the key color (0–1).
        /// </summary> 
        public double Tolerance
        {
            get => _tolerance;
            set
            {
                _tolerance = value;
                UpdateParameter("Tolerance", _tolerance);
            }
        }

        public ObservableCollection<EffectParameter> Parameters { get; }

        public ChromaKeyEffect()
        {
            // Initialize parameter collection
            Parameters = new ObservableCollection<EffectParameter>
            {
                new EffectParameter("KeyColor", _keyColor),
                new EffectParameter("Tolerance", _tolerance, 0, 1)
            };
        }

        public void ProcessFrame(Mat inputFrame, Mat outputFrame)
        {
            Debug.WriteLine("ChromaKeyEffect.ProcessFrame called!");
            // Convert BGR to HSV on the input frame
            Cv2.CvtColor(inputFrame, _hsvMat, ColorConversionCodes.BGR2HSV);

            // Convert target WPF Color to HSV
            var target = System.Drawing.Color.FromArgb(_keyColor.A, _keyColor.R, _keyColor.G, _keyColor.B);
            using var colorMat = new Mat(1, 1, MatType.CV_8UC3, new Scalar(target.B, target.G, target.R));
            using var tmpHsv = new Mat();
            Cv2.CvtColor(colorMat, tmpHsv, ColorConversionCodes.BGR2HSV);
            Vec3b hsvTarget = tmpHsv.Get<Vec3b>(0, 0);

            // Compute tolerances in HSV space
            double tolH = _tolerance * 180;   // Hue range 0–180
            double tolS = _tolerance * 255;
            double tolV = _tolerance * 255;

            // Range lower/upper
            var lower = new Scalar(
                hsvTarget.Item0 - tolH,
                hsvTarget.Item1 - tolS,
                hsvTarget.Item2 - tolV);
            var upper = new Scalar(
                hsvTarget.Item0 + tolH,
                hsvTarget.Item1 + tolS,
                hsvTarget.Item2 + tolV);

            // Threshold to create mask of keyed pixels
            Cv2.InRange(_hsvMat, lower, upper, _mask);

            // Convert the input to BGRA to have an alpha channel
            using var bgraFrame = new Mat();
            Cv2.CvtColor(inputFrame, bgraFrame, ColorConversionCodes.BGR2BGRA);

            // Set alpha channel based on the mask (255 for opaque, 0 for transparent)
            for (int y = 0; y < bgraFrame.Rows; y++)
            {
                for (int x = 0; x < bgraFrame.Cols; x++)
                {
                    var pixel = bgraFrame.Get<Vec4b>(y, x);
                    // If mask is non-zero (key color), set alpha to 0
                    if (_mask.Get<byte>(y, x) != 0)
                        pixel[3] = 0;
                    else
                        pixel[3] = 255;
                    bgraFrame.Set(y, x, pixel);
                }
            }
            Debug.WriteLine($"Processing chroma key: Target HSV={hsvTarget}, Tolerance={_tolerance}");
            Debug.WriteLine($"Mask contains {Cv2.CountNonZero(_mask)} non-zero pixels out of {_mask.Rows * _mask.Cols}");
            // Copy the result to output
            bgraFrame.CopyTo(outputFrame);
        }

        // Helper to sync parameter collection on set
        private void UpdateParameter(string name, object value)
        {
            var param = Parameters.FirstOrDefault(p => p.Name == name);
            if (param != null)
                param.Value = value;
        }
    }
}
