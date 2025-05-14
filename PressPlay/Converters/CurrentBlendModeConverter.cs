using PressPlay.Effects;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace PressPlay.Converters
{
    public class CurrentBlendModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // The value will be the Effects collection
            if (value is IEnumerable effects)
            {
                // Try to find a BlendingEffect
                foreach (var effect in effects)
                {
                    if (effect is BlendingEffect blendEffect)
                    {
                        // Return the blend mode name
                        return blendEffect.BlendMode.ToString();
                    }
                }
            }

            // Default to Normal if no blend effect found
            return "Normal";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This direction isn't needed since we handle blend mode changes through a command
            throw new NotImplementedException();
        }
    }
}