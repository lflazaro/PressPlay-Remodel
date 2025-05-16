using OpenCvSharp;
using PressPlay.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace PressPlay.Effects
{
    public class TransformEffect : IEffect
    {
        public string Name => "Transform";
        public bool Enabled { get; set; } = true;

        private TimeCode _lastAppliedTime = null;

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
            var current = _item.Position;   // or whatever tracks “which frame” you’re on
            if (_lastAppliedTime != null && current.Equals(_lastAppliedTime))
            {
                // second invocation for the same frame—just pass through
                input.CopyTo(output);
                return;
            }

            _lastAppliedTime = current;
            Debug.WriteLine($"[TF] ProcessFrame: Enabled={Enabled}," + $"_item={(_item == null ? "NULL" : "OK")},"+ $"HasAnyTransform={(_item?.HasAnyTransform() ?? false)}");
            if (!Enabled || _item == null)
            {
                input.CopyTo(output);
                return;
            }

            int w = input.Width, h = input.Height;

            // build a single 2×3 matrix:
            //  1) rotate around image center,
            //  2) then shift by your TranslateX/Y
            var M = Cv2.GetRotationMatrix2D(
                new Point2f(w / 2f, h / 2f),
                _item.Rotation,
                _item.ScaleX   // this is uniform scale; if you need separate X/Y, build your own 2×3
            );
            M.Set(0, 2, M.At<double>(0, 2) + _item.TranslateX);
            M.Set(1, 2, M.At<double>(1, 2) + _item.TranslateY);

            // warp directly into the output
            Cv2.WarpAffine(
                input,
                output,
                M,
                new Size(w, h),
                InterpolationFlags.Linear,
                BorderTypes.Constant,
                Scalar.All(0)
            );

            // optionally apply opacity
            if (_item.Opacity < 0.999)
            {
                using var black = new Mat(output.Size(), output.Type(), Scalar.All(0));
                Cv2.AddWeighted(output, _item.Opacity, black, 1 - _item.Opacity, 0, output);
            }
        }
        public void Reset()
        {
            // Force a recalculation of all transform parameters
            if (_item != null)
            {
                // Capture original values
                double rotation = _item.Rotation;
                double scaleX = _item.ScaleX;
                double scaleY = _item.ScaleY;

                // Reset then restore to force update
                _item.Rotation = 0;
                _item.ScaleX = 1;
                _item.ScaleY = 1;

                // Restore with notification
                _item.Rotation = rotation;
                _item.ScaleX = scaleX;
                _item.ScaleY = scaleY;

                Debug.WriteLine($"Reset transform: Rotation={rotation}, Scale=({scaleX},{scaleY})");
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