using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using PressPlay.Helpers;

namespace PressPlay.Converters
{
    public class KeyframePositionConverter : MarkupExtension, IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3)
                return 0.0;

            if (values[0] is int frame &&
                values[1] is int startFrame &&
                values[2] is int zoom)
            {
                int diff = frame - startFrame;
                return Constants.FramesToPixels(diff, zoom);
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
