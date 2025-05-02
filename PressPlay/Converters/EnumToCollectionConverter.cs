﻿using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PressPlay.Converters
{
	public class EnumToCollectionConverter : MarkupExtension, IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			var values = new List<object>();

			foreach (var item in Enum.GetValues(value.GetType()))
			{
				values.Add(item);
			}

			return values;
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
