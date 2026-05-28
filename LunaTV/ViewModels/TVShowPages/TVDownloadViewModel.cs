using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaTV.Base.DB.UnitOfWork;
using LunaTV.Base.Models;
using LunaTV.Constants;
using LunaTV.ViewModels.Base;
using LunaTV.Views;
using M3U8Download;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;

namespace LunaTV.ViewModels.TVShowPages;

public partial class TVDownloadViewModel : ViewModelBase
{
    private readonly Dictionary<int, DownloadManager> _downloadManagers = new();

    private readonly DispatcherTimer _downloadTimer;
    private readonly SugarRepository<MediaDownload> _mediaDownloadTable;
    private readonly Dictionary<int, int> _downloadPersistTicks = new();
    private MediaDownloadViewModel? _currentDownloadingMVM;

    [ObservableProperty] private int _downloadingCount;
    [ObservableProperty] private string _downloadName = "曼达洛人";
    [ObservableProperty] private string _downloadUrl = "https://vod.360zyx.vip/20250708/7T2xjBRd/index.m3u8";
    private bool _isInitialized;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _waitingCount;

    public TVDownloadViewModel()
    {
        _mediaDownloadTable = App.Services.GetRequiredService<SugarRepository<MediaDownload>>();
        MediaDownloadViewModels = new ObservableCollection<MediaDownloadViewModel>();
        FilteredDownloadViewModels = new ObservableCollection<MediaDownloadViewModel>();
        _downloadTimer = new DispatcherTimer
            (TimeSpan.FromSeconds(1), DispatcherPriority.Background, DownloadTimerOnTick);
        _downloadTimer.Start();
        Dispatcher.UIThread.InvokeAsync(async () => { await LoadDownloadTasksFromDBAsync(); });
    }

    public ObservableCollection<MediaDownloadViewModel> MediaDownloadViewModels { get; set; }
    public ObservableCollection<MediaDownloadViewModel> FilteredDownloadViewModels { get; set; }
    [ObservableProperty] private string? _downloadFilterText;

    partial void OnDownloadFilterTextChanged(string? value)
    {
        RefreshFilteredDownloads();
    }

    private void RefreshFilteredDownloads()
    {
        var filterText = DownloadFilterText?.Trim();
        var filteredDownloads = string.IsNullOrWhiteSpace(filterText)
            ? MediaDownloadViewModels
            : new ObservableCollection<MediaDownloadViewModel>(MediaDownloadViewModels.Where(download => MatchesDownloadFilter(download, filterText)));

        FilteredDownloadViewModels.Clear();
        foreach (var download in filteredDownloads)
        {
            FilteredDownloadViewModels.Add(download);
        }
    }

    private static bool MatchesDownloadFilter(MediaDownloadViewModel download, string filterText)
    {
        return ContainsIgnoreCase(download.Name, filterText)
               || ContainsIgnoreCase(download.Episode, filterText)
               || ContainsIgnoreCase(download.Source, filterText)
               || ContainsIgnoreCase(download.OutputFilePath, filterText)
               || ContainsIgnoreCase(download.Url, filterText);
    }

    private static bool ContainsIgnoreCase(string? value, string filterText)
    {
        return value?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true;
    }

