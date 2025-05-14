using OpenCvSharp;
using PressPlay.Models;
using System.Collections.ObjectModel;

namespace PressPlay.Effects
{
    public class TransformEffect : IEffect
    {
        public string Name => "Transform";
        public bool Enabled { get; set; } = true;

        public ObservableCollection<EffectParameter> Parameters { get; }
            = new ObservableCollection<EffectParameter>();

        private readonly TrackItem _item;

        public TransformEffect(TrackItem item) => _item = item;

        public void ProcessFrame(Mat input, Mat output)
        {
            if (!_item.HasAnyTransform() && _item.Opacity.Approximately(1.0))
            {
                // No transformation needed, just copy the input
                input.CopyTo(output);
                return;
            }

            // Get dimensions
            var w = input.Width;
            var h = input.Height;
            var cx = w / 2.0;
            var cy = h / 2.0;

            // Step 1: Create a larger canvas to avoid border artifacts during transformation
            // Determine the output size based on the transformation
            double scaleFactor = Math.Max(_item.ScaleX, _item.ScaleY);
            int padX = (int)(w * 0.1 + Math.Abs(_item.TranslateX)); // 10% padding + translation
            int padY = (int)(h * 0.1 + Math.Abs(_item.TranslateY)); // 10% padding + translation

            // Create expanded canvas
            Size expandedSize = new Size(w + padX * 2, h + padY * 2);
            using var expandedMat = new Mat(expandedSize, input.Type(), Scalar.All(0));

            // Define ROI for placing the original image
            Rect roi = new Rect(padX, padY, w, h);
            using var roiMat = expandedMat.SubMat(roi);
            input.CopyTo(roiMat);

            // Step 2: Create the transformation matrix
            // Adjust center point for expanded image
            double expandedCx = cx + padX;
            double expandedCy = cy + padY;

            var M = Cv2.GetRotationMatrix2D(
                new Point2f((float)expandedCx, (float)expandedCy),
                (float)_item.Rotation,
                1.0);

            // Apply scale
            M.Set(0, 0, M.At<double>(0, 0) * _item.ScaleX);
            M.Set(0, 1, M.At<double>(0, 1) * _item.ScaleY);
            M.Set(1, 0, M.At<double>(1, 0) * _item.ScaleX);
            M.Set(1, 1, M.At<double>(1, 1) * _item.ScaleY);

            // Apply translation
            M.Set(0, 2, M.At<double>(0, 2) + _item.TranslateX);
            M.Set(1, 2, M.At<double>(1, 2) + _item.TranslateY);

            // Step 3: Apply transformation to the expanded image
            using var transformedMat = new Mat();
            Cv2.WarpAffine(
                expandedMat,
                transformedMat,
                M,
                expandedSize,
                InterpolationFlags.Linear,
                BorderTypes.Constant,   // Use constant border with black color
                Scalar.All(0));         // Black borders

            // Step 4: Crop back to original size with the transformed image center-aligned
            int cropX = (transformedMat.Width - w) / 2;
            int cropY = (transformedMat.Height - h) / 2;

            // Ensure we don't go out of bounds
            cropX = Math.Max(0, Math.Min(cropX, transformedMat.Width - w));
            cropY = Math.Max(0, Math.Min(cropY, transformedMat.Height - h));

            Rect cropRoi = new Rect(cropX, cropY,
                                   Math.Min(w, transformedMat.Width - cropX),
                                   Math.Min(h, transformedMat.Height - cropY));

            // If regions are valid, extract the cropped region to the output
            if (cropRoi.Width > 0 && cropRoi.Height > 0)
            {
                using var croppedMat = transformedMat.SubMat(cropRoi);
                croppedMat.CopyTo(output);
            }
            else
            {
                // Fallback if crop is invalid
                transformedMat.CopyTo(output);
            }

            // Step 5: Apply opacity if needed
            if (_item.Opacity < 0.999)
            {
                using var black = new Mat(output.Size(), output.Type(), Scalar.All(0));
                Cv2.AddWeighted(output, _item.Opacity, black, 1 - _item.Opacity, 0, output);
            }
        }
    }

    static class Extensions
    {
        public static bool HasAnyTransform(this TrackItem ti) =>
            ti.TranslateX.Approximately(0) == false ||
            ti.TranslateY.Approximately(0) == false ||
            ti.Rotation.Approximately(0) == false ||
            ti.ScaleX.Approximately(1) == false ||
            ti.ScaleY.Approximately(1) == false;

        public static bool Approximately(this double a, double b, double eps = 1e-6)
            => Math.Abs(a - b) < eps;
    }
}