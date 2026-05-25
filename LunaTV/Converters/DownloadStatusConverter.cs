using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LunaTV.Converters;

public class DownloadStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDownloaded) return isDownloaded ? "已完成" : "待下载";
        return "未知";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}