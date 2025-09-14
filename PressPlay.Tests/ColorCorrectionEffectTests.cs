using OpenCvSharp;
using PressPlay.Effects;
using Xunit;

namespace PressPlay.Tests;

public class ColorCorrectionEffectTests
{
    [Fact]
    public void TintColor_BlendsWithFrameInsteadOfOverlaying()
    {
        var effect = new ColorCorrectionEffect
        {
            TintColor = System.Windows.Media.Color.FromRgb(255, 0, 0)
        };

        using var input = new Mat(1, 2, MatType.CV_8UC3);
        input.Set(0, 0, new Scalar(10, 20, 30));
        input.Set(0, 1, new Scalar(40, 50, 60));

        using var output = new Mat();
        effect.ProcessFrame(input, output);

        var pixel1 = output.Get<Vec3b>(0, 0);
        var pixel2 = output.Get<Vec3b>(0, 1);

        Assert.Equal(0, pixel1.Item0);
        Assert.Equal(0, pixel1.Item1);
        Assert.Equal(30, pixel1.Item2);

        Assert.Equal(0, pixel2.Item0);
        Assert.Equal(0, pixel2.Item1);
        Assert.Equal(60, pixel2.Item2);
    }

    [Fact]
    public void Gamma_GreaterThanOne_DarkensImage()
    {
        var effect = new ColorCorrectionEffect { Gamma = 2.0 };

        using var input = new Mat(1, 1, MatType.CV_8UC3, new Scalar(128, 128, 128));
        using var output = new Mat();
        effect.ProcessFrame(input, output);

        var pixel = output.Get<Vec3b>(0, 0);
        Assert.True(pixel.Item0 < 128);
    }

    [Fact]
    public void Saturation_Zero_ProducesGrayscale()
    {
        var effect = new ColorCorrectionEffect { Saturation = 0.0 };

        using var input = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        using var output = new Mat();
        effect.ProcessFrame(input, output);

        var pixel = output.Get<Vec3b>(0, 0);
        Assert.Equal(pixel.Item0, pixel.Item1);
        Assert.Equal(pixel.Item1, pixel.Item2);
    }
}
