using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using LunaTV.Base.Web;
using LunaTV.Constants;
using LunaTV.ViewModels.Base;

namespace LunaTV.ViewModels;

public partial class PlaygroundViewModel : PageViewModelBase
{
    public override string Title => "创作广场";

    public override IconSource IconSource { set; get; } =
        App.TopLevel.TryFindResource("ColorFillIcon", out var value) ? (IconSource)value : null;

    private readonly LunaHttpStaticPageServer _htmlServerProxy;
    [ObservableProperty] private bool _showGridLines;
    [ObservableProperty] private Size _viewportSize;
    [ObservableProperty] private double _zoom = 1.0;


    public PlaygroundViewModel()
    {
        _htmlServerProxy = new LunaHttpStaticPageServer();
        _htmlServerProxy?.Start(GlobalDefine.RootPath + "wwwroot/unfake", 8080);
    }


    [RelayCommand]
    private void ImageNode()
    {
    }


    [RelayCommand]
    private async void AlgorithmNode()
    {
        await App.TopLevel.Launcher.LaunchUriAsync(new Uri("http://localhost:8080/index.html"));
    }

    [RelayCommand]
    private void DisplayNode()
    {
    }
}