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
using M3U8Download;
using Microsoft.Extensions.DependencyInjection;
using N_m3u8DL_RE.Util;
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

    public TVDownloadViewModel()
    {
        _mediaDownloadTable = App.Services.GetRequiredService<SugarRepository<MediaDownload>>();
        MediaDownloadViewModels = new ObservableCollection<MediaDownloadViewModel>();
        _downloadTimer = new DispatcherTimer
            (TimeSpan.FromSeconds(1), DispatcherPriority.Background, DownloadTimerOnTick);
        _downloadTimer.Start();
    }

    private async void DownloadTimerOnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_currentDownloadingMVM is null)
            {
                if (MediaDownloadViewModels.Count > 0)
                {
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
                    // 刷新下载进度
                    Downloading(_currentDownloadingMVM);
                }
                else
                {
                    _currentDownloadingMVM =
                        MediaDownloadViewModels.FirstOrDefault(x => x.DownloadStatus == DownloadType.None);
                    if (_currentDownloadingMVM != null)
                    {
                        PreDownload(_currentDownloadingMVM);
                    }
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

    public async Task AddMediaDownload(string name, string url, bool check = true)
    {
        if (!File.Exists(GlobalDefine.FFmpegPath))
        {
            App.Notification?.Show(new Notification("错误", "FFmpeg路径配置错误", NotificationType.Error),
                NotificationType.Error);
            return;
        }

        var isDownloaded = false;
        var id = 0;
        // 检查是否已存在
        if (check)
        {
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
                isDownloaded = mediaDownload.IsDownloaded;
            }
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
            LocalPath = GlobalDefine.DownloadPath
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
            ActionIndicate = false,
            ActionText = isDownloaded ? "重新下载" : "开始",
            Status = "未开始",
            StatusIndicate = false,
            Progress = 0
        });

        TotalCount += 1;
        WaitingCount += 1;
    }

    private void PreDownload(MediaDownloadViewModel mdvm)
    {
        mdvm.DownloadStatus = DownloadType.Downloading;
        _downloadManagers[mdvm.Id] = new DownloadManager();
        Task.Run(async () => await _downloadManagers[mdvm.Id].DownloadAsync(mdvm.Url!, mdvm.LocalPath!, mdvm.Name!));
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
        }
    }

    [RelayCommand]
    private async Task DownloadAction()
    {
        // 外部资源下载
        await AddMediaDownload(OtherUtil.GetFileNameFromInput(DownloadUrl), DownloadUrl);
    }
}

public partial class MediaDownloadViewModel : ObservableObject
{
    [ObservableProperty] private bool _actionIndicate; // 动作指示
    [ObservableProperty] private string? _actionText = "开始"; // 状态 开始/暂停/重新下载
    [ObservableProperty] private string? _localPath; // 本地地址
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string? _status = "未开始";
    [ObservableProperty] private bool _statusIndicate; // 状态指示
    [ObservableProperty] private string _remainingTime = "--:--:--";
    [ObservableProperty] private string _sizeStr = "--:--/--:--";
    [ObservableProperty] private string _speed = "0:00MBps";
    public int Id { get; set; }
    public string? Name { get; set; } //电影名
    public string? Episode { get; set; } //剧集
    public string? Url { get; set; } //播放地址
    public DownloadType DownloadStatus { get; set; } // 下载状态
    public DateTime UpdateTime { get; set; }

    partial void OnActionTextChanged(string? value)
    {
        Status = value switch
        {
            "开始" => "未开始",
            "暂停" => "下载中",
            "重新下载" => "已完成",
            _ => "未开始"
        };

        DownloadStatus = Status == "已完成" ? DownloadType.Downloaded : DownloadType.DownloadFailed;
        ActionIndicate = Status != "未开始";
        StatusIndicate = Status == "下载中";
    }

    [RelayCommand]
    private void DownloadAction()
    {
        ActionText = ActionText switch
        {
            "开始" => "暂停",
            "暂停" => "重新下载",
            "重新下载" => "开始",
            _ => "开始"
        };
    }

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