namespace LunaTV.Constants;

public static class Utilities
{
    public static string[] VideoFileExtensions { get; } =
        { ".avi", ".mkv", ".wmv", ".mpg", ".mpeg", ".divx", ".mp4", ".asf", ".flv", ".mov", ".m4v", ".vob", ".ogv", ".webm", ".ts", ".tts", ".m2ts", ".mts", ".avs", ".mxf" };

    public static string[] AudioFileExtensions { get; } = { ".mp3", ".wav", ".wma", ".ogg", ".mpa", ".m4a", ".ape", ".aiff", ".flac", ".aac", ".ac3", ".eac3", ".mka", ".opus", ".adts", ".m4b" };
}

public enum SeSpectrogramStyle
{
    Classic,
    ClassicViridis,
    ClassicPlasma,
    ClassicInferno,
    ClassicTurbo,
    Neon
}