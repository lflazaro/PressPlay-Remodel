using PressPlay.Helpers;
using PressPlay.Models;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace PressPlay.Converters
{
    public class TrackItemFadePositionConverter : MarkupExtension, IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // guard early
            if (values == null || values.Length < 3)
            {
                if (targetType == typeof(PointCollection)) return new PointCollection();
                if (targetType == typeof(double)) return 0.0;
                return null;
            }

            var project = values[0] as Project;
            var item = values[2] as ITrackItem;

            // if we still don't have a project or item, bail out safely
            if (project == null || item == null)
            {
                if (targetType == typeof(PointCollection)) return new PointCollection();
                if (targetType == typeof(double)) return 0.0;
                return null;
            }

            // now we know project & item are non-null
            if (targetType == typeof(double))
            {
                bool isLeft = (FadeControlType)parameter == FadeControlType.Left;
                return isLeft
                    ? item.GetFadeInXPosition(project.TimelineZoom)
                    : item.GetFadeOutXPosition(project.TimelineZoom);
            }

            if (targetType == typeof(PointCollection))
            {
                bool isLeft = (FadeControlType)parameter == FadeControlType.Left;
                var collection = new PointCollection
        {
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 48)
        };

                if (isLeft)
                {
                    collection.Add(new System.Windows.Point(
                        item.GetFadeInXPosition(project.TimelineZoom),
                        0));
                }
                else
                {
                    // negative direction for right handle
                    collection.Add(new System.Windows.Point(
                        -item.GetFadeOutXPosition(project.TimelineZoom),
                        0));
                }

                return collection;
            }

            return null;
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
