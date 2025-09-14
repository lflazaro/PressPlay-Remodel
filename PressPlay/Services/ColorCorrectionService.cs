using System;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using PressPlay.Models;

namespace PressPlay.Services
{
    public static class ColorCorrectionService
    {
        public static BitmapSource Apply(BitmapSource source, TrackItem item)
        {
            if (source == null)
                return source;
            if (item == null)
                return source;

            // Skip processing when default values
            bool needHueSat = Math.Abs(item.Hue) > 0.001 || Math.Abs(item.Saturation - 1.0) > 0.001;
            bool needBc = Math.Abs(item.Brightness) > 0.001 || Math.Abs(item.Contrast - 1.0) > 0.001;
            bool needGamma = Math.Abs(item.Gamma - 1.0) > 0.001;
            if (!needHueSat && !needBc && !needGamma)
                return source;

            using var mat = BitmapSourceConverter.ToMat(source);

            if (needHueSat)
            {
                using var hsv = new Mat();
                Cv2.CvtColor(mat, hsv, ColorConversionCodes.BGR2HSV);
                Mat[] channels = Cv2.Split(hsv);
                if (Math.Abs(item.Hue) > 0.001)
                {
                    Cv2.Add(channels[0], new Scalar(item.Hue / 2.0), channels[0]);
                    Cv2.Min(channels[0], new Scalar(179), channels[0]);
                }
                if (Math.Abs(item.Saturation - 1.0) > 0.001)
                {
                    Cv2.Multiply(channels[1], new Scalar(item.Saturation), channels[1]);
                    Cv2.Min(channels[1], new Scalar(255), channels[1]);
                }
                Cv2.Merge(channels, hsv);
                Cv2.CvtColor(hsv, mat, ColorConversionCodes.HSV2BGR);
                foreach (var c in channels)
                    c.Dispose();
            }

            if (needBc)
            {
                mat.ConvertTo(mat, -1, item.Contrast, item.Brightness);
            }

            if (needGamma)
            {
                byte[] lut = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    lut[i] = (byte)(Math.Pow(i / 255.0, 1.0 / item.Gamma) * 255.0);
                }
                using var lutMat = new Mat(1, 256, MatType.CV_8UC1, lut);
                Cv2.LUT(mat, lutMat, mat);
            }

            return BitmapSourceConverter.ToBitmapSource(mat);
        }
    }
}