    // 从数据库加载下载任务
    private async Task LoadDownloadTasksFromDBAsync()
    {
        var downloads = await _mediaDownloadTable.GetListAsync(x => true);
        foreach (var downloadTask in downloads)
        {
            if (!MediaDownloadViewModels.Any(x => x.Url == downloadTask.Url))
            {
                var downloadStatus = (DownloadType)downloadTask.DownloadStatus;
                if (downloadStatus == DownloadType.Downloading) downloadStatus = DownloadType.None;

                var mediaDownloadViewModel = new MediaDownloadViewModel
                {
                    Id = downloadTask.Id,
                    Source = downloadTask.Source,
                    Name = downloadTask.Name,
                    Episode = downloadTask.Episode,
                    Url = downloadTask.Url,
                    DownloadStatus = downloadStatus,
                    LocalPath = downloadTask.LocalPath,
                    Status = ToStatusWord(downloadStatus),
                    Progress = downloadTask.Progress,
                    SizeStr = downloadTask.SizeText ?? "--/--",
                    Speed = downloadTask.SpeedText ?? "0 Bps",
                    RemainingTime = downloadTask.RemainingTimeText ?? "--:--:--",
                    ErrorMessage = downloadTask.ErrorMessage,
                    DownloadedBytes = downloadTask.DownloadedBytes,
                    TotalBytes = downloadTask.TotalBytes,
                    OutputFilePath = downloadTask.OutputFilePath,
                    Cover = downloadTask.Cover,
                    CreateTime = downloadTask.CreateTime,
                    UpdateTime = downloadTask.UpdateTime
                };
                var resolvedOutputFilePath = DownloadFileResolver.ResolveExistingFile(
                    mediaDownloadViewModel.OutputFilePath,
                    mediaDownloadViewModel.LocalPath,
                    mediaDownloadViewModel.Name,
                    mediaDownloadViewModel.Episode);
                if (downloadStatus == DownloadType.Downloaded && !string.IsNullOrWhiteSpace(resolvedOutputFilePath))
                {
                    mediaDownloadViewModel.OutputFilePath = resolvedOutputFilePath;
                    if (downloadTask.OutputFilePath != resolvedOutputFilePath)
                    {
                        downloadTask.OutputFilePath = resolvedOutputFilePath;
                        await _mediaDownloadTable.UpdateAsync(downloadTask);
                    }
                }

                MediaDownloadViewModels.Add(mediaDownloadViewModel);
                AddDownloadCounts(downloadStatus);
            }
        }

        RefreshFilteredDownloads();
        _isInitialized = true;
        if (!File.Exists(GlobalDefine.FFmpegPath))
        {
            App.Notification?.Show(new Notification("错误", "FFmpeg路径配置错误", NotificationType.Error),
                NotificationType.Error);
        }
    }

