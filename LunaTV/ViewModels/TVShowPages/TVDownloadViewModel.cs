using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaTV.Base.DB.UnitOfWork;
using LunaTV.Base.Models;
using LunaTV.Constants;
using LunaTV.ViewModels.Base;
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
    private MediaDownloadViewModel? _currentDownloadingMVM;

    [ObservableProperty] private int _downloadingCount;
    [ObservableProperty] private string _downloadUrl = "https://vod.360zyx.vip/20250708/7T2xjBRd/index.m3u8";
    [ObservableProperty] private string _downloadName = "曼达洛人";
    public ObservableCollection<MediaDownloadViewModel> MediaDownloadViewModels { get; set; }

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _waitingCount;
    private bool _isInitialized;

    public TVDownloadViewModel()
    {
        _mediaDownloadTable = App.Services.GetRequiredService<SugarRepository<MediaDownload>>();
        MediaDownloadViewModels = new ObservableCollection<MediaDownloadViewModel>();
        _downloadTimer = new DispatcherTimer
            (TimeSpan.FromSeconds(1), DispatcherPriority.Background, DownloadTimerOnTick);
        _downloadTimer.Start();
        Dispatcher.UIThread.InvokeAsync(async () => { await LoadUnDownloadFromDBAsync(); });
    }

    // 从数据库加载下载任务
    private async Task LoadUnDownloadFromDBAsync()
    {
        var unDownloads = await _mediaDownloadTable.GetListAsync(x => !x.IsDownloaded);
        foreach (var downloadTask in unDownloads)
        {
            if (MediaDownloadViewModels.Any(x => x.Url == downloadTask.Url) != true)
            {
                MediaDownloadViewModels.Add(new MediaDownloadViewModel
                {
                    Id = downloadTask.Id,
                    Name = downloadTask.Name,
                    Episode = downloadTask.Episode,
                    Url = downloadTask.Url,
                    DownloadStatus = DownloadType.None,
                    LocalPath = downloadTask.LocalPath,
                    Status = StatusWord.Unstarted,
                    Progress = 0,
                    UpdateTime = downloadTask.UpdateTime
                });
                WaitingCount += 1;
                TotalCount += 1;
            }
        }

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
                        var md = new MediaDownload
                        {
                            Id = _currentDownloadingMVM.Id,
                            Source = string.Empty,
                            Name = _currentDownloadingMVM.Name,
                            Episode = _currentDownloadingMVM.Episode,
                            Url = _currentDownloadingMVM.Url,
                            IsDownloaded = true,
                            LocalPath = _currentDownloadingMVM.LocalPath
                        };
                        await _mediaDownloadTable.UpdateAsync(md);
                        _downloadManagers.Remove(_currentDownloadingMVM.Id);
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
        }
    }

    ~TVDownloadViewModel()
    {
        _downloadTimer.Stop();
    }

    public async Task AddMediaDownload(string name, string url)
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
                await OverlayMessageBox.ShowAsync($"是否重新下载 【{name}】 ?", "提示", icon: MessageBoxIcon.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            id = mediaDownload.Id;
        }

        // 添加到数据库
        var md = new MediaDownload
        {
            Id = id,
            Source = string.Empty,
            Name = name,
            Episode = string.Empty,
            Url = url,
            IsDownloaded = false,
            LocalPath = Path.Combine(GlobalDefine.DownloadPath, name)
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
            Name = name,
            Episode = string.Empty,
            Url = url,
            DownloadStatus = DownloadType.None,
            LocalPath = Path.Combine(GlobalDefine.DownloadPath, name),
            Status = StatusWord.Unstarted,
            Progress = 0,
            UpdateTime = DateTime.Now
        });

        TotalCount += 1;
        WaitingCount += 1;
    }

    private void PreDownload(MediaDownloadViewModel mdvm)
    {
        mdvm.DownloadStatus = DownloadType.Downloading;
        _downloadManagers[mdvm.Id] = new DownloadManager();
        mdvm.Status = StatusWord.Downloading;
        Task.Run(async () => await _downloadManagers[mdvm.Id].DownloadAsync(mdvm.Url!, mdvm.LocalPath!, mdvm.Name!));
        WaitingCount -= 1;
        DownloadingCount += 1;
    }

    private void Downloading(MediaDownloadViewModel mdvm)
    {
        // 开始下载
        if (_downloadManagers[mdvm.Id].DownloadStatus.Count > 0)
        {
            mdvm.Speed = _downloadManagers[mdvm.Id].DownloadStatus[0].speed;
            mdvm.SizeStr = _downloadManagers[mdvm.Id].DownloadStatus[0].sizeStr;
            mdvm.Progress = (int)_downloadManagers[mdvm.Id].DownloadStatus[0].percentage;
            mdvm.RemainingTime = _downloadManagers[mdvm.Id].DownloadStatus[0].remainingTimeStr;
            if (_downloadManagers[mdvm.Id].DownloadStatus[0].downloadType != DownloadType.None)
            {
                mdvm.DownloadStatus = _downloadManagers[mdvm.Id].DownloadStatus[0].downloadType;

                if (mdvm.DownloadStatus == DownloadType.Downloaded)
                {
                    DownloadingCount -= 1;
                    mdvm.Status = StatusWord.Downloaded;
                }
                else if (mdvm.DownloadStatus == DownloadType.DownloadFailed)
                {
                    DownloadingCount -= 1;
                    mdvm.Status = StatusWord.DownloadFailed;
                }
            }
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
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string? _status = StatusWord.Unstarted; // 状态 未开始/下载中/下载失败/已完成
    [ObservableProperty] private string _remainingTime = "--:--:--";
    [ObservableProperty] private string _sizeStr = "--:--/--:--";
    [ObservableProperty] private string _speed = "0:00MBps";
    public DownloadType DownloadStatus { get; set; } = DownloadType.None; // 下载状态 

    public int Id { get; set; }
    public string? Name { get; set; } //电影名
    public string? Episode { get; set; } //剧集
    public string? Url { get; set; } //播放地址
    public DateTime UpdateTime { get; set; }

    [RelayCommand]
    public void OpenFolder()
    {
        if (string.IsNullOrEmpty(LocalPath)) return;

        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", LocalPath);
            else
                Process.Start("open", LocalPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}

internal static class StatusWord
{
    public const string Unstarted = "未开始";
    public const string Downloading = "下载中";
    public const string Downloaded = "已完成";
    public const string DownloadFailed = "下载失败";
}