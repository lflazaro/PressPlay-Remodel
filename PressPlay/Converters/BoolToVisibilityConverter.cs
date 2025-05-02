﻿using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PressPlay.Converters
{
    public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return Invert ? (b ? Visibility.Collapsed : Visibility.Visible) : (b ? Visibility.Visible : Visibility.Collapsed);
            }

            return Visibility.Visible;
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
