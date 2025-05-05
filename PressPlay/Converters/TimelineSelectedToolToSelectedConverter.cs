using PressPlay.CustomControls;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PressPlay.Converters
{
    public class TimelineSelectedToolToSelectedConverter : MarkupExtension, IValueConverter
    {
        public TimelineSelectedTool SelectedTool { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Make sure we're comparing the same type
            if (value is TimelineSelectedTool currentTool && parameter is TimelineSelectedTool desiredTool)
                return currentTool == desiredTool;

            // For string parameters (from XAML)
            if (value is TimelineSelectedTool tool && parameter is string paramString)
            {
                if (Enum.TryParse<TimelineSelectedTool>(paramString, out var paramTool))
                    return tool == paramTool;
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? parameter : TimelineSelectedTool.SelectionTool;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
