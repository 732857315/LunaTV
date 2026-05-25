using System;
using System.Diagnostics;
using System.IO;

namespace LunaTV.Constants;

public sealed class GlobalDefine
{
    private const int PlatformWindows = 1;
    private const int PlatformLinux = 2;
    private const int PlatformMac = 3;
    private static int s_platform;

    static GlobalDefine()
    {
        string fileName = OperatingSystem.IsWindows() ? "LunaTV.exe" : "LunaTV";
        FileVersionInfo app = FileVersionInfo.GetVersionInfo(Path.Combine(RootPath, fileName));

        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appPath = Path.Combine(basePath, "LunaTV");
        if (!Directory.Exists(appPath))
        {
            Directory.CreateDirectory(appPath);
        }
    }

    /// <summary>
    ///     App版本
    /// </summary>
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    ///     App根地址
    /// </summary>
    public static string RootPath => AppDomain.CurrentDomain.BaseDirectory;

    public static string DataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaTV");

    public static string DownloadPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LunaTV");

    /// <summary>
    ///     App数据库连接字符串
    /// </summary>
    public static string DbConn => Path.Combine(DataPath, "lunatv.sqlite");

    public static string AppJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaTV",
            "lunatv-app.json");

    public static string FFmpegPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

    public static string WaveformsFolder => Path.Combine(DataPath, "Waveforms");
    public static int WaveformMinimumSampleRate { get; set; } = 126;
    public static string SpectrogramsFolder => Path.Combine(DataPath, "Spectrograms");
    public static string SpectrogramStyle { get; set; } = SeSpectrogramStyle.Classic.ToString();

    public static bool UseFrameMode { get; set; } = false;

    public static bool IsRunningOnWindows
    {
        get
        {
            if (s_platform == 0)
            {
                s_platform = GetPlatform();
            }
            return s_platform == PlatformWindows;
        }
    }

    public static bool IsRunningOnLinux
    {
        get
        {
            if (s_platform == 0)
            {
                s_platform = GetPlatform();
            }
            return s_platform == PlatformLinux;
        }
    }

    public static bool IsRunningOnMac
    {
        get
        {
            if (s_platform == 0)
            {
                s_platform = GetPlatform();
            }
            return s_platform == PlatformMac;
        }
    }

    private static int GetPlatform()
    {
        // Current versions of Mono report MacOSX platform as Unix
        return Environment.OSVersion.Platform == PlatformID.MacOSX ||
               Environment.OSVersion.Platform == PlatformID.Unix && Directory.Exists("/Applications") && Directory.Exists("/System") && Directory.Exists("/Users")
            ? PlatformMac
            : Environment.OSVersion.Platform == PlatformID.Unix
                ? PlatformLinux
                : PlatformWindows;
    }
}