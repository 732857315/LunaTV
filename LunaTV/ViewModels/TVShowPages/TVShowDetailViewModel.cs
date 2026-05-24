using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irihi.Avalonia.Shared.Contracts;
using LunaTV.Base.DB.UnitOfWork;
using LunaTV.Base.Models;
using LunaTV.Constants;
using LunaTV.Models;
using LunaTV.ViewModels.Base;
using LunaTV.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using LunaTV.Views.TVShowPages;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;

namespace LunaTV.ViewModels.TVShowPages;

public partial class TVShowDetailViewModel : ViewModelBase, IDialogContext
{
    private readonly SugarRepository<ViewHistory> _viewHistoryTable;

    public TVShowDetailViewModel()
    {
        _viewHistoryTable = App.Services.GetRequiredService<SugarRepository<ViewHistory>>();
    }

    public string? VideoName { get; set; }
    public string? SourceName { get; set; }
    public string SourceNameText => $"({AppConifg.ApiSitesConfig[SourceName].Name})";
    public DetailResult VideoDetail { get; set; }
    public List<EpisodeSubjectItem> Episodes { get; set; } = new();
    public bool IsVideoBorderVisible { get; set; }
    public string EpisodesCountText { get; set; }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public void RefreshUi()
    {
        Episodes = VideoDetail.Episodes.Select(ep => new EpisodeSubjectItem
        {
            Watched = false,
            Name = ep.Name,
            Url = ep.Url
        }).ToList();
        EpisodesCountText = $"共{Episodes.Count}集";
        ViewHistory? viewHistory = _viewHistoryTable.GetSingle(his =>
            his.VodId == VideoDetail.VodId && his.Source == SourceName && his.Name == VideoName);
        if (viewHistory is not null)
        {
            Episodes[Episodes.IndexOf(Episodes.FirstOrDefault(ep => ep.Name == viewHistory.Episode))].Watched = true;
        }
    }

    [RelayCommand]
    private void Play(object? episode)
    {
        if (episode is not EpisodeSubjectItem episodeSubject) return;

        Episodes.ForEach(episode => episode.Watched = episode.Name == episodeSubject.Name);

        var win = new MpvPlayerWindow();
        (App.VisualRoot as MainWindow)?.Hide();
        win.Show();
        if (win.DataContext is MpvPlayerWindowModel videoModel)
        {
            videoModel.MediaUrl = episodeSubject.Url;
            videoModel.Title = $"{VideoName} {episodeSubject.Name}";
            videoModel.Episodes = new ObservableCollection<EpisodeSubjectItem>(Episodes);

            ViewHistory? viewHistory = _viewHistoryTable.GetSingle(his =>
                his.VodId == VideoDetail.VodId && his.Source == VideoDetail.Source && his.Name == VideoName);
            if (viewHistory is not null)
            {
                videoModel.ViewHistory = new ViewHistory
                {
                    Id = viewHistory.Id,
                    VodId = VideoDetail.VodId,
                    Name = VideoName,
                    Episode = episodeSubject.Name,
                    Url = episodeSubject.Url,
                    Source = VideoDetail.Source,
                    PlaybackPosition = viewHistory.PlaybackPosition,
                    Duration = 0,
                    TotalEpisodeCount = VideoDetail.Episodes.Count,
                    IsLocal = false
                };
            }
            else
            {
                videoModel.ViewHistory = new ViewHistory
                {
                    VodId = VideoDetail.VodId,
                    Name = VideoName,
                    Episode = episodeSubject.Name,
                    Url = episodeSubject.Url,
                    Source = VideoDetail.Source,
                    PlaybackPosition = 0,
                    Duration = 0,
                    TotalEpisodeCount = VideoDetail.Episodes.Count,
                    IsLocal = false
                };
            }
        }

        Close();
    }

    [RelayCommand]
    private async Task CopyLinks()
    {
        var CopyMediaSubject = new CopyMediaSubject
        {
            Name = VideoName,
            Medias = Episodes.Select(ep => new CopyMediaDetail
            {
                Url = ep.Url,
                Episode = ep.Name
            }).ToList()
        };
        await App.Clipboard.SetTextAsync(JsonSerializer.Serialize(CopyMediaSubject,
            new JsonSerializerOptions
            {
                WriteIndented = true, // 美化输出（缩进）
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 支持中文字符
            }));
        App.Notification?.Show(new Notification("复制链接", $"成功复制{Episodes.Count}个链接到剪切板",
                NotificationType.Success),
            NotificationType.Success);

        var downloadSelectionViewModel = new TVDownloadSelectionViewModel(VideoName, CopyMediaSubject.Medias);
        if (await Dialog.ShowCustomAsync<TVDownloadSelectionView, TVDownloadSelectionViewModel, bool>(
                downloadSelectionViewModel))
        {
            var selectedEpisodes = downloadSelectionViewModel.Episodes.Where(ep => ep.IsSelected);
            var tvdownloadVm = App.Services.GetRequiredService<TVDownloadViewModel>();

            foreach (var episode in selectedEpisodes)
            {
                await tvdownloadVm.AddMediaDownload($"{VideoName}-{episode.Episode}", episode.Url);
            }
        }
    }
}

public partial class EpisodeSubjectItem : ObservableObject
{
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _url;
    [ObservableProperty] private bool _watched; //是否观看
}