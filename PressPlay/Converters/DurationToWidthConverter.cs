using PressPlay.Helpers;
using PressPlay.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PressPlay.Converters
{
    public class DurationToWidthConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeCode duration && parameter is int zoomLevel)
            {
                return Constants.FramesToPixels(duration.TotalFrames, zoomLevel);
            }

            // Default zoom level if not provided
            int defaultZoom = 1;
            if (value is TimeCode timeCode)
            {
                return Constants.FramesToPixels(timeCode.TotalFrames, defaultZoom);
            }

            return 100; // Default width if conversion fails
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}