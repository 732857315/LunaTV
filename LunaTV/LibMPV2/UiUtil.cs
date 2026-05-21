using System;
using Avalonia;
using Avalonia.Styling;
using SkiaSharp;

namespace LunaTV.LibMpv2;

public static class UiUtil
{
    public static void DrawCheckerboardBackground(SKCanvas canvas, int width, int height, int squareSize = 16)
    {
        // Define colors for the checkerboard pattern        
        var lightColor = SKColor.Parse("#EEEEEE");
        var darkColor = SKColor.Parse("#BBBBBB");

        if (Application.Current?.ActualThemeVariant == ThemeVariant.Dark)
        {
            lightColor = SKColor.Parse("#333333"); // Darker color for light squares in dark theme
            darkColor = SKColor.Parse("#555555"); // Lighter color for dark squares in dark theme
        }

        using (var lightPaint = new SKPaint { Color = lightColor, Style = SKPaintStyle.Fill })
        using (var darkPaint = new SKPaint { Color = darkColor, Style = SKPaintStyle.Fill })
        {
            // Calculate number of squares needed
            var cols = (int)Math.Ceiling((double)width / squareSize);
            var rows = (int)Math.Ceiling((double)height / squareSize);

            for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
            {
                // Determine if this square should be light or dark
                var isLight = (row + col) % 2 == 0;
                var paint = isLight ? lightPaint : darkPaint;

                // Calculate square position and size
                var rect = new SKRect(
                    col * squareSize,
                    row * squareSize,
                    Math.Min((col + 1) * squareSize, width),
                    Math.Min((row + 1) * squareSize, height)
                );

                canvas.DrawRect(rect, paint);
            }
        }
    }
}