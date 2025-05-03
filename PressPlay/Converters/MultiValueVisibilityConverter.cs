using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PressPlay.Converters
{
    public class MultiValueVisibilityConverter : MarkupExtension, IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // For AudioTrackItem waveform loading indicator:
            // values[0] = WaveformImagePath (string)
            // values[1] = HasWaveform (bool)

            // Show loading indicator when WaveformImagePath is not null/empty but HasWaveform is false
            if (values.Length >= 2)
            {
                bool hasPath = !string.IsNullOrEmpty(values[0] as string);
                bool hasWaveform = values[1] is bool b && b;

                // Show the loading indicator only when we have a path but no waveform yet
                return (hasPath && !hasWaveform) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
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