using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LunaTV.LibMpv2;
using LunaTV.LibMpv2.LibMpvDynamic;
using LunaTV.LibMPV2.LibMpvDynamic;

namespace LunaTV.LibMPV2;

public class VideoPlayerControl : UserControl
{
    public static readonly StyledProperty<Control?> PlayerContentProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Control?>(nameof(PlayerContent));

    public static readonly StyledProperty<double> VolumeProperty =
        AvaloniaProperty.Register<VideoPlayerControl, double>(nameof(Volume), 100);

    /// <summary>
    ///     Video position in seconds.
    /// </summary>
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<VideoPlayerControl, double>(nameof(Position));

    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<VideoPlayerControl, double>(nameof(Duration));

    public static readonly StyledProperty<string> ProgressTextProperty =
        AvaloniaProperty.Register<VideoPlayerControl, string>(nameof(ProgressText), default!);

    public static readonly StyledProperty<ICommand> PlayCommandProperty =
        AvaloniaProperty.Register<VideoPlayerControl, ICommand>(nameof(PlayCommand));

    public static readonly StyledProperty<ICommand> StopCommandProperty =
        AvaloniaProperty.Register<VideoPlayerControl, ICommand>(nameof(StopCommand));

    public static readonly StyledProperty<ICommand> FullScreenCommandProperty =
        AvaloniaProperty.Register<VideoPlayerControl, ICommand>(nameof(FullScreenCommand));

    public static readonly StyledProperty<bool> StopIsVisibleProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(StopIsVisible));

    public static readonly StyledProperty<bool> FullScreenIsVisibleProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(FullScreenIsVisible));

    private readonly Button _buttonFullScreen;
    private readonly Button _buttonFullScreenCollapse;
    private readonly Button _buttonPlay;
    private readonly ContentPresenter? _contentPresenter;
    private readonly Grid _gridProgress; // Reference to the controls grid
    private readonly TextBlock _iconVolume;
    private readonly TextBlock _textBlockPlayerName;

    private readonly TextBlock _textBlockVideoFileName;
    private readonly double _volumeIgnore = -1;
    private DispatcherTimer? _autoHideTimer;

    private bool _isFullScreen;
    private DateTime _lastActivityTime;

    private double _positionIgnore = -1;
    private DispatcherTimer? _positionTimer;
    private int _slowPollCounter;
    private bool _surfaceLeftButtonDown;
    private string _videoFileName;

    public VideoPlayerControl(IVideoPlayer videoPlayerInstance)
    {
        VideoPlayer = videoPlayerInstance;
        _videoFileName = string.Empty;
        _lastActivityTime = DateTime.UtcNow;

        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"), // video + controls
            Background = Brushes.Transparent // Enable hit testing for pointer events
        };

        // PlayerContent
        var contentPresenter = new ContentPresenter
        {
            [!ContentPresenter.ContentProperty] = this[!PlayerContentProperty],
            Background = new SolidColorBrush(Colors.Black)
        };
        _contentPresenter = contentPresenter;
        mainGrid.Children.Add(contentPresenter);
        Grid.SetRow(contentPresenter, 0);

        // Row with buttons + position slider + volume slider
        _gridProgress = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(10, 4)
        };
        Grid.SetRow(_gridProgress, 1);
        mainGrid.Children.Add(_gridProgress);

        // Attach a tunnel handler so we see clicks even if child handles them.
        mainGrid.AddHandler(PointerPressedEvent, OnMainGridPointerPressed, RoutingStrategies.Tunnel, true);
        // Release handler is on `this` (not mainGrid) so it still fires when the pointer
        // is captured to this control — routing wouldn't reach mainGrid in that case.
        AddHandler(PointerReleasedEvent, OnMainGridPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            true);

        // Buttons
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Play
        _buttonPlay = new Button
        {
            Margin = new Thickness(0, 0, 3, 0),
            [AutomationProperties.NameProperty] = "Play"
        };
        // Attached.SetIcon(_buttonPlay, "fa-solid fa-play");
        _buttonPlay.Click += (_, _) =>
        {
            VideoPlayer.PlayOrPause();
            PlayPauseRequested?.Invoke();
        };
        _buttonPlay.Bind(Button.CommandProperty, new Binding
        {
            Path = nameof(PlayCommand),
            Source = this
        });

        stackPanel.Children.Add(_buttonPlay);

        // Stop
        var buttonStop = new Button
        {
            Margin = new Thickness(0, 0, 3, 0),
            [AutomationProperties.NameProperty] = "Stop"
        };
        buttonStop.Bind(IsVisibleProperty, new Binding
        {
            Path = nameof(StopIsVisible),
            Source = this
        });
        // Attached.SetIcon(buttonStop, "fa-solid fa-stop");
        buttonStop.Click += (_, _) =>
        {
            VideoPlayer.Stop();
            StopRequested?.Invoke();
        };

        stackPanel.Children.Add(buttonStop);
        buttonStop.Bind(Button.CommandProperty, new Binding
        {
            Path = nameof(StopCommand),
            Source = this
        });

        // Fullscreen
        _buttonFullScreen = new Button
        {
            Margin = new Thickness(0, 0, 3, 0),
            [AutomationProperties.NameProperty] = "FullScreen"
        };
        _buttonFullScreen.Bind(IsVisibleProperty, new Binding
        {
            Path = nameof(FullScreenIsVisible),
            Source = this
        });
        // Attached.SetIcon(_buttonFullScreen, "fa-solid fa-expand");
        _buttonFullScreen.Click += (_, _) => FullscreenRequested?.Invoke();

        stackPanel.Children.Add(_buttonFullScreen);
        _buttonFullScreen.Bind(Button.CommandProperty, new Binding
        {
            Path = nameof(FullScreenCommand),
            Source = this
        });


        _buttonFullScreenCollapse = new Button
        {
            Margin = new Thickness(0, 0, 3, 0),
            IsVisible = false,
            [AutomationProperties.NameProperty] = "ExitFullScreen"
        };
        // Attached.SetIcon(_buttonFullScreenCollapse, "fa-solid fa-compress");
        _buttonFullScreenCollapse.Click += (_, _) => FullscreenCollapseRequested?.Invoke();

        stackPanel.Children.Add(_buttonFullScreenCollapse);

        _gridProgress.Children.Add(stackPanel);
        Grid.SetColumn(stackPanel, 0);

        var sliderPosition = new Slider
        {
            Minimum = 0,
            Margin = new Thickness(2, 0, 0, 0),
            [AutomationProperties.NameProperty] = "VideoPosition"
        };

        sliderPosition.TemplateApplied += (s, e) =>
        {
            if (e.NameScope.Find<Thumb>("thumb") is Thumb thumb)
            {
                thumb.Width = 14;
                thumb.Height = 14;
            }
        };

        sliderPosition.Bind(RangeBase.MaximumProperty, this.GetObservable(DurationProperty));
        sliderPosition.Bind(RangeBase.ValueProperty, this.GetObservable(PositionProperty));

        // Also ensure the control can receive keyboard focus
        sliderPosition.Focusable = true;

        var sliderPositionUserMoving = false;
        sliderPosition.AddHandler(PointerPressedEvent, (_, _) => sliderPositionUserMoving = true,
            RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(PointerReleasedEvent, (_, _) => sliderPositionUserMoving = false,
            RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(PointerCaptureLostEvent, (_, _) => sliderPositionUserMoving = false,
            RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp
                or Key.PageDown) sliderPositionUserMoving = true;
        }, RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(KeyUpEvent, (_, _) => sliderPositionUserMoving = false, RoutingStrategies.Tunnel);

        // For any direct value changes
        sliderPosition.ValueChanged += (s, e) =>
        {
            NotifyPositionChanged(e.NewValue);
            if (sliderPositionUserMoving) UserSeeked?.Invoke(e.NewValue);
        };

        _gridProgress.Children.Add(sliderPosition);
        Grid.SetColumn(sliderPosition, 1);

        _iconVolume = new TextBlock
        {
            Text = "v"
        };
        _gridProgress.Children.Add(_iconVolume);
        Grid.SetColumn(_iconVolume, 2);

        var sliderVolume = new Slider
        {
            Minimum = 0,
            Maximum = videoPlayerInstance.VolumeMaximum,
            Width = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
            [AutomationProperties.NameProperty] = "Volume"
        };

        sliderVolume.TemplateApplied += (s, e) =>
        {
            if (e.NameScope.Find<Thumb>("thumb") is Thumb thumb)
            {
                thumb.Width = 14;
                thumb.Height = 14;
            }
        };
        sliderVolume.Bind(RangeBase.ValueProperty, this.GetObservable(VolumeProperty));

        sliderVolume.ValueChanged += (s, e) =>
        {
            if (_volumeIgnore == e.NewValue) return;

            Volume = e.NewValue;
            VideoPlayer.Volume = e.NewValue;
            VolumeChanged?.Invoke(e.NewValue);
            SetVolumeIcon(e.NewValue < 0.0001);
        };


        _gridProgress.Children.Add(sliderVolume);
        Grid.SetColumn(sliderVolume, 3);


        // ProgressText
        var progressText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeight.Bold
        };
        progressText.Bind(TextBlock.TextProperty, this.GetObservable(ProgressTextProperty));
        _gridProgress.Children.Add(progressText);
        Grid.SetColumn(progressText, 1);
        ProgressText = string.Empty;
        progressText.PointerPressed += (_, _) => ToggleDisplayProgressTextModeRequested?.Invoke();

        _textBlockPlayerName = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Opacity = 0.6
        };
        _gridProgress.Children.Add(_textBlockPlayerName);
        Grid.SetColumn(_textBlockPlayerName, 3);

        _textBlockVideoFileName = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Opacity = 0.6,
            TextAlignment = TextAlignment.Right
        };
        _gridProgress.Children.Add(_textBlockVideoFileName);
        Grid.SetColumn(_textBlockVideoFileName, 4);
        _textBlockVideoFileName.PointerPressed += (_, e) => { VideoFileNamePointerPressed?.Invoke(e); };

        Content = mainGrid;

        sliderPosition.Maximum = 1;
        sliderPosition.Value = 0;

        sliderVolume.Maximum = LibMpvDynamicPlayer.MaxVolume;
        sliderVolume.Value = 50;

        // Attach keyboard event handler to detect keyboard activity
        KeyDown += OnKeyDown;
    }

    public Control? PlayerContent
    {
        get => GetValue(PlayerContentProperty);
        set => SetValue(PlayerContentProperty, value);
    }

    public double Volume
    {
        get => GetValue(VolumeProperty);
        set
        {
            if (value < 0)
                value = 0;
            else if (value > VideoPlayer.VolumeMaximum) value = VideoPlayer.VolumeMaximum;

            SetValue(VolumeProperty, value);
            VideoPlayer.Volume = value;
        }
    }

    /// <summary>
    ///     Video position in seconds.
    /// </summary>
    public double Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public string ProgressText
    {
        get => GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    public ICommand PlayCommand
    {
        get => GetValue(PlayCommandProperty);
        set => SetValue(PlayCommandProperty, value);
    }

    public ICommand StopCommand
    {
        get => GetValue(StopCommandProperty);
        set => SetValue(StopCommandProperty, value);
    }

    public ICommand FullScreenCommand
    {
        get => GetValue(FullScreenCommandProperty);
        set => SetValue(FullScreenCommandProperty, value);
    }

    public bool StopIsVisible
    {
        get => GetValue(StopIsVisibleProperty);
        set => SetValue(StopIsVisibleProperty, value);
    }

    public bool FullScreenIsVisible
    {
        get => GetValue(FullScreenIsVisibleProperty);
        set => SetValue(FullScreenIsVisibleProperty, value);
    }

    public bool IsFullScreen
    {
        get => _isFullScreen;
        set
        {
            if (_isFullScreen == value) return;

            _buttonFullScreenCollapse.IsVisible = value;
            _buttonFullScreen.IsVisible = !value;
            _isFullScreen = value;

            // Start or stop the auto-hide mechanism based on full screen state
            if (value)
            {
                StartAutoHideControls();
            }
            else
            {
                StopAutoHideControls();
                ShowControls();
            }

            IsFullScreenChanged?.Invoke(value);
        }
    }

    public bool IsPlaying => VideoPlayer.IsPlaying;

    public IVideoPlayer VideoPlayer { get; }

    public bool VideoPlayerDisplayTimeLeft { get; set; }

    public int ContentWidth => _contentPresenter?.Bounds.Width > 0 ? (int)_contentPresenter.Bounds.Width : 0;
    public int ContentHeight => _contentPresenter?.Bounds.Height > 0 ? (int)_contentPresenter.Bounds.Height : 0;

    // Enable/disable click-to-toggle behavior (default on)
    public bool ClickToTogglePlay { get; set; } = true;
    public bool IsSmpteTimingEnabled { get; set; }

    public event Action<bool>? IsFullScreenChanged;

    private void NotifyPositionChanged(double newPosition)
    {
        if (Math.Abs(_positionIgnore - newPosition) < 0.001) return;

        // First update our property
        Position = newPosition;

        VideoPlayer.Position = newPosition;

        // Then notify listeners like the ViewModel
        PositionChanged?.Invoke(newPosition);
    }

    public void SetPosition(double seconds)
    {
        Position = seconds;
    }

    public void SetPositionDisplayOnly(double seconds)
    {
        _positionIgnore = seconds;
        Position = seconds;
    }

    // Raised when the user clicks the video surface (row 0), not the controls row.
    public event EventHandler<PointerPressedEventArgs>? SurfacePointerPressed;

    private void OnMainGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed || _surfaceLeftButtonDown) return;

        // If the click is inside the controls row (_gridProgress), ignore it
        var inControls = false;
        try
        {
            var ptInControls = e.GetPosition(_gridProgress);
            inControls =
                ptInControls.X >= 0 &&
                ptInControls.Y >= 0 &&
                ptInControls.X <= _gridProgress.Bounds.Width &&
                ptInControls.Y <= _gridProgress.Bounds.Height;
        }
        catch
        {
            // ignore
        }

        if (inControls) return;

        _surfaceLeftButtonDown = true;
        e.Pointer.Capture(this);

        // This is a click on the video surface
        SurfacePointerPressed?.Invoke(this, e);

        if (ClickToTogglePlay)
        {
            VideoPlayer.PlayOrPause();
            PlayPauseRequested?.Invoke();
            e.Handled = true;
        }

        if (IsFullScreen)
            // Consider this user activity for the auto-hide logic
            OnUserActivity();
    }

    private void OnMainGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_surfaceLeftButtonDown) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
        {
            _surfaceLeftButtonDown = false;
            e.Pointer.Capture(null);
        }
    }

    public event Action? PlayPauseRequested;
    public event Action? StopRequested;
    public event Action? FullscreenRequested;
    public event Action? FullscreenCollapseRequested;
    public event Action<double>? PositionChanged;
    public event Action<double>? UserSeeked;
    public event Action<double>? VolumeChanged;
    public event Action? ToggleDisplayProgressTextModeRequested;
    public event Action<PointerPressedEventArgs>? VideoFileNamePointerPressed;

    public void SetPlayPauseIcon(bool isPlaying)
    {
        if (isPlaying)
            AutomationProperties.SetName(_buttonPlay, "Pause");
        else
            AutomationProperties.SetName(_buttonPlay, "Play");
    }

    public void SetVolumeIcon(bool isMuted)
    {
        Dispatcher.UIThread.Invoke(() => { _iconVolume.Text = isMuted ? "x" : "v"; });
    }

    internal async Task Open(string videoFileName)
    {
        // Reset slider state before LoadFile. Otherwise, when the new file's
        // Duration arrives on the next timer tick, the slider's Maximum drops
        // and a stale Value (left over from the previous file) gets clamped to
        // the new Maximum — firing ValueChanged and seeking mpv to EOF.
        SetPositionDisplayOnly(0);
        Duration = 0;

        await VideoPlayer.LoadFile(videoFileName);
        VideoPlayer.Volume = Volume;
        _positionTimer?.Stop();
        _slowPollCounter = 4; // force Duration+icon update on the very first tick
        StartPositionTimer();
        VideoPlayer.Pause();
        _textBlockPlayerName.Text = VideoPlayer.Name;
        _videoFileName = videoFileName;

        // Re-arm fullscreen auto-hide. A preceding Close() (e.g. via Ctrl+N
        // before opening a new file) stops _autoHideTimer, and the IsFullScreen
        // setter doesn't run a fresh true→true transition — so without this
        // the controls would stay visible until the user moves the cursor on
        // the fullscreen monitor.
        if (IsFullScreen) StartAutoHideControls();

        var shortName = Path.GetFileName(videoFileName);
        if (shortName.Length > 55) shortName = "..." + shortName[^50..];
        _textBlockVideoFileName.Text = shortName;
    }

    internal void Close()
    {
        _positionTimer?.Stop();
        StopAutoHideControls();
        VideoPlayer.CloseFile();
        ProgressText = string.Empty;
        _videoFileName = string.Empty;
        _textBlockVideoFileName.Text = string.Empty;
        SetPositionDisplayOnly(0);
        Duration = 0;
    }

    internal async Task WaitForPlayersReadyAsync(int timeoutMs = 2500)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < end)
        {
            // Consider player ready when Duration is known (> 0)
            var ready = VideoPlayer.Duration > 0.001;

            if (ready) break;

            await Task.Delay(100);
        }

        // Small extra delay to ensure seeking is reliable
        await Task.Delay(200);
    }

    internal void TogglePlayPause()
    {
        VideoPlayer.PlayOrPause();
    }

    internal AudioTrackInfo? ToggleAudioTrack()
    {
        return VideoPlayer.ToggleAudioTrack();
    }

    private void StartPositionTimer()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _positionTimer.Tick += (s, e) =>
        {
            // Duration and IsPlaying change infrequently — poll every 5th tick (~250 ms)
            // instead of every 50 ms to reduce P/Invoke overhead on the UI thread.
            // Polled first so that ProgressText below always uses the current Duration.
            _slowPollCounter++;
            if (_slowPollCounter >= 5)
            {
                _slowPollCounter = 0;
                Duration = VideoPlayer.Duration;
                SetPlayPauseIcon(VideoPlayer.IsPlaying);
            }

            var postFix = IsSmpteTimingEnabled ? " (SMPTE)" : string.Empty;
            var pos = VideoPlayer.Position;
            if (IsSmpteTimingEnabled) pos = pos * 1000.0 / 1001.0; // SMPTE timing adjustment

            SetPositionDisplayOnly(pos);

            if (VideoPlayerDisplayTimeLeft)
            {
                var left = Duration - pos;

                if (left > 0.001)
                    ProgressText =
                        $"-{left.ToString("0.00")}{postFix}";
                else
                    ProgressText =
                        $"{0.ToString("0.00")}{postFix}";
            }
            else
            {
                ProgressText =
                    $"{pos.ToString("0.00")}/{Duration.ToString("0.00")}{postFix}";
            }
        };
        _positionTimer.Start();
    }

    private void StartAutoHideControls()
    {
        _lastActivityTime = DateTime.UtcNow;

        // When the user opts to hide controls in full-screen, never show them —
        // not even briefly on entry — and don't bother arming the auto-hide timer.
        if (false)
        {
            HideControls();
            _autoHideTimer?.Stop();
            return;
        }

        // Show controls initially when entering full screen
        ShowControls();

        // Single one-shot timer reset on each user activity. Stops itself on tick so
        // Stop()+Start() reliably reschedules a fresh 3-second wait from "now" — a
        // free-running periodic timer can drift out of phase with _lastActivityTime
        // and fail to hide after the user re-shows controls during playback.
        if (_autoHideTimer == null)
        {
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _autoHideTimer.Tick += (s, e) =>
            {
                _autoHideTimer?.Stop();
                if (IsFullScreen) HideControls();
            };
        }

        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    private void StopAutoHideControls()
    {
        _autoHideTimer?.Stop();
    }

    private void OnUserActivity()
    {
        _lastActivityTime = DateTime.UtcNow;
        if (IsFullScreen)
        {
            // If the user opted to hide controls in full-screen, don't reveal them on activity.
            if (false) return;

            ShowControls();
            if (_autoHideTimer != null)
            {
                _autoHideTimer.Stop();
                _autoHideTimer.Start();
            }
        }
    }

    public void NotifyUserActivity()
    {
        OnUserActivity();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsFullScreen) OnUserActivity();
    }

    public void Reload()
    {
        var videoFileName = _videoFileName;
        var position = Position;
        Close();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(100);
                await Open(videoFileName);
                await Task.Delay(100);
                Position = position;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e, "Failed to reload video");
            }
        });
    }

    private void ShowControls()
    {
        Dispatcher.UIThread.Post(() => { _gridProgress.IsVisible = true; });
    }

    private void HideControls()
    {
        Dispatcher.UIThread.Post(() => { _gridProgress.IsVisible = false; });
    }

    internal void SetSpeed(double speed)
    {
        VideoPlayer.Speed = speed;
    }

    public void HideVideoControls()
    {
        _gridProgress.IsVisible = false;
    }
}