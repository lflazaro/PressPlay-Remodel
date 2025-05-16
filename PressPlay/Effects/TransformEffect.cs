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

        private TrackItem _item;

        public TransformEffect(TrackItem item) => _item = item;

        public void SetTrackItem(TrackItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
        }

        public void ProcessFrame(Mat input, Mat output)
        {
            // 0) Fast-path: no transform & full opacity → passthrough
            if (!_item.HasAnyTransform() && _item.Opacity.Approximately(1.0))
            {
                input.CopyTo(output);
                return;
            }

            // 1) Base dimensions & compute pivot in px
            int w = input.Width, h = input.Height;
            double originX = _item.RotationOrigin.X * w;
            double originY = _item.RotationOrigin.Y * h;

            // 2) Pad out the canvas (10% + any translation) to avoid black edges
            int padX = (int)(w * 0.1 + Math.Abs(_item.TranslateX));
            int padY = (int)(h * 0.1 + Math.Abs(_item.TranslateY));
            Size expandedSize = new Size(w + padX * 2, h + padY * 2);

            using var expandedMat = new Mat(expandedSize, input.Type(), Scalar.All(0));
            // Place original image at (padX, padY) in the expandedMat
            var roi = new Rect(padX, padY, w, h);
            using (var slot = expandedMat.SubMat(roi))
                input.CopyTo(slot);

            // 3) Build a single affine: [scale → rotate] about (px,py), then translate
            double θ = _item.Rotation * Math.PI / 180.0;
            double cos = Math.Cos(θ), sin = Math.Sin(θ);

            // Combined scale+rotate
            double a = cos * _item.ScaleX;   // M[0,0]
            double b = -sin * _item.ScaleY;   // M[0,1]
            double c = sin * _item.ScaleX;   // M[1,0]
            double d = cos * _item.ScaleY;   // M[1,1]

            // Pivot in expanded‐canvas coords
            double px = padX + originX;
            double py = padY + originY;

            // Translation to keep pivot fixed + user translate
            double tx = (1 - a) * px - b * py + _item.TranslateX;
            double ty = c * px + (1 - d) * py + _item.TranslateY;

            var M = new Mat(2, 3, MatType.CV_64F);
            M.Set(0, 0, a); M.Set(0, 1, b); M.Set(0, 2, tx);
            M.Set(1, 0, c); M.Set(1, 1, d); M.Set(1, 2, ty);

            // 4) Warp the entire expandedMat
            using var transformedMat = new Mat();
            Cv2.WarpAffine(
                expandedMat,
                transformedMat,
                M,
                expandedSize,
                InterpolationFlags.Linear,
                BorderTypes.Constant,
                Scalar.All(0)
            );

            // 5) **Crop at the exact ROI** where we placed the original image
            var cropRoi = new Rect(padX, padY, w, h);
            // Guard against out-of-bounds just in case
            cropRoi.X = Math.Max(0, Math.Min(cropRoi.X, transformedMat.Width - w));
            cropRoi.Y = Math.Max(0, Math.Min(cropRoi.Y, transformedMat.Height - h));

            using var finalMat = transformedMat.SubMat(cropRoi);
            finalMat.CopyTo(output);

            // 6) Apply opacity if needed
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