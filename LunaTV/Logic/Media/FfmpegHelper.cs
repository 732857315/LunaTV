using LunaTV.Constants;
using System.IO;
using System.Runtime.InteropServices;

namespace LunaTV.Logic.Media;

public static class FfmpegHelper
{
    public static bool IsFfmpegInstalled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        // UseFFmpegForWaveExtraction = true;        
        return File.Exists(GlobalDefine.FFmpegPath);
    }
}