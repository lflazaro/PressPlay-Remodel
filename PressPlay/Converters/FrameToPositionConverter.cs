// PressPlay/Converters/FrameToPositionConverter.cs
using PressPlay.Helpers;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PressPlay.Converters
{
    public class FrameToPositionConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int frame)
            {
                // Get zoom level from parameter or use default
                int zoomLevel = 1;
                if (parameter is int zoom)
                    zoomLevel = zoom;

                return frame * Constants.TimelinePixelsInSeparator / Constants.TimelineZooms[zoomLevel];
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double position)
            {
                // Get zoom level from parameter or use default
                int zoomLevel = 1;
                if (parameter is int zoom)
                    zoomLevel = zoom;

                return (int)(position * Constants.TimelineZooms[zoomLevel] / Constants.TimelinePixelsInSeparator);
            }
            return 0;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}