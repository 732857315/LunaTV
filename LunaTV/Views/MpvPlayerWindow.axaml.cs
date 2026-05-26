using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LunaTV.Extensions;
using LunaTV.ViewModels;
using Ursa.Controls;

namespace LunaTV.Views;

public partial class MpvPlayerWindow : UrsaWindow
{
    /// <summary>
    /// Gets the name of the seek bar.
    /// </summary>
    private const string SeekBarPartName = "PART_SeekBar";

    /// <summary>
    /// Gets the seek bar slider control.
    /// </summary>
    private Slider? SeekBarPart { get; set; }

    /// <summary>
    /// Gets the name of the track within the seek bar.
    /// </summary>
    private const string SeekBarTrackPartName = "PART_Track";

    /// <summary>
    /// Gets the track within the seek bar.
    /// </summary>
    private Track? SeekBarTrackPart { get; set; }

    /// <summary>
    /// The name of the seek bar decrease button.
    /// </summary>
    private const string SeekBarDecreaseName = "PART_DecreaseButton";

    /// <summary>
    /// Gets the seek bar decrease button. 
    /// </summary>
    private RepeatButton? SeekBarDecreasePart { get; set; }

    /// <summary>
    /// The name of the seek bar increase button.
    /// </summary>
    private const string SeekBarIncreaseName = "PART_IncreaseButton";

    /// <summary>
    /// Gets the seek bar increase button.
    /// </summary>
    private RepeatButton? SeekBarIncreasePart { get; set; }

    /// <summary>
    /// Gets the thumb within the seek bar. 
    /// </summary>
    private Thumb? SeekBarThumbPart => SeekBarTrackPart?.Thumb;

    private readonly MpvPlayerWindowModel _viewModel;
    private readonly DispatcherTimer _overlayTimer;
    private readonly DispatcherTimer _fullscreenStateGuardTimer;
    private Point? _lastPointerPosition;
    private bool _ignorePointerUntilMoved;

    public MpvPlayerWindow()
    {
        InitializeComponent();

        _viewModel = new MpvPlayerWindowModel();
        DataContext = _viewModel;

        _viewModel.Notification = new WindowNotificationManager(this);
        _viewModel.Window = this;

        _overlayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(6)
        };
        _overlayTimer.Tick += (s, e) =>
        {
            _overlayTimer.Stop();
            HideOverlay();
        };

        _fullscreenStateGuardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _fullscreenStateGuardTimer.Tick += (_, _) =>
        {
            _fullscreenStateGuardTimer.Stop();
            FixBrokenFullScreenState();
        };

        PositionChanged += (_, _) => ScheduleFullScreenStateCheck();
        Resized += (_, _) => ScheduleFullScreenStateCheck();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        SeekBarPart = this.FindControl<Slider>(SeekBarPartName);

        // Thumb doesn't yet exist.
        if (SeekBarPart != null)
            SeekBarPart.TemplateApplied += (_, t) =>
            {
                SeekBarTrackPart = t.NameScope.FindOrThrow<Track>(SeekBarTrackPartName);
                SeekBarIncreasePart = t.NameScope.FindOrThrow<RepeatButton>(SeekBarIncreaseName);
                SeekBarDecreasePart = t.NameScope.FindOrThrow<RepeatButton>(SeekBarDecreaseName);

                SeekBarIncreasePart.AddHandler(RepeatButton.PointerPressedEvent, SeekBarPointerPressed,
                    RoutingStrategies.Tunnel);
                SeekBarDecreasePart.AddHandler(RepeatButton.PointerPressedEvent, SeekBarPointerPressed,
                    RoutingStrategies.Tunnel);
                SeekBarIncreasePart.AddHandler(RepeatButton.PointerReleasedEvent, SeekBarPointerReleased,
                    RoutingStrategies.Tunnel);
                SeekBarDecreasePart.AddHandler(RepeatButton.PointerReleasedEvent, SeekBarPointerReleased,
                    RoutingStrategies.Tunnel);

                SeekBarThumbPart!.DragStarted += (_, _) => _viewModel.IsSeekBarPressed = true;
                SeekBarThumbPart!.DragCompleted += (_, _) => _viewModel.IsSeekBarPressed = false;
            };

        _viewModel?.OnWindowLoaded();
    }


    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Stop();
        _overlayTimer.Stop();
        _fullscreenStateGuardTimer.Stop();
        base.OnClosed(e);
        (App.VisualRoot as MainWindow)?.Show();
    }

    private void SeekBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.IsSeekBarPressed = true;
    }

    private void SeekBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _viewModel.IsSeekBarPressed = false;
    }

    private void ShowOverlay()
    {
        PlayBar.IsVisible = true;
        VideoTitleOverlay.IsVisible = true;
    }

    private void HideOverlay()
    {
        PlayBar.IsVisible = false;
        VideoTitleOverlay.IsVisible = false;
        _ignorePointerUntilMoved = true;
    }

    private void RestartAutoHideOverlay()
    {
        _ignorePointerUntilMoved = false;
        ShowOverlay();
        _overlayTimer.Stop();
        _overlayTimer.Start();
    }

    private void ScheduleFullScreenStateCheck()
    {
        if (WindowState != WindowState.FullScreen) return;

        _fullscreenStateGuardTimer.Stop();
        _fullscreenStateGuardTimer.Start();
    }

    private void FixBrokenFullScreenState()
    {
        if (WindowState != WindowState.FullScreen) return;

        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;

        var screenBounds = screen.Bounds;
        var clientWidth = ClientSize.Width * DesktopScaling;
        var clientHeight = ClientSize.Height * DesktopScaling;
        var isStillFullScreenSized = Math.Abs(clientWidth - screenBounds.Width) < 8 &&
                                     Math.Abs(clientHeight - screenBounds.Height) < 8;

        if (isStillFullScreenSized) return;

        WindowState = WindowState.Maximized;
        _overlayTimer.Stop();
        ShowOverlay();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_ignorePointerUntilMoved) return;
        _lastPointerPosition = e.GetPosition(this);
        RestartAutoHideOverlay();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pointerPosition = e.GetPosition(this);
        if (_ignorePointerUntilMoved)
        {
            if (_lastPointerPosition is { } lastPosition && Math.Abs(pointerPosition.X - lastPosition.X) < 2 && Math.Abs(pointerPosition.Y - lastPosition.Y) < 2)
            {
                return;
            }

            _ignorePointerUntilMoved = false;
        }

        _lastPointerPosition = pointerPosition;
        if (!PlayBar.IsVisible) RestartAutoHideOverlay();
    }
}