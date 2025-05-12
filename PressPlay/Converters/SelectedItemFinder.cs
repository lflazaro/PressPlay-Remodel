using PressPlay.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace PressPlay.Converters
{
    public class SelectedItemFinder : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Project project && parameter is string paramString)
            {
                // Parse parameters (item type and property name)
                string[] parts = paramString.Split(',');
                string itemType = parts[0].Trim();
                string propName = parts.Length > 1 ? parts[1].Trim() : null;

                // Find the selected item
                object selectedItem = null;

                if (itemType == "TrackItem")
                {
                    selectedItem = project.Tracks
                        .SelectMany(t => t.Items)
                        .FirstOrDefault(item => item.IsSelected);
                }
                else if (itemType == "ProjectClip")
                {
                    var trackItem = project.Tracks
                        .SelectMany(t => t.Items)
                        .FirstOrDefault(item => item.IsSelected);

                    if (trackItem != null)
                    {
                        selectedItem = project.Clips
                            .OfType<ProjectClip>()
                            .FirstOrDefault(c =>
                                string.Equals(c.FilePath, trackItem.FilePath, StringComparison.OrdinalIgnoreCase)
                                || (trackItem is AudioTrackItem ati && c.Id == ati.ClipId));
                    }
                }

                // If property name is specified, access it
                if (selectedItem != null && !string.IsNullOrEmpty(propName))
                {
                    var prop = selectedItem.GetType().GetProperty(propName);
                    if (prop != null)
                    {
                        return prop.GetValue(selectedItem);
                    }
                }

                return selectedItem;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // This is more complex - we need to find the object and set the property
            // For simplicity, it's best to make this one-way binding
            throw new NotImplementedException();
        }
    }
}
