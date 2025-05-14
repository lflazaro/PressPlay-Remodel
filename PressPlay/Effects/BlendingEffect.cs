using OpenCvSharp;
using PressPlay.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace PressPlay.Effects
{
    /// <summary>
    /// Blend modes available for compositing
    /// </summary>
    public enum BlendMode
    {
        Normal,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion,
    }

    /// <summary>
    /// Effect that applies different blend modes between layers
    /// </summary>
    public class BlendingEffect : IEffect
    {
        public string Name => "Blending";
        public bool Enabled { get; set; } = true;

        private BlendMode _blendMode = BlendMode.Normal;
        public BlendMode BlendMode
        {
            get => _blendMode;
            set
            {
                if (_blendMode != value)
                {
                    _blendMode = value;
                    UpdateParameters();
                }
            }
        }

        private readonly TrackItem _item;

        // Parameters that will be exposed to the UI
        public ObservableCollection<EffectParameter> Parameters { get; }
            = new ObservableCollection<EffectParameter>();

        public BlendingEffect(TrackItem item)
        {
            _item = item;
            InitializeParameters();
        }

        private void InitializeParameters()
        {
            // Create a parameter for blend mode selection
            var blendModeParam = new EffectParameter("Blend Mode", _blendMode.ToString());
            Parameters.Add(blendModeParam);
        }

        private void UpdateParameters()
        {
            // Update the blend mode parameter when it changes
            var param = Parameters.FirstOrDefault(p => p.Name == "Blend Mode");
            if (param != null)
            {
                param.Value = _blendMode.ToString();
            }
        }

        public void ProcessFrame(Mat baseLayer, Mat output)
        {
            // If not enabled or normal blend, just copy the input
            if (!Enabled || _blendMode == BlendMode.Normal)
            {
                baseLayer.CopyTo(output);
                return;
            }

            try
            {
                // Apply the blend mode
                switch (_blendMode)
                {
                    case BlendMode.Multiply:
                        ApplyMultiply(baseLayer, output);
                        break;
                    case BlendMode.Screen:
                        ApplyScreen(baseLayer, output);
                        break;
                    case BlendMode.Overlay:
                        ApplyOverlay(baseLayer, output);
                        break;
                    case BlendMode.Darken:
                        ApplyDarken(baseLayer, output);
                        break;
                    case BlendMode.Lighten:
                        ApplyLighten(baseLayer, output);
                        break;
                    case BlendMode.ColorDodge:
                        ApplyColorDodge(baseLayer, output);
                        break;
                    case BlendMode.ColorBurn:
                        ApplyColorBurn(baseLayer, output);
                        break;
                    case BlendMode.HardLight:
                        ApplyHardLight(baseLayer, output);
                        break;
                    case BlendMode.SoftLight:
                        ApplySoftLight(baseLayer, output);
                        break;
                    case BlendMode.Difference:
                        ApplyDifference(baseLayer, output);
                        break;
                    case BlendMode.Exclusion:
                        ApplyExclusion(baseLayer, output);
                        break;
                    default:
                        baseLayer.CopyTo(output);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying blend mode {_blendMode}: {ex.Message}");
                baseLayer.CopyTo(output);
            }
        }

        #region Blend Mode Implementations

        private void ApplyMultiply(Mat baseLayer, Mat output)
        {
            // Multiply: Multiplies pixel values and normalizes
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            // Create a temporary Mat to store the result
            using var resultMat = new Mat();
            Cv2.Multiply(normalized, normalized, resultMat);

            // Convert back to original type
            resultMat.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyScreen(Mat baseLayer, Mat output)
        {
            // Screen: Invert, multiply, invert again
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var inverted = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), normalized, inverted);

            using var multiplied = new Mat();
            Cv2.Multiply(inverted, inverted, multiplied);

            using var result = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), multiplied, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyOverlay(Mat baseLayer, Mat output)
        {
            // Overlay: Conditional blend based on base layer luminance
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var gray = new Mat();
            Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

            // Create masks for dark and light areas
            using var darkMask = new Mat();
            Cv2.Threshold(gray, darkMask, 0.5, 1.0, ThresholdTypes.Binary);

            using var lightMask = new Mat();
            Cv2.Threshold(gray, lightMask, 0.5, 1.0, ThresholdTypes.BinaryInv);

            // Convert masks to 3-channel for multiplication
            using var darkMask3Ch = new Mat();
            Cv2.CvtColor(darkMask, darkMask3Ch, ColorConversionCodes.GRAY2BGR);

            using var lightMask3Ch = new Mat();
            Cv2.CvtColor(lightMask, lightMask3Ch, ColorConversionCodes.GRAY2BGR);

            // Apply screen to light areas
            using var screenResult = new Mat();
            ApplyScreen(normalized, screenResult);

            // Apply multiply to dark areas
            using var multiplyResult = new Mat();
            ApplyMultiply(normalized, multiplyResult);

            // Multiply with masks
            using var screenPart = new Mat();
            Cv2.Multiply(screenResult, darkMask3Ch, screenPart);

            using var multiplyPart = new Mat();
            Cv2.Multiply(multiplyResult, lightMask3Ch, multiplyPart);

            // Combine results
            using var result = new Mat();
            Cv2.Add(screenPart, multiplyPart, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyDarken(Mat baseLayer, Mat output)
        {
            // Darken: Take the darker of each pixel
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var result = new Mat();
            Cv2.Min(normalized, normalized, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyLighten(Mat baseLayer, Mat output)
        {
            // Lighten: Take the lighter of each pixel
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var result = new Mat();
            Cv2.Max(normalized, normalized, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyColorDodge(Mat baseLayer, Mat output)
        {
            // Color Dodge: Divides the base by the inverted blend
            using var normalizedBase = new Mat();
            baseLayer.ConvertTo(normalizedBase, MatType.CV_32FC3, 1.0 / 255.0);

            using var inverted = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), normalizedBase, inverted);

            // Avoid division by zero
            using var epsilon = new Mat(inverted.Size(), inverted.Type(), new Scalar(0.001, 0.001, 0.001));
            using var divisor = new Mat();
            Cv2.Max(inverted, epsilon, divisor);

            using var result = new Mat();
            Cv2.Divide(normalizedBase, divisor, result);

            // Clamp to [0, 1]
            using var clamped = new Mat();
            // Find min/max values but don't use them - just to avoid errors
            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);
            Cv2.Min(result, new Scalar(1, 1, 1), clamped);

            clamped.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyColorBurn(Mat baseLayer, Mat output)
        {
            // Color Burn: Inverts the base, divides by the blend, then inverts the result
            using var normalizedBase = new Mat();
            baseLayer.ConvertTo(normalizedBase, MatType.CV_32FC3, 1.0 / 255.0);

            using var invertedBase = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), normalizedBase, invertedBase);

            // Avoid division by zero
            using var epsilon = new Mat(normalizedBase.Size(), normalizedBase.Type(), new Scalar(0.001, 0.001, 0.001));
            using var divisor = new Mat();
            Cv2.Max(normalizedBase, epsilon, divisor);

            using var division = new Mat();
            Cv2.Divide(invertedBase, divisor, division);

            using var result = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), division, result);

            // Clamp to [0, 1]
            using var min = new Mat();
            Cv2.Max(result, new Scalar(0, 0, 0), min);

            using var clamped = new Mat();
            Cv2.Min(min, new Scalar(1, 1, 1), clamped);

            clamped.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyHardLight(Mat baseLayer, Mat output)
        {
            // Hard Light: Screen for blend values > 0.5, multiply for values <= 0.5
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var gray = new Mat();
            Cv2.CvtColor(normalized, gray, ColorConversionCodes.BGR2GRAY);

            // Create masks for dark and light areas
            using var lightMask = new Mat();
            Cv2.Threshold(gray, lightMask, 0.5, 1.0, ThresholdTypes.Binary);

            using var darkMask = new Mat();
            Cv2.Threshold(gray, darkMask, 0.5, 1.0, ThresholdTypes.BinaryInv);

            // Convert masks to 3-channel
            using var lightMask3Ch = new Mat();
            Cv2.CvtColor(lightMask, lightMask3Ch, ColorConversionCodes.GRAY2BGR);

            using var darkMask3Ch = new Mat();
            Cv2.CvtColor(darkMask, darkMask3Ch, ColorConversionCodes.GRAY2BGR);

            // Apply screen to light areas
            using var screenResult = new Mat();
            ApplyScreen(normalized, screenResult);

            // Apply multiply to dark areas
            using var multiplyResult = new Mat();
            ApplyMultiply(normalized, multiplyResult);

            // Multiply with masks
            using var screenPart = new Mat();
            Cv2.Multiply(screenResult, lightMask3Ch, screenPart);

            using var multiplyPart = new Mat();
            Cv2.Multiply(multiplyResult, darkMask3Ch, multiplyPart);

            // Combine results
            using var result = new Mat();
            Cv2.Add(screenPart, multiplyPart, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplySoftLight(Mat baseLayer, Mat output)
        {
            // Soft Light: Similar to overlay but gentler effect
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var twoTimesNormalized = new Mat();
            Cv2.Multiply(normalized, new Scalar(2, 2, 2), twoTimesNormalized);

            using var sqrtNormalized = new Mat();
            Cv2.Sqrt(normalized, sqrtNormalized);

            using var darkMask = new Mat();
            Cv2.Threshold(normalized, darkMask, 0.5, 1.0, ThresholdTypes.BinaryInv);

            using var darkMask3Ch = new Mat();
            Cv2.CvtColor(darkMask, darkMask3Ch, ColorConversionCodes.GRAY2BGR);

            using var lightMask = new Mat();
            Cv2.Threshold(normalized, lightMask, 0.5, 1.0, ThresholdTypes.Binary);

            using var lightMask3Ch = new Mat();
            Cv2.CvtColor(lightMask, lightMask3Ch, ColorConversionCodes.GRAY2BGR);

            using var darkResult = new Mat();
            Cv2.Multiply(normalized, twoTimesNormalized, darkResult);

            using var oneMinusNormalized = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), normalized, oneMinusNormalized);

            using var twoMinusTwoTimesNormalized = new Mat();
            Cv2.Subtract(new Scalar(2, 2, 2), twoTimesNormalized, twoMinusTwoTimesNormalized);

            using var temp = new Mat();
            Cv2.Multiply(oneMinusNormalized, twoMinusTwoTimesNormalized, temp);

            using var lightResult = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), temp, lightResult);

            using var darkPart = new Mat();
            Cv2.Multiply(darkResult, darkMask3Ch, darkPart);

            using var lightPart = new Mat();
            Cv2.Multiply(lightResult, lightMask3Ch, lightPart);

            using var result = new Mat();
            Cv2.Add(darkPart, lightPart, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyDifference(Mat baseLayer, Mat output)
        {
            // Difference: Absolute difference between base and blend
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var result = new Mat();
            Cv2.Absdiff(normalized, normalized, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        private void ApplyExclusion(Mat baseLayer, Mat output)
        {
            // Exclusion: Similar to difference but lower contrast
            using var normalized = new Mat();
            baseLayer.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);

            using var multiplied = new Mat();
            Cv2.Multiply(normalized, normalized, multiplied);

            using var halfScalar = new Mat(multiplied.Size(), multiplied.Type(), new Scalar(0.5, 0.5, 0.5));
            using var temp = new Mat();
            Cv2.Subtract(multiplied, halfScalar, temp);

            using var result = new Mat();
            Cv2.Subtract(halfScalar, temp, result);

            result.ConvertTo(output, baseLayer.Type(), 255.0);
        }

        #endregion
    }
}