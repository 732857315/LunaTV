using Avalonia;
using System.Runtime.InteropServices;
using Ursa.Controls;

namespace LunaTV.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();

        ApplyPlatformSpecificMargin();

        // Test with OpenGL
        // var player = new LibMpvDynamicPlayer();
        // if (player.CanLoad())
        // {
        //     var view = new LibMpvDynamicOpenGlControl(player);
        //     ContentControl.Content = new VideoPlayerControl(player)
        //     {
        //         PlayerContent = view,
        //         StopIsVisible = true,
        //         FullScreenIsVisible = true,
        //         VerticalAlignment = VerticalAlignment.Stretch,
        //         HorizontalAlignment = HorizontalAlignment.Stretch
        //     };
        // }
        //
        // Dispatcher.InvokeAsync(async () =>
        // { 
        //     await player.LoadFile("/Users/x/Downloads/图片文字去除.mp4");
        //     player.Play();
        // });
    }

    private void ApplyPlatformSpecificMargin()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            LeftTitlebar.Margin = new Thickness(60, 0, 0, 0);
        }
    }
}