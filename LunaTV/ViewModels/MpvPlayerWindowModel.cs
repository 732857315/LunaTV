using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;
using LunaTV.Constants;
using LunaTV.ViewModels.Base;
using LunaTV.ViewModels.TVShowPages;
using LunaTV.Views;
using LunaTV.Views.Media;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace LunaTV.ViewModels;

public class SpeedMenuItemViewModel
{
    public string? Header { get; set; }
    public ICommand? Command { get; set; }
    public double CommandParameter { get; set; }
}

public partial class MpvPlayerWindowModel : ViewModelBase, IDisposable
{
    public static string BuildPlayerTitle(string? title, string? episode)
    {
        if (string.IsNullOrWhiteSpace(title)) return episode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(episode)) return title;
        return string.Equals(title, episode, StringComparison.OrdinalIgnoreCase) ? title : $"{title} {episode}";
    }

    private readonly LoadingWaitViewModel _loadingWaitViewModel = new();

    private bool _disposed;
    [ObservableProperty] private TimeSpan _duration = TimeSpan.FromSeconds(1);

    // /// <summary>
    // /// Occurs after the media player is initialized.
    // /// </summary>
    // public event EventHandler? MediaPlayerInitialized;
    private bool _isLoaded;
    [ObservableProperty] private bool _isMediaLoaded;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isPlaying;
    private bool _isSettingPosition;
    private DateTime _lastPositionUpdateTime = DateTime.MinValue;
    private double _lastPositionValue;
    [ObservableProperty] private bool _isVideosKanbanChecked;
    [ObservableProperty] private int _kanBanWidth;
    [ObservableProperty] private bool _loop; //循环播放
    [ObservableProperty] private string _mediaUrl = "https://vip.dytt-luck.com/20250827/19457_e0c4ac2b/index.m3u8";
    [ObservableProperty] private TimeSpan _position = TimeSpan.Zero;
    [ObservableProperty] private double _speed = 1.0f; //0.5,1.0,1.5,2.0,2.5,3.0,3.5,4.0
    [ObservableProperty] private string _speedText = "1x";

    private PlaybackStatus _status;
    [ObservableProperty] private string? _title = "无";
    [ObservableProperty] private int _volume = 70;

    public MpvPlayerWindowModel()
    {
        for (var i = 8; i >= 1; i--)
        {
            SpeedMenuItems.Add(
                new SpeedMenuItemViewModel
                {
                    Header = $"{i * 0.5}x",
                    Command = SpeedChangeCommand,
                    CommandParameter = i * 0.5
                }
            );
        }

        DbServiceInit();
    }

    public Window? Window { get; set; }

    public IList<SpeedMenuItemViewModel> SpeedMenuItems { get; } = new List<SpeedMenuItemViewModel>();

    public MpvContext Mpv { get; set; } = default!;
    public WindowNotificationManager? Notification { get; set; }

    private static void IgnoreUnavailableProperty(Action action)
    {
        try
        {
            action();
        }
        catch (MpvException e) when (e.Message.Contains("property unavailable", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static async Task IgnoreUnavailablePropertyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (MpvException e) when (e.Message.Contains("property unavailable", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static bool IsNetworkMedia(string? mediaUrl)
    {
        return Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    }

    private static string GetReferrer(string mediaUrl)
    {
        var uri = new Uri(mediaUrl);
        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }

    private async Task ConfigureNetworkPlaybackAsync(string mediaUrl)
    {
        if (!IsNetworkMedia(mediaUrl)) return;

        var options = new MpvAsyncOptions { WaitForResponse = false };
        await IgnoreUnavailablePropertyAsync(() => { Mpv.SetOptionString("cache-secs", "120"); return Task.CompletedTask; });
        await Task.WhenAll(
            IgnoreUnavailablePropertyAsync(() => Mpv.UserAgent.SetAsync(LunaTV.Base.Constants.UserAgent.GetRandomUserAgent(), options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.Referrer.SetAsync(GetReferrer(mediaUrl), options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.NetworkTimeout.SetAsync(Math.Max(5, AppConifg.PlayerConfig.Timeout / 1000.0), options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.Cache.SetAsync(true, options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.CachePause.SetAsync(true, options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.CachePauseInitial.SetAsync(true, options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.CachePauseWait.SetAsync(5, options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.DemuxerReadAheadSecs.SetAsync(20, options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.DemuxerMaxBytes.SetAsync(150 * 1024 * 1024, options)),
            IgnoreUnavailablePropertyAsync(() => Mpv.DemuxerMaxBackBytes.SetAsync(30 * 1024 * 1024, options)));
    }

    private string GetPlaybackErrorText()
    {
        var episode = string.IsNullOrWhiteSpace(ViewHistory?.Episode) ? Title : ViewHistory.Episode;
        return string.IsNullOrWhiteSpace(episode) ? "当前视频无法播放" : $"{episode} 无法播放";
    }

    /// <summary>
    ///     Gets or sets whether the user is dragging the seek bar.
    /// </summary>
    public bool IsSeekBarPressed { get; set; }

    public PlaybackStatus Status
    {
        get => _status;
        protected set
        {
            SetProperty(ref _status, value);
            var text = _status switch
            {
                PlaybackStatus.Loading => "Loading...",
                PlaybackStatus.Playing => ViewHistory != null ? $"{ViewHistory.Name}-{ViewHistory.Episode}" : Title,
                PlaybackStatus.Error => GetPlaybackErrorText(),
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(text)) return;

            var notificationType = _status == PlaybackStatus.Error ? NotificationType.Warning : NotificationType.Information;
            var title = _status == PlaybackStatus.Error ? "播放失败" : "播放信息";
            Notification?.Show(new Notification(title, text), notificationType);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task OnWindowLoaded()
    {
        await Task.Delay(100); // Fails to load if we don't give a slight delay.

        Mpv!.FileLoaded += PlayerFileLoaded;
        Mpv.EndFile += PlayerEndFile;
        Mpv.TimePos.Changed += PlayerPositionChanged;

        var options = new MpvAsyncOptions { WaitForResponse = false };
        await IgnoreUnavailablePropertyAsync(() => Mpv.Volume.SetAsync(Volume, options));
        await IgnoreUnavailablePropertyAsync(() => Mpv.Speed.SetAsync(Speed, options));
        await IgnoreUnavailablePropertyAsync(() => Mpv.LoopFile.SetAsync(Loop ? "yes" : "no", options));
    }

    public async Task PlayPause()
    {
        if (string.IsNullOrEmpty(MediaUrl) || Design.IsDesignMode)
        {
            return;
        }

        if (!_isLoaded)
        {
            await IgnoreUnavailablePropertyAsync(() => Mpv!.Stop().InvokeAsync());
            await IgnoreUnavailablePropertyAsync(() => Mpv.Pause.SetAsync(false));
            if (!string.IsNullOrEmpty(MediaUrl))
            {
                _ = Loading();
                try
                {
                    await ConfigureNetworkPlaybackAsync(MediaUrl!);
                    await Mpv.LoadFile(MediaUrl!).InvokeAsync();
                    IsPlaying = true;
                    _isLoaded = true;
                    MediaPlayerOnLoaded();
                }
                catch (Exception)
                {
                    _loadingWaitViewModel.Close();
                    IsPlaying = false;
                    Status = PlaybackStatus.Error;
                }
            }
            else
            {
                IsPlaying = false;
            }
        }
        else
        {
            if (IsPlaying) FlushPendingPosition();
            await IgnoreUnavailablePropertyAsync(() => Mpv!.Pause.SetAsync(IsPlaying));
            IsPlaying = !IsPlaying;
        }
    }

    private void Stoped()
    {
        FlushPendingPosition();
        SaveViewHistory();

        if (string.IsNullOrEmpty(MediaUrl) && !IsMediaLoaded)
        {
            return;
        }

        MediaUrl = string.Empty;
        _isLoaded = false;
        IsPlaying = false;
        Status = PlaybackStatus.Stopped;
        IsMediaLoaded = false;
        Duration = TimeSpan.FromSeconds(1);
        Position = TimeSpan.Zero;
    }

    public void Stop()
    {
        IgnoreUnavailableProperty(() => Mpv.Pause.Set(false));
        IgnoreUnavailableProperty(() => Mpv!.Stop().Invoke());
        SpeedChange(1.0f);
        Stoped();
    }

    public void Seek(int seconds)
    {
        if (!IsMediaLoaded)
        {
            return;
        }

        var newPos = Position.Add(TimeSpan.FromSeconds(seconds));
        if (newPos < TimeSpan.Zero)
        {
            newPos = TimeSpan.Zero;
        }
        else if (newPos > Duration)
        {
            newPos = Duration;
        }

        if (newPos != Position)
        {
            // Position = newPos;
            lock (Mpv)
            {
                IgnoreUnavailableProperty(() => Mpv.TimePos.Set(newPos.TotalSeconds));
            }
        }
    }

    public void ChangeVolume(int value)
    {
        var newVolume = Volume + value;
        if (newVolume < 0)
        {
            newVolume = 0;
        }
        else if (newVolume > 100)
        {
            newVolume = 100;
        }

        Volume = newVolume;
    }

    [RelayCommand]
    private void GoHead()
    {
        Position = TimeSpan.Zero;
        if (IsMediaLoaded)
        {
            lock (Mpv!)
            {
                IgnoreUnavailableProperty(() => Mpv.TimePos.Set(0));
            }
        }
    }

    [RelayCommand]
    private void GoTail()
    {
        Position = TimeSpan.FromSeconds(Duration.TotalSeconds - 1);
        if (IsMediaLoaded)
        {
            lock (Mpv!)
            {
                // var pos = TimeSpan.FromTicks(Position.Ticks);
                IgnoreUnavailableProperty(() => Mpv.TimePos.Set(Position.TotalSeconds));
            }
        }
    }

    [RelayCommand]
    private void Screenshot()
    {
        if (IsMediaLoaded)
        {
            var path = Path.Combine(GlobalDefine.DownloadPath);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            try
            {
                Mpv.ScreenshotToFile(Path.Combine(path, $"{DateTime.Now:yyyyMMddHHmmssfff}.png"))
                    .Invoke();
                Notification?.Show(new Notification("截图已保存到", path),
                    NotificationType.Information);
            }
            catch (MpvException)
            {
                Notification?.Show(new Notification("截图失败", "当前视频状态暂时不能截图"), NotificationType.Warning);
            }
        }
    }

    [RelayCommand]
    private void Mute()
    {
        IsMuted = !IsMuted;
        SaveMute();
    }

    [RelayCommand]
    private void ExitFullScreen()
    {
        if (Window is not null)
        {
            Window.WindowState = WindowState.Maximized;
        }
    }

    [RelayCommand]
    private void SpeedChange(double value)
    {
        SpeedText = $"{value}x";
        Speed = value;
    }

    [RelayCommand]
    private void KanbanSelect(EpisodeSubjectItem item)
    {
        Stop();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsVideosKanbanChecked = false;
            KanBanWidth = 0;
            if (ViewHistory is not null)
            {
                ViewHistory.PlaybackPosition = 0;
                ViewHistory.Episode = item.Name;
                ViewHistory.Url = item.Url;
            }

            MediaUrl = item.Url;
            Title = BuildPlayerTitle(ViewHistory?.Name, item.Name);
            Episodes.ToList().ForEach(episode => episode.Watched = episode.Name == item.Name);
            SaveViewHistory();
        });
    }

    private void PlayerFileLoaded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _loadingWaitViewModel.Close();

            Status = PlaybackStatus.Playing;
            try
            {
                Duration = TimeSpan.FromSeconds(Mpv!.Duration.Get()!.Value);
            }
            catch (MpvException e) when (e.Message.Contains("property unavailable", StringComparison.OrdinalIgnoreCase))
            {
                Duration = TimeSpan.FromSeconds(1);
            }

            IsMediaLoaded = true;
            if (Duration > TimeSpan.FromSeconds(1))
            {
                lock (Mpv!)
                {
                    IgnoreUnavailableProperty(() => Mpv.TimePos.Set(ViewHistory?.PlaybackPosition ?? 0));
                }

                SaveViewHistory();
            }
            else
            {
                SetPositionNoSeek(TimeSpan.Zero);
            }
        });
    }

    private void PlayerEndFile(object? sender, MpvEndFileEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Reason == MpvEndFileReason.Error)
            {
                Status = PlaybackStatus.Error;
                _loadingWaitViewModel.Close();
                IsPlaying = false;
                _isLoaded = false;
                IsMediaLoaded = false;
                return;
            }

            Stop();

            MediaPlayerOnEndReached();
        });
    }

    /// MPV播放刷新进度条
    private void PlayerPositionChanged(object? sender, MpvValueChangedEventArgs<double, double> e)
    {
        _lastPositionValue = e.NewValue!.Value;
        var now = DateTime.UtcNow;
        if ((now - _lastPositionUpdateTime).TotalMilliseconds >= 150)
        {
            _lastPositionUpdateTime = now;
            var pos = TimeSpan.FromSeconds(_lastPositionValue);
            Dispatcher.UIThread.Post(() => SetPositionNoSeek(pos));
        }
    }

    /// <summary>
    ///     Immediately dispatches the last recorded position to the UI,
    ///     bypassing the throttle. Call when pausing or stopping.
    /// </summary>
    private void FlushPendingPosition()
    {
        var pos = TimeSpan.FromSeconds(_lastPositionValue);
        Dispatcher.UIThread.Post(() => SetPositionNoSeek(pos));
    }


    /// <summary>
    ///     Sets the position without raising PositionChanged.
    /// </summary>
    /// <param name="pos">The position value to set.</param>
    private void SetPositionNoSeek(TimeSpan pos)
    {
        if (!IsSeekBarPressed)
        {
            _isSettingPosition = true; //不要更新播放进度
            Position = pos;
            _isSettingPosition = false; //更新播放进度
        }
    }

    partial void OnMediaUrlChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            // bug：会触发数据库重复添加
            // Stop();
            // Title = "无";
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(async () => { await PlayPause(); });
        }
    }

    partial void OnPositionChanged(TimeSpan value)
    {
        if (IsSeekBarPressed && IsMediaLoaded && !_isSettingPosition)
        {
            lock (Mpv!)
            {
                var pos = TimeSpan.FromTicks(Math.Max(0, Math.Min(Duration.Ticks, value.Ticks)));
                IgnoreUnavailableProperty(() => Mpv.TimePos.Set(pos.TotalSeconds));
            }
        }
    }

    partial void OnVolumeChanged(int value)
    {
        if (Mpv is not null) IgnoreUnavailableProperty(() => Mpv.Volume.Set(value));
        SaveVolume();
    }

    partial void OnSpeedChanged(double value)
    {
        if (Mpv is not null) IgnoreUnavailableProperty(() => Mpv.Speed.Set(value));
    }

    partial void OnLoopChanged(bool value)
    {
        if (Mpv is not null) IgnoreUnavailableProperty(() => Mpv.LoopFile.Set(value ? "yes" : "no"));
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (Mpv is not null) IgnoreUnavailableProperty(() => Mpv.Mute.Set(value));
    }

    partial void OnIsVideosKanbanCheckedChanged(bool value)
    {
        KanBanWidth = value ? 300 : 0;
    }

    public async Task Loading()
    {
        var options = new DialogOptions
        {
            Title = "",
            Mode = DialogMode.None,
            Button = DialogButton.None,
            ShowInTaskBar = false,
            IsCloseButtonVisible = true,
            StartupLocation = WindowStartupLocation.CenterScreen,
            CanDragMove = true,
            CanResize = false,
            StyleClass = ""
        };

        _loadingWaitViewModel.TimerStart();

        await Dialog.ShowModal<LoadingWaitView, LoadingWaitViewModel>(_loadingWaitViewModel, Window, options);
    }

    /// <summary>
    ///     Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="disposing">
    ///     The disposing parameter should be false when called from a finalizer, and true when called from
    ///     the IDisposable.Dispose method.
    /// </param>
    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Managed resources.
            }

            // Unmanaged resources.
            Mpv?.Dispose();

            _disposed = true;
        }
    }
}