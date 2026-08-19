using System;
using System.Globalization;
using System.Windows.Data;

namespace YiMengFix.UI;

public sealed class ProgressScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double number)
        {
            return Math.Clamp(number / 100d, 0d, 1d);
        }

        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
