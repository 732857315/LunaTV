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
    public double percentage { get; set; }
    public long size { get; set; }
    public long totalSize { get; set; }
    public string sizeStr { get; set; } = String.Empty;
    public string speed { get; set; } = String.Empty;
    public TimeSpan? remainingTime { get; set; }
    public string remainingTimeStr { get; set; } = string.Empty;
    public string? name { get; set; }
    public string? url { get; set; }
    public string? saveDir { get; set; }
    public DownloadType downloadType { get; set; } = DownloadType.None;
}