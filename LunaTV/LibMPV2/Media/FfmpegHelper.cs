using System.IO;
using System.Runtime.InteropServices;
using LunaTV.Constants;

namespace LunaTV.LibMPV2.Media;

public static class FfmpegHelper
{
    public static bool IsFfmpegInstalled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return true;

        // UseFFmpegForWaveExtraction = true;        
        return File.Exists(GlobalDefine.FFmpegPath);
    }
}