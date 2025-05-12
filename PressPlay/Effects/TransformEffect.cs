using OpenCvSharp;
using PressPlay.Models;
using System.Collections.ObjectModel;   // for TrackItem

namespace PressPlay.Effects
{
    public class TransformEffect : IEffect
    {
        public string Name => "Transform";
        public bool Enabled { get; set; } = true;
        // no extra parameters here—driven by TrackItem directly
        public ObservableCollection<EffectParameter> Parameters { get; }
            = new ObservableCollection<EffectParameter>();

        private readonly TrackItem _item;
        public TransformEffect(TrackItem item) => _item = item;

        public void ProcessFrame(Mat input, Mat output)
        {
            if (!_item.Opacity.Approximately(0) || !_item.HasAnyTransform())
            {
                // Build single affine matrix: scale → rotate → translate
                var w = input.Width;
                var h = input.Height;
                var cx = w / 2.0;
                var cy = h / 2.0;

                // rotation + scale
                var M = Cv2.GetRotationMatrix2D(new Point2f((float)cx, (float)cy),
                                                 (float)_item.Rotation,
                                                 1.0);
                // inject scale onto the rotation matrix
                M.Set(0, 0, M.At<double>(0, 0) * _item.ScaleX);
                M.Set(0, 1, M.At<double>(0, 1) * _item.ScaleY);
                M.Set(1, 0, M.At<double>(1, 0) * _item.ScaleX);
                M.Set(1, 1, M.At<double>(1, 1) * _item.ScaleY);

                // inject translation
                M.Set(0, 2, M.At<double>(0, 2) + _item.TranslateX);
                M.Set(1, 2, M.At<double>(1, 2) + _item.TranslateY);

                // warp into output
                Cv2.WarpAffine(input, output, M, input.Size(),
                               InterpolationFlags.Linear,
                               BorderTypes.Transparent);
            }
            else
            {
                // no transform → just copy
                input.CopyTo(output);
            }

            // now apply opacity over a black background
            if (_item.Opacity < 0.999)
            {
                using var black = new Mat(input.Size(), input.Type(), Scalar.All(0));
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

