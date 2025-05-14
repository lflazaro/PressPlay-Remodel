using OpenCvSharp;
using PressPlay.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace PressPlay.Effects
{
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
        public ObservableCollection<EffectParameter> Parameters { get; }
            = new ObservableCollection<EffectParameter>();

        public BlendingEffect(TrackItem item)
        {
            _item = item;
            InitializeParameters();
        }

        private void InitializeParameters()
        {
            var blendModeParam = new EffectParameter("Blend Mode", _blendMode.ToString());
            Parameters.Add(blendModeParam);
        }

        private void UpdateParameters()
        {
            var param = Parameters.FirstOrDefault(p => p.Name == "Blend Mode");
            if (param != null)
                param.Value = _blendMode.ToString();
        }

        /// <summary>
        /// Apply the effect to a single frame (no background).
        /// </summary>
        public void ProcessFrame(Mat baseLayer, Mat output)
        {
            if (!Enabled || _blendMode == BlendMode.Normal)
            {
                baseLayer.CopyTo(output);
                return;
            }

            switch (_blendMode)
            {
                case BlendMode.Multiply:
                    ApplyMultiply(baseLayer, baseLayer, output);
                    break;
                case BlendMode.Screen:
                    ApplyScreen(baseLayer, baseLayer, output);
                    break;
                case BlendMode.Overlay:
                    ApplyOverlay(baseLayer, baseLayer, output);
                    break;
                case BlendMode.Darken:
                    ApplyDarken(baseLayer, baseLayer, output);
                    break;
                case BlendMode.Lighten:
                    ApplyLighten(baseLayer, baseLayer, output);
                    break;
                case BlendMode.ColorDodge:
                    ApplyColorDodge(baseLayer, baseLayer, output);
                    break;
                case BlendMode.ColorBurn:
                    ApplyColorBurn(baseLayer, baseLayer, output);
                    break;
                case BlendMode.Exclusion:
                    ApplyExclusion(baseLayer, baseLayer, output);
                    break;
                case BlendMode.HardLight:
                    ApplyHardLight(baseLayer, baseLayer, output);
                    break;
                case BlendMode.SoftLight:
                    ApplySoftLight(baseLayer, baseLayer, output);
                    break;
                case BlendMode.Difference:
                    ApplyDifference(baseLayer, baseLayer, output);
                    break;
                default:
                    baseLayer.CopyTo(output);
                    break;
            }
        }

        #region Blend Mode Implementations

        public void ApplyMultiply(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var resultF = new Mat();
            Cv2.Multiply(bgF, fgF, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyScreen(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var invBg = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), bgF, invBg);
            using var invFg = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), fgF, invFg);

            using var multInv = new Mat();
            Cv2.Multiply(invBg, invFg, multInv);

            using var resultF = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), multInv, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyOverlay(Mat background, Mat foreground, Mat output)
        {
            // Multiply result
            using var multiplyResult = new Mat();
            ApplyMultiply(background, foreground, multiplyResult);

            // Screen result
            using var screenResult = new Mat();
            ApplyScreen(background, foreground, screenResult);

            // Determine mask from background luminance
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var gray = new Mat();
            Cv2.CvtColor(bgF, gray, ColorConversionCodes.BGR2GRAY);

            using var mask = new Mat();
            Cv2.Threshold(gray, mask, 0.5, 1.0, ThresholdTypes.Binary);
            using var mask3 = new Mat();
            Cv2.CvtColor(mask, mask3, ColorConversionCodes.GRAY2BGR);
            using var invMask3 = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), mask3, invMask3);

            using var darkPart = new Mat();
            Cv2.Multiply(multiplyResult, invMask3, darkPart);
            using var lightPart = new Mat();
            Cv2.Multiply(screenResult, mask3, lightPart);

            using var resultF = new Mat();
            Cv2.Add(darkPart, lightPart, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyDarken(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var resultF = new Mat();
            Cv2.Min(bgF, fgF, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyLighten(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var resultF = new Mat();
            Cv2.Max(bgF, fgF, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyColorDodge(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var invFg = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), fgF, invFg);
            using var epsilon = new Mat(invFg.Size(), invFg.Type(), new Scalar(0.001, 0.001, 0.001));
            using var denom = new Mat();
            Cv2.Max(invFg, epsilon, denom);

            using var resultF = new Mat();
            Cv2.Divide(bgF, denom, resultF);

            using var clamped = new Mat();
            Cv2.Min(resultF, new Scalar(1, 1, 1), clamped);

            clamped.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyColorBurn(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var invBg = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), bgF, invBg);
            using var epsilon = new Mat(fgF.Size(), fgF.Type(), new Scalar(0.001, 0.001, 0.001));
            using var denom = new Mat();
            Cv2.Max(fgF, epsilon, denom);

            using var division = new Mat();
            Cv2.Divide(invBg, denom, division);

            using var resultF = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), division, resultF);

            using var clampedMin = new Mat();
            Cv2.Max(resultF, new Scalar(0, 0, 0), clampedMin);
            using var clamped = new Mat();
            Cv2.Min(clampedMin, new Scalar(1, 1, 1), clamped);

            clamped.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyHardLight(Mat background, Mat foreground, Mat output)
        {
            // Hard Light is Overlay with swapped inputs
            ApplyOverlay(foreground, background, output);
        }

        public void ApplySoftLight(Mat background, Mat foreground, Mat output)
        {
            // Soft Light: gentle contrast shading
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            // Precompute 2*fg and sqrt(bg)
            using var twoFg = new Mat();
            Cv2.Multiply(fgF, new Scalar(2, 2, 2), twoFg);
            using var sqrtBg = new Mat();
            Cv2.Sqrt(bgF, sqrtBg);

            // Dark part: bg - (1 - 2*fg)*(bg - bg^2)
            using var bgSquared = new Mat();
            Cv2.Multiply(bgF, bgF, bgSquared);
            using var temp1 = new Mat();
            Cv2.Subtract(bgF, bgSquared, temp1);
            using var invTwoFg = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), twoFg, invTwoFg);
            using var part1 = new Mat();
            Cv2.Multiply(temp1, invTwoFg, part1);

            // Light part: bg + (2*fg - 1)*(sqrt(bg) - bg)
            using var fgMinusOne = new Mat();
            Cv2.Subtract(twoFg, new Scalar(1, 1, 1), fgMinusOne);
            using var diffSqrtBgBg = new Mat();
            Cv2.Subtract(sqrtBg, bgF, diffSqrtBgBg);
            using var part2 = new Mat();
            Cv2.Multiply(fgMinusOne, diffSqrtBgBg, part2);

            // Combine based on fg threshold
            using var mask = new Mat();
            Cv2.Threshold(fgF, mask, 0.5, 1.0, ThresholdTypes.Binary);
            using var mask3 = new Mat();
            Cv2.CvtColor(mask, mask3, ColorConversionCodes.GRAY2BGR);
            using var invMask3 = new Mat();
            Cv2.Subtract(new Scalar(1, 1, 1), mask3, invMask3);

            using var darkPart = new Mat();
            Cv2.Multiply(part1, invMask3, darkPart);
            using var lightPart = new Mat();
            Cv2.Multiply(part2, mask3, lightPart);

            using var resultF = new Mat();
            Cv2.Add(darkPart, lightPart, resultF);
            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyDifference(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var resultF = new Mat();
            Cv2.Absdiff(bgF, fgF, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        public void ApplyExclusion(Mat background, Mat foreground, Mat output)
        {
            using var bgF = new Mat();
            background.ConvertTo(bgF, MatType.CV_32FC3, 1.0 / 255.0);
            using var fgF = new Mat();
            foreground.ConvertTo(fgF, MatType.CV_32FC3, 1.0 / 255.0);

            using var mult = new Mat();
            Cv2.Multiply(bgF, fgF, mult);
            using var twoMult = new Mat();
            Cv2.Multiply(mult, new Scalar(2, 2, 2), twoMult);

            using var sum = new Mat();
            Cv2.Add(bgF, fgF, sum);

            using var resultF = new Mat();
            Cv2.Subtract(sum, twoMult, resultF);

            resultF.ConvertTo(output, background.Type(), 255.0);
        }

        #endregion
    }
}
