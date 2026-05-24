using System;
using System.Collections.Generic;
using Irihi.Avalonia.Shared.Contracts;
using LunaTV.ViewModels.Base;

namespace LunaTV.ViewModels.TVShowPages;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaTV.Models;
using System.Collections.ObjectModel;
using System.Linq;

public partial class TVDownloadSelectionViewModel : ViewModelBase, IDialogContext
{
    public string MediaName { get; set; }
    public ObservableCollection<DownloadableEpisode> Episodes { get; set; }

    public TVDownloadSelectionViewModel(string mediaName, List<CopyMediaDetail> episodes)
    {
        MediaName = mediaName;
        Episodes = new ObservableCollection<DownloadableEpisode>(
            episodes.Select(e => new DownloadableEpisode
            {
                Url = e.Url,
                Episode = e.Episode,
                IsSelected = true // 默认全部选中
            })
        );
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool allSelected = Episodes.All(e => e.IsSelected);
        foreach (var episode in Episodes)
        {
            episode.IsSelected = !allSelected;
        }
    }

    [RelayCommand]
    private void DownloadSelected()
    {
        var selectedEpisodes = Episodes.Where(e => e.IsSelected).ToList();

        // 这里可以实现实际的下载逻辑
        // 暂时只返回选中的剧集信息
        var downloadPackage = new CopyMediaSubject
        {
            Name = MediaName,
            Medias = selectedEpisodes.Select(e => new CopyMediaDetail
            {
                Url = e.Url,
                Episode = e.Episode
            }).ToList()
        };

        // 触发下载事件或执行下载操作

        RequestClose?.Invoke(this, true);
    }


    public void Close()
    {
        RequestClose?.Invoke(this, false);
    }

    public event EventHandler<object?>? RequestClose;
}

public partial class DownloadableEpisode : ObservableObject
{
    [ObservableProperty] private string? _url;
    [ObservableProperty] private string? _episode;
    [ObservableProperty] private bool _isSelected;
}