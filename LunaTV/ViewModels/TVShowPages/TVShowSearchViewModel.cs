using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaTV.Constants;
using LunaTV.Models;
using LunaTV.Services;
using LunaTV.ViewModels.Base;
using LunaTV.Views;
using LunaTV.Views.TVShowPages;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;

namespace LunaTV.ViewModels.TVShowPages;

public partial class TVShowSearchViewModel : ViewModelBase
{
    public const string LocalHost = "LocalHost";
    private readonly List<SearchResult> _allSearchResults = new();
    private readonly HashSet<string> _searchResultKeys = new();
    private readonly Dictionary<string, DetailResult> _searchDetails = new();
    private readonly HashSet<string> _failedSearchSources = new();
    private int _autoPageSize = 16;
    private CancellationTokenSource? _searchCancellationTokenSource;
    private List<string> _searchSources = [];
    private string? _currentSearchName;
    private bool _currentSearchIsAdult;
    private bool _isShowingDetail;
    private int _nextSourceIndex;
    private int _nextPage = 1;

    private readonly MovieTvService _apiService;

    private readonly LoadingWaitViewModel _loadingWaitViewModel = new();
    [ObservableProperty] private int _currentPage = 1;

    [ObservableProperty] private string? _inputMovieTvName;
    [ObservableProperty] private bool _isAdultMode;
    [ObservableProperty] private bool _isAdultVisible = true;
    [ObservableProperty] private string? _searchCountText = "共 0 个结果";
    [ObservableProperty] private int _totalVideos;
    [ObservableProperty] private int _pageSize = 16;
    [ObservableProperty] private bool _isAutoPageSize = true;
    [ObservableProperty] private int _manualPageSize = 16;
    [ObservableProperty] private bool _isManualPageSizeEnabled;
    [ObservableProperty] private double _searchResultCardWidth = 300;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isSearchPaused;
    [ObservableProperty] private bool _isSearchCompleted = true;
    [ObservableProperty] private bool _canSearch = true;

    public TVShowSearchViewModel()
    {
        _apiService = App.Services.GetRequiredService<MovieTvService>();
        HistoryMovies = new ObservableCollection<string>();
        SearchResults = new ObservableCollection<SearchResult>();
    }

    public ObservableCollection<string> HistoryMovies { get; set; }
    public ObservableCollection<SearchResult> SearchResults { get; set; }

    partial void OnIsAutoPageSizeChanged(bool value)
    {
        IsManualPageSizeEnabled = !value;
        if (!value)
        {
            ManualPageSize = PageSize;
            return;
        }

        ApplyPageSize(_autoPageSize);
    }

    partial void OnManualPageSizeChanged(int value)
    {
        if (IsAutoPageSize) return;

        var normalizedPageSize = NormalizePageSize(value);
        if (value != normalizedPageSize)
        {
            ManualPageSize = normalizedPageSize;
            return;
        }

        ApplyPageSize(value);
    }

    [RelayCommand]
    public async Task Search(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || IsSearching) return;

        if (IsAdultMode) IsAdultMode = AppConifg.SelectAdultApis.Count > 0;
        _searchSources = (IsAdultMode ? AppConifg.SelectAdultApis : AppConifg.SelectApis).ToList();
        if (_searchSources.Count == 0)
        {
            App.Notification?.Show(
                new Notification("没有选择任何源", $"查找\"{name}\"资源失败！", NotificationType.Warning),
                NotificationType.Warning,
                showClose: true);
            return;
        }

        App.Notification?.Show(
            new Notification("查找", name, NotificationType.Success),
            NotificationType.Success,
            showClose: true);

        _currentSearchName = name;
        _currentSearchIsAdult = IsAdultMode;
        _nextSourceIndex = 0;
        _nextPage = 1;
        _failedSearchSources.Clear();
        _searchResultKeys.Clear();
        _searchDetails.Clear();
        SearchResults.Clear();
        _allSearchResults.Clear();
        CurrentPage = 1;
        TotalVideos = 0;
        IsSearchPaused = false;
        IsSearchCompleted = false;
        SearchCountText = "搜索中，共 0 个结果";

        await RunSearchAsync();
    }

    public void StopCurrentSearch()
    {
        _searchCancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void StopSearch()
    {
        StopCurrentSearch();
    }

    [RelayCommand]
    private async Task ContinueSearch()
    {
        if (IsSearching || !IsSearchPaused || string.IsNullOrWhiteSpace(_currentSearchName)) return;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentSearchName)) return;

        IsSearching = true;
        CanSearch = false;
        IsSearchPaused = false;
        SearchCountText = $"搜索中，共 {_allSearchResults.Count} 个结果";
        _searchCancellationTokenSource = new CancellationTokenSource();
        var token = _searchCancellationTokenSource.Token;

        try
        {
            while (_nextSourceIndex < _searchSources.Count && _allSearchResults.Count < AppConifg.SearchMaxVideos)
            {
                token.ThrowIfCancellationRequested();
                var source = _searchSources[_nextSourceIndex];
                var (results, pageCount) = await _apiService.SearchPage(source, _currentSearchName, _nextPage, _currentSearchIsAdult, token);
                await AppendSearchResultsAsync(results, token);
                if (results.Count == 0 && pageCount == 0 && !string.IsNullOrWhiteSpace(_apiService.LastSearchPageError))
                {
                    _failedSearchSources.Add(source);
                    SearchCountText = $"{source} 搜索失败，继续搜索中，共 {_allSearchResults.Count} 个结果";
                }

                if (_nextPage >= pageCount || _nextPage >= AppConifg.SearchMaxPages || pageCount <= 0)
                {
                    _nextSourceIndex++;
                    _nextPage = 1;
                }
                else
                {
                    _nextPage++;
                }
            }

            IsSearchCompleted = true;
            SearchCountText = BuildSearchCountText(false);
        }
        catch (OperationCanceledException)
        {
            IsSearchPaused = true;
            IsSearchCompleted = false;
            var stoppedText = $"已停止，共 {_allSearchResults.Count} 个结果";
            SearchCountText = _failedSearchSources.Count == 0
                ? stoppedText
                : $"{stoppedText}，{_failedSearchSources.Count} 个源失败";
        }
        finally
        {
            _searchCancellationTokenSource?.Dispose();
            _searchCancellationTokenSource = null;
            IsSearching = false;
            CanSearch = true;
        }
    }

    private async Task AppendSearchResultsAsync(IEnumerable<SearchResult> results, CancellationToken cancellationToken)
    {
        var startVisible = (CurrentPage - 1) * PageSize;
        var endVisible = startVisible + PageSize;

        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(result.Id)) continue;

            var key = BuildSearchResultKey(result);
            if (_searchResultKeys.Contains(key)) continue;

            var detail = await _apiService.SearchDetail(result.Source, result.Id, _currentSearchIsAdult, cancellationToken);
            if (detail?.Episodes is not { Count: > 0 }) continue;

            _searchResultKeys.Add(key);
            _searchDetails[key] = detail;
            var index = _allSearchResults.Count;
            _allSearchResults.Add(result);
            if (index >= startVisible && index < endVisible) SearchResults.Add(result);
            if (_allSearchResults.Count >= AppConifg.SearchMaxVideos) break;
        }

        TotalVideos = _allSearchResults.Count;
        SearchCountText = BuildSearchCountText(IsSearching);
        NormalizeManualPageSize();
    }

    private string BuildSearchCountText(bool isSearching)
    {
        var text = isSearching
            ? $"搜索中，共 {_allSearchResults.Count} 个结果"
            : $"共 {_allSearchResults.Count} 个结果";

        return _failedSearchSources.Count == 0
            ? text
            : $"{text}，{_failedSearchSources.Count} 个源失败";
    }

    private static string BuildSearchResultKey(SearchResult result)
    {
        return $"{result.Source}:id:{result.Id}";
    }

    private void RemoveSearchResult(SearchResult searchResult)
    {
        _allSearchResults.Remove(searchResult);
        var key = BuildSearchResultKey(searchResult);
        _searchResultKeys.Remove(key);
        _searchDetails.Remove(key);
        TotalVideos = _allSearchResults.Count;
        SearchCountText = BuildSearchCountText(IsSearching);
        NormalizeManualPageSize();
        RefreshCurrentPage();
    }

    [RelayCommand]
    public async Task ShowDetail(object? item)
    {
        if (_isShowingDetail) return;
        if (item is not SearchResult searchResult) return;

        _isShowingDetail = true;
        try
        {
            await ShowDetailCore(searchResult);
        }
        finally
        {
            _isShowingDetail = false;
        }
    }

    private async Task ShowDetailCore(SearchResult searchResult)
    {
        App.Notification?.Show(
            new Notification("找剧中", searchResult.Name, NotificationType.Success),
            NotificationType.Success,
            showClose: true);
        _ = Loading();
        var key = BuildSearchResultKey(searchResult);
        var videos = _searchDetails.TryGetValue(key, out var cachedDetail)
            ? cachedDetail
            : await _apiService.SearchDetail(searchResult.Source, searchResult.Id, IsAdultMode);

        _loadingWaitViewModel.Close();
        if (videos?.Episodes is not { Count: > 0 })
        {
            RemoveSearchResult(searchResult);
            App.Notification?.Show(
                new Notification("没有可播放视频", $"{searchResult.Name} 没有可播放剧集，已从结果中移除。", NotificationType.Information),
                NotificationType.Information,
                showClose: true);
            return;
        }

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

        var vm = new TVShowDetailViewModel
        {
            VideoName = searchResult.Name,
            SourceName = searchResult.Source,
            VideoDetail = videos ?? new DetailResult(),
            IsVideoBorderVisible = videos?.Type is not null,
            EpisodesCountText = $"共{videos?.Episodes?.Count ?? 0}集"
        };
        vm.RefreshUi();

        await Dialog.ShowModal<TVShowDetailView, TVShowDetailViewModel>(vm, options: options);
    }

    [RelayCommand]
    private void DeleteHistoty(string name)
    {
        HistoryMovies.Remove(name);
    }

    [RelayCommand]
    private void ClearAllHistories()
    {
        HistoryMovies.Clear();
    }

    [RelayCommand]
    private void LoadPage(int page)
    {
        CurrentPage = page;
        RefreshCurrentPage();
    }

    public void UpdatePageSize(double width, double height)
    {
        if (!IsAutoPageSize) return;
        if (width <= 0 || height <= 0) return;

        const double itemMinWidth = 300;
        const double itemHeight = 168;
        const double itemSpacing = 8;

        var columns = int.Max(1, (int)(width / (itemMinWidth + itemSpacing)));
        var rows = int.Max(1, (int)(height / itemHeight));
        SearchResultCardWidth = width < itemMinWidth
            ? itemMinWidth
            : (width - itemSpacing - itemSpacing * columns) / columns;
        _autoPageSize = columns * rows;
        ManualPageSize = _autoPageSize;
        ApplyPageSize(_autoPageSize);
    }

    private void ApplyPageSize(int value)
    {
        var newPageSize = int.Max(1, value);

        if (newPageSize == PageSize) return;

        PageSize = newPageSize;

        if (_allSearchResults.Count == 0) return;

        var maxPage = int.Max(1, (int)Math.Ceiling((double)_allSearchResults.Count / PageSize));
        if (CurrentPage > maxPage) CurrentPage = maxPage;

        RefreshCurrentPage();
    }

    private int NormalizePageSize(int value)
    {
        var maxPageSize = int.Max(1, _allSearchResults.Count);
        return int.Clamp(value, 1, maxPageSize);
    }

    private void NormalizeManualPageSize()
    {
        if (IsAutoPageSize) return;

        var normalizedPageSize = NormalizePageSize(ManualPageSize);
        if (ManualPageSize != normalizedPageSize) ManualPageSize = normalizedPageSize;
    }

    private void RefreshCurrentPage()
    {
        SearchResults.Clear();

        var start = (CurrentPage - 1) * PageSize;
        if (start >= _allSearchResults.Count) return;

        _allSearchResults
            .Skip(start)
            .Take(PageSize)
            .ToList()
            .ForEach(x => SearchResults.Add(x));
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

        await Dialog.ShowModal<LoadingWaitView, LoadingWaitViewModel>(_loadingWaitViewModel, options: options);
    }
}