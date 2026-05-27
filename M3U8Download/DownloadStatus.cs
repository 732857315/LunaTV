namespace M3U8Download;

public enum DownloadType
{
    None,
    Downloading,
    Downloaded,
    DownloadFailed
}

public class DownloadStatus
{
    public double Percentage { get; set; }
    public long Size { get; set; }
    public long TotalSize { get; set; }
    public string SizeStr { get; set; } = String.Empty;
    public string Speed { get; set; } = String.Empty;
    public TimeSpan? RemainingTime { get; set; }
    public string RemainingTimeStr { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? SaveDir { get; set; }
    public DownloadType DownloadType { get; set; } = DownloadType.None;
}