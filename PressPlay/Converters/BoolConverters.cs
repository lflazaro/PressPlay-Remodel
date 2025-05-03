using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace PressPlay.Converters
{
    /// <summary>
    /// Converts a boolean value to a brush
    /// </summary>
    public class BoolToBrushConverter : MarkupExtension, IValueConverter
    {
        public Brush TrueValue { get; set; } = Brushes.White;
        public Brush FalseValue { get; set; } = Brushes.Black;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? TrueValue : FalseValue;
            }

            return FalseValue;
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

    /// <summary>
    /// Converts a boolean value to a thickness
    /// </summary>
    public class BoolToThicknessConverter : MarkupExtension, IValueConverter
    {
        public double TrueValue { get; set; } = 2;
        public double FalseValue { get; set; } = 1;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return new Thickness(b ? TrueValue : FalseValue);
            }

            return new Thickness(FalseValue);
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