using Avalonia;
using Avalonia.Dialogs;
using Avalonia.Media;
using LunaTV.Constants;
using LunaTV.Extensions;
using LunaTV.Models;
using LunaTV.Services;
using Microsoft.Extensions.Hosting;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LunaTV;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // windows下输出中文
        if (OperatingSystem.IsWindows())
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var appJsonConfig = AppJsonConfigService.ReadJson<AppJsonConfig>(GlobalDefine.AppJsonPath) ?? new AppJsonConfig();
        appJsonConfig.Player ??= new Player();
        appJsonConfig.StoragePaths ??= new StoragePathsConfig();
        GlobalDefine.ApplyStoragePaths(appJsonConfig.StoragePaths);

        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddViewModels();
                services.AddServices();
                services.AddViews();
                services.AddDb();
            }).Build();
        ServiceLocator.Host = host;

#pragma warning disable CA1416
        return AppBuilder.Configure<App>()
            .UseManagedSystemDialogs()
#pragma warning restore CA1416
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                RenderingMode = new[] { X11RenderingMode.Glx, X11RenderingMode.Egl }
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    // put OpenGL first, to have higher priority over Metal
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Metal,
                    AvaloniaNativeRenderingMode.Software
                ]
            })
            .With(new Win32PlatformOptions())
            .With(new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new FontFallback
                    {
                        FontFamily = new FontFamily(GetFontFamily())
                    }
                }
            })
            .LogToTrace();
    }

    private static string GetFontFamily()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // windows下使用微软雅黑
            return "微软雅黑";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macos下使用pingfang sc
            return "PingFang SC";
        }

        return "";
    }
}