    private async void DownloadTimerOnTick(object? sender, EventArgs e)
    {
        if (!_isInitialized || !File.Exists(GlobalDefine.FFmpegPath))
        {
            return;
        }

        try
        {
            if (_currentDownloadingMVM is null)
            {
                if (MediaDownloadViewModels.Count > 0)
                {
                    Console.WriteLine($"等待下载：{MediaDownloadViewModels.Count}");
                    _currentDownloadingMVM =
                        MediaDownloadViewModels.FirstOrDefault(x => x.DownloadStatus == DownloadType.None);
                    if (_currentDownloadingMVM != null)
                    {
                        PreDownload(_currentDownloadingMVM);
                    }
                }
            }
            else
            {
                if (_currentDownloadingMVM.DownloadStatus == DownloadType.Downloading)
                {
                    Console.WriteLine($"下载中：{_currentDownloadingMVM.Name}");
                    // 刷新下载进度
                    Downloading(_currentDownloadingMVM);
                    if (_currentDownloadingMVM.DownloadStatus != DownloadType.Downloading)
                    {
                        Console.WriteLine($"下载完成：{_currentDownloadingMVM.Name}");
                        await PersistDownloadAsync(_currentDownloadingMVM);
                        _downloadManagers.Remove(_currentDownloadingMVM.Id);
                        _downloadPersistTicks.Remove(_currentDownloadingMVM.Id);
                        _currentDownloadingMVM =
                            MediaDownloadViewModels.FirstOrDefault(x => x.DownloadStatus == DownloadType.None);
                        if (_currentDownloadingMVM != null)
                        {
                            PreDownload(_currentDownloadingMVM);
                        }
                    }
                }
                else
                {
                    _currentDownloadingMVM = null;
                }
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            if (_currentDownloadingMVM is not null)
            {
                _currentDownloadingMVM.DownloadStatus = DownloadType.DownloadFailed;
                _currentDownloadingMVM.Status = StatusWord.DownloadFailed;
                _currentDownloadingMVM.ErrorMessage = exception.Message;
                await PersistDownloadAsync(_currentDownloadingMVM);
            }
        }
    }

    ~TVDownloadViewModel()
    {
        _downloadTimer.Stop();
    }

    public async Task AddMediaDownload(string name, string url, string folder = "", string source = "", string? cover = null)
    {
        if (MediaDownloadViewModels.Any(x => x.Url == url))
        {
            App.Notification?.Show(new Notification("警告", $"【{name} {url}】\n 已在下载队列", NotificationType.Warning),
                NotificationType.Warning);
            return;
        }

        if (!File.Exists(GlobalDefine.FFmpegPath))
        {
            App.Notification?.Show(new Notification("错误", "FFmpeg路径配置错误", NotificationType.Error),
                NotificationType.Error);
            return;
        }

        var id = 0;
        // 检查是否已存在
        var mediaDownload = await _mediaDownloadTable.GetSingleAsync(md => md.Url == url);
        if (mediaDownload != null)
        {
            var result =
                await MessageBox.ShowAsync($"是否重新下载 【{name}】 ?", "提示", MessageBoxIcon.Warning, MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            id = mediaDownload.Id;
        }

        // 添加到数据库
        var md = new MediaDownload
        {
            Id = id,
            Source = source,
            Name = name,
            Episode = string.Empty,
            Url = url,
            IsDownloaded = false,
            DownloadStatus = (int)DownloadType.None,
            Progress = 0,
            DownloadedBytes = 0,
            TotalBytes = 0,
            SizeText = "--/--",
            SpeedText = "0 Bps",
            RemainingTimeText = "--:--:--",
            ErrorMessage = null,
            OutputFilePath = null,
            Cover = cover,
            LocalPath = Path.Combine(GlobalDefine.DownloadPath, folder),
            CreateTime = id > 0 ? mediaDownload?.CreateTime ?? DateTime.Now : DateTime.Now,
            UpdateTime = DateTime.Now
        };
        if (id > 0)
        {
            await _mediaDownloadTable.UpdateAsync(md);
        }

        else
        {
            id = await _mediaDownloadTable.Context.Insertable(md).ExecuteReturnIdentityAsync();
        }

        // 添加到列表
        MediaDownloadViewModels.Add(new MediaDownloadViewModel
        {
            Id = id,
            Source = source,
            Name = name,
            Episode = string.Empty,
            Url = url,
            DownloadStatus = DownloadType.None,
            LocalPath = Path.Combine(GlobalDefine.DownloadPath, folder),
            Status = StatusWord.Unstarted,
            Progress = 0,
            SizeStr = "--/--",
            Speed = "0 Bps",
            RemainingTime = "--:--:--",
            ErrorMessage = null,
            DownloadedBytes = 0,
            TotalBytes = 0,
            OutputFilePath = null,
            Cover = cover,
            CreateTime = md.CreateTime,
            UpdateTime = DateTime.Now
        });

        AddDownloadCounts(DownloadType.None);
        RefreshFilteredDownloads();
    }

    private async void PreDownload(MediaDownloadViewModel mdvm)
    {
        mdvm.DownloadStatus = DownloadType.Downloading;
        _downloadManagers[mdvm.Id] = new DownloadManager();
        mdvm.Status = StatusWord.Downloading;
        mdvm.ErrorMessage = null;
        await PersistDownloadAsync(mdvm);
        Task.Run(async () => await _downloadManagers[mdvm.Id].DownloadAsync(mdvm.Url!, mdvm.LocalPath!, mdvm.Name!));
        WaitingCount -= 1;
        DownloadingCount += 1;
    }

    private void Downloading(MediaDownloadViewModel mdvm)
    {
        // 开始下载
        if (_downloadManagers[mdvm.Id].DownloadStatus.Count > 0)
        {
            var downloadStatus = _downloadManagers[mdvm.Id].DownloadStatus[0];
            mdvm.Speed = downloadStatus.speed;
            mdvm.SizeStr = downloadStatus.sizeStr;
            mdvm.Progress = downloadStatus.percentage;
            mdvm.RemainingTime = downloadStatus.remainingTimeStr;
            mdvm.DownloadedBytes = downloadStatus.size;
            mdvm.TotalBytes = downloadStatus.totalSize;
            mdvm.OutputFilePath = ResolveOutputFilePath(downloadStatus, mdvm);
            if (downloadStatus.downloadType != DownloadType.None)
            {
                mdvm.DownloadStatus = downloadStatus.downloadType;

                if (mdvm.DownloadStatus == DownloadType.Downloaded)
                {
                    DownloadingCount -= 1;
                    mdvm.Status = StatusWord.Downloaded;
                    mdvm.Progress = 100;
                }
                else if (mdvm.DownloadStatus == DownloadType.DownloadFailed)
                {
                    DownloadingCount -= 1;
                    mdvm.Status = StatusWord.DownloadFailed;
                    mdvm.ErrorMessage = StatusWord.DownloadFailed;
                }
            }

            if (mdvm.DownloadStatus == DownloadType.Downloading && ShouldPersistDownloadTick(mdvm.Id))
                _ = PersistDownloadAsync(mdvm);
            RefreshFilteredDownloads();
        }
    }

    private bool ShouldPersistDownloadTick(int id)
    {
        _downloadPersistTicks.TryGetValue(id, out var tick);
        tick++;
        _downloadPersistTicks[id] = tick;
        return tick == 1 || tick % 5 == 0;
    }

    private async Task PersistDownloadAsync(MediaDownloadViewModel mdvm)
    {
        if (mdvm.DownloadStatus == DownloadType.Downloaded)
        {
            mdvm.OutputFilePath = DownloadFileResolver.ResolveExistingFile(
                mdvm.OutputFilePath,
                mdvm.LocalPath,
                mdvm.Name,
                mdvm.Episode) ?? mdvm.OutputFilePath;
        }

        var mediaDownload = new MediaDownload
        {
            Id = mdvm.Id,
            Source = mdvm.Source,
            Name = mdvm.Name,
            Episode = mdvm.Episode,
            Url = mdvm.Url,
            LocalPath = mdvm.LocalPath,
            IsDownloaded = mdvm.DownloadStatus == DownloadType.Downloaded,
            DownloadStatus = (int)mdvm.DownloadStatus,
            Progress = mdvm.DownloadStatus == DownloadType.Downloaded ? 100 : mdvm.Progress,
            DownloadedBytes = mdvm.DownloadedBytes,
            TotalBytes = mdvm.TotalBytes,
            SizeText = mdvm.SizeStr,
            SpeedText = mdvm.Speed,
            RemainingTimeText = mdvm.RemainingTime,
            ErrorMessage = mdvm.ErrorMessage,
            OutputFilePath = mdvm.OutputFilePath,
            Cover = mdvm.Cover,
            CreateTime = mdvm.CreateTime,
            UpdateTime = DateTime.Now
        };

        await _mediaDownloadTable.UpdateAsync(mediaDownload);
    }

    private static string ToStatusWord(DownloadType downloadType)
    {
        return downloadType switch
        {
            DownloadType.Downloading => StatusWord.Downloading,
            DownloadType.Downloaded => StatusWord.Downloaded,
            DownloadType.DownloadFailed => StatusWord.DownloadFailed,
            _ => StatusWord.Unstarted
        };
    }

    private static string? ResolveOutputFilePath(DownloadStatus downloadStatus, MediaDownloadViewModel mediaDownload)
    {
        var resolvedFilePath = DownloadFileResolver.ResolveExistingFile(
            null,
            downloadStatus.saveDir,
            downloadStatus.name,
            mediaDownload.Episode);
        if (!string.IsNullOrWhiteSpace(resolvedFilePath)) return resolvedFilePath;

        if (string.IsNullOrWhiteSpace(downloadStatus.saveDir) || string.IsNullOrWhiteSpace(downloadStatus.name)) return mediaDownload.OutputFilePath;
        return Path.Combine(downloadStatus.saveDir, downloadStatus.name);
    }

    private void AddDownloadCounts(DownloadType downloadStatus)
    {
        TotalCount += 1;
        if (downloadStatus == DownloadType.None) WaitingCount += 1;
        else if (downloadStatus == DownloadType.Downloading) DownloadingCount += 1;
    }

    private void RemoveDownloadCounts(DownloadType downloadStatus)
    {
        if (TotalCount > 0) TotalCount -= 1;
        if (downloadStatus == DownloadType.None && WaitingCount > 0) WaitingCount -= 1;
        else if (downloadStatus == DownloadType.Downloading && DownloadingCount > 0) DownloadingCount -= 1;
    }

    [RelayCommand]
    private async Task DeleteDownloadTask(MediaDownloadViewModel? task)
    {
        if (task is null) return;
        await _mediaDownloadTable.DeleteByIdAsync(task.Id);
        _downloadManagers.Remove(task.Id);
        _downloadPersistTicks.Remove(task.Id);
        if (ReferenceEquals(_currentDownloadingMVM, task)) _currentDownloadingMVM = null;
        MediaDownloadViewModels.Remove(task);
        RemoveDownloadCounts(task.DownloadStatus);
        RefreshFilteredDownloads();
    }

    [RelayCommand]
    private async Task ClearCompletedTasks()
    {
        var completedTasks = MediaDownloadViewModels
            .Where(task => task.DownloadStatus == DownloadType.Downloaded)
            .ToList();
        foreach (var task in completedTasks)
        {
            await DeleteDownloadTask(task);
        }
    }

    [RelayCommand]
    private async Task ClearFailedTasks()
    {
        var failedTasks = MediaDownloadViewModels
            .Where(task => task.DownloadStatus == DownloadType.DownloadFailed)
            .ToList();
        foreach (var task in failedTasks)
        {
            await DeleteDownloadTask(task);
        }
    }

    [RelayCommand]
    private async Task DownloadAction()
    {
        if (string.IsNullOrEmpty(DownloadName) || string.IsNullOrEmpty(DownloadUrl))
        {
            App.Notification?.Show(new Notification("错误", "请输入下载名称和地址", NotificationType.Error),
                NotificationType.Error);
            return;
        }

        // 外部资源下载
        await AddMediaDownload(DownloadName, DownloadUrl);
    }
}

public partial class MediaDownloadViewModel : ObservableObject
{
    [ObservableProperty] private string? _localPath; // 本地地址
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _remainingTime = "--:--:--";
    [ObservableProperty] private string _sizeStr = "--/--";
    [ObservableProperty] private string _speed = "0 Bps";
    [ObservableProperty] private string? _status = StatusWord.Unstarted; // 状态 未开始/下载中/下载失败/已完成
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _outputFilePath;
    public DownloadType DownloadStatus { get; set; } = DownloadType.None; // 下载状态
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }

    public int Id { get; set; }
    public string? Source { get; set; }
    public string? Name { get; set; } //电影名
    public string? Episode { get; set; } //剧集
    public string? Url { get; set; } //播放地址
    public string? Cover { get; set; }
    public DateTime CreateTime { get; set; } = DateTime.Now;
    public DateTime UpdateTime { get; set; }

    [RelayCommand]
    public void Play()
    {
        var filePath = DownloadFileResolver.ResolveExistingFile(OutputFilePath, LocalPath, Name, Episode);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            App.Notification?.Show(new Notification("错误", "下载文件不存在，无法播放", NotificationType.Error),
                NotificationType.Error);
            return;
        }

        OutputFilePath = filePath;

        var win = new MpvPlayerWindow();
        (App.VisualRoot as MainWindow)?.Hide();
        win.Show();
        if (win.DataContext is MpvPlayerWindowModel videoModel)
        {
            var title = string.IsNullOrWhiteSpace(Name) ? Path.GetFileName(filePath) : Name;
            var episode = string.IsNullOrWhiteSpace(Episode) ? title : Episode;
            videoModel.MediaUrl = filePath;
            videoModel.Title = MpvPlayerWindowModel.BuildPlayerTitle(title, episode);
            videoModel.Episodes = new ObservableCollection<EpisodeSubjectItem>
            {
                new()
                {
                    Name = episode,
                    Url = filePath,
                    OutputFilePath = filePath,
                    IsDownloaded = true,
                    Watched = true
                }
            };
            videoModel.ViewHistory = new ViewHistory
            {
                VodId = filePath,
                Name = title,
                Episode = episode,
                Url = filePath,
                Source = string.IsNullOrWhiteSpace(Source) ? "下载" : Source,
                Cover = Cover,
                PlaybackPosition = 0,
                Duration = 0,
                TotalEpisodeCount = 1,
                IsLocal = true
            };
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (string.IsNullOrEmpty(LocalPath)) return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", LocalPath);
            }
            else
            {
                Process.Start("open", LocalPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}

public static class DownloadFileResolver
{
    private static readonly string[] PlayableExtensions =
    [
        ".mp4", ".mkv", ".ts", ".m4v", ".mov", ".avi", ".flv", ".wmv", ".webm", ".m4a", ".mp3", ".aac"
    ];

    public static string? ResolveExistingFile(string? outputFilePath, string? localPath, string? name, string? episode)
    {
        if (IsPlayableFile(outputFilePath)) return outputFilePath;
        if (IsPlayableFile(localPath)) return localPath;

        var directory = ResolveDirectory(outputFilePath, localPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsPlayableFile)
            .Select(file => new FileInfo(file))
            .OrderByDescending(file => Score(file.Name, name, episode))
            .ThenByDescending(file => file.LastWriteTime)
            .ToList();

        return files.FirstOrDefault()?.FullName;
    }

    private static string? ResolveDirectory(string? outputFilePath, string? localPath)
    {
        if (!string.IsNullOrWhiteSpace(localPath) && Directory.Exists(localPath)) return localPath;
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        {
            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(directory)) return directory;
        }

        return null;
    }

    private static bool IsPlayableFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && File.Exists(path)
               && PlayableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static int Score(string fileName, string? name, string? episode)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(name) && fileName.Contains(name, StringComparison.OrdinalIgnoreCase)) score += 2;
        if (!string.IsNullOrWhiteSpace(episode) && fileName.Contains(episode, StringComparison.OrdinalIgnoreCase)) score += 3;
        return score;
    }
}

internal static class StatusWord
{
    public const string Unstarted = "未开始";
    public const string Downloading = "下载中";
    public const string Downloaded = "已完成";
    public const string DownloadFailed = "下载失败";
}