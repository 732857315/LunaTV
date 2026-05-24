using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LunaTV.Converters;

public class StatusToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status switch
            {
                "未开始" => App.TopLevel.TryFindResource("LabelTagSolidGreyBackground", out var valueg)
                    ? (Brush)valueg
                    : Brush.Parse("#888D92"), // 灰色
                "下载中" => App.TopLevel.TryFindResource("LabelTagSolidOrangeBackground", out var valuey)
                    ? (Brush)valuey
                    : Brush.Parse("#FFAE43"), // 黄色
                "已完成" => App.TopLevel.TryFindResource("LabelTagSolidGreenBackground", out var valueg)
                    ? (Brush)valueg
                    : Brush.Parse("#97C65F"), // 绿色
                "下载失败" => App.TopLevel.TryFindResource("LabelTagSolidRedBackground", out var valueg)
                    ? (Brush)valueg
                    : Brush.Parse("#FC725A"), // 红色
                _ => Brush.Parse("#888D92") // 默认灰色
            };
        }

        return Brush.Parse("#888D92"); // 默认灰色
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return "";
    }
}