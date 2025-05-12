// Converters/NullToBoolConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace PressPlay.Converters
{
    /// <summary>
    /// Returns True if the value is non-null.  False otherwise.
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        // Converts null → false, non-null → true
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        // Back-conversion not supported
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
