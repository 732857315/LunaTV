using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using LunaTV.ViewModels.Base;
using Semi.Avalonia;
using System.Collections.Generic;
using Notification = Ursa.Controls.Notification;

namespace LunaTV.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _currentAppTheme = "Auto";

    [ObservableProperty]
    private FlowDirection _currentFlowDirection;

    public string CurrentVersion => typeof(App).Assembly.GetName().Version?.ToString();
    public string CurrentAvaloniaVersion => typeof(Application).Assembly.GetName().Version?.ToString();

    public List<string> AppThemes => ["Auto", "Light", "Dark", "Aquatic", "Desert", "Dusk", "NightSky"];

    public FlowDirection[] AppFlowDirections { get; } =
        new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft };

    partial void OnCurrentAppThemeChanged(string? value)
    {
        Application? app = App.Current;
        if (app is null) return;
        ThemeVariant theme = value switch
        {
            "Auto" => app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            "Aquatic" => SemiTheme.Aquatic,
            "Desert" => SemiTheme.Desert,
            "Dusk" => SemiTheme.Dusk,
            "NightSky" => SemiTheme.NightSky,
            _ => app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark
        };
        app.RequestedThemeVariant = theme;

        App.Notification?.Show(
            new Notification("主题已更新", $"当前主题是{value}"),
            NotificationType.Success,
            classes: ["Light"]);
    }

    partial void OnCurrentFlowDirectionChanged(FlowDirection value)
    {
        if (App.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime cdl)
        {
            if (cdl.MainWindow.FlowDirection == value)
            {
                return;
            }
            cdl.MainWindow.FlowDirection = value;
        }
    }
}