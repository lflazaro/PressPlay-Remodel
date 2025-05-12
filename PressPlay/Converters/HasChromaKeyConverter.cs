// Converters/HasChromaKeyConverter.cs
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using PressPlay.Effects;

namespace PressPlay.Converters
{
    /// <summary>
    /// Binds an Effects collection to Visibility:
    /// Visible if it contains any ChromaKeyEffect, else Collapsed.
    /// </summary>
    public class HasChromaKeyConverter : IValueConverter
    {
        // Expects value to be IEnumerable of IEffect
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable effects)
            {
                foreach (var fx in effects)
                {
                    if (fx is ChromaKeyEffect)
                        return Visibility.Visible;
                }
            }
            return Visibility.Collapsed;
        }

        // Back-conversion not supported
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

