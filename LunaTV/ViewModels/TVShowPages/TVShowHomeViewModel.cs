using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaTV.Base.Api;
using LunaTV.Base.DB.UnitOfWork;
using LunaTV.Base.Models;
using LunaTV.Constants;
using LunaTV.Models;
using LunaTV.ViewModels.Base;
using LunaTV.Views;
using LunaTV.Views.TVShowPages;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;

namespace LunaTV.ViewModels.TVShowPages;

public partial class TVShowHomeViewModel : ViewModelBase
{
    private int _pageSize = 16;
    private int _autoPageSize = 16;
    private const int DoubanPageLimit = 16;
    private static readonly JsonSerializerOptions DoubanJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SugarRepository<ApiSource> _apiSourceTable;

    private readonly List<string> _defaultMovieTags =
        ["热门", "最新", "经典", "豆瓣高分", "冷门佳片", "华语", "欧美", "韩国", "日本", "动作", "喜剧", "日综", "爱情", "科幻", "悬疑", "恐怖", "治愈"];

    private readonly List<string> _defaultTvTags =
        ["热门", "美剧", "英剧", "韩剧", "日剧", "国产剧", "港剧", "日本动画", "综艺", "纪录片"];

    private readonly bool _initialized;

    private readonly LoadingWaitViewModel _loadingWaitViewModel = new();
    private bool _isRefreshingMovieCards;
    private bool _refreshMovieCardsAgain;
    private CancellationTokenSource? _pageSizeRefreshDebounceTokenSource;
    private DoubanVerifyWindow? _doubanVerifyWindow;

    [ObservableProperty] private ObservableCollection<string> _doubanTags;
    private bool _isTagChanged2Refresh; //标签改变的时候要不要更新

    [ObservableProperty] private ObservableCollection<MovieCardItem> _movieCardItems;
    [ObservableProperty] private bool _movieChecked = true;
    private int _pageStart;
    [ObservableProperty] private bool _isAutoPageSize = true;
    [ObservableProperty] private int _manualPageSize = 16;
    [ObservableProperty] private bool _isManualPageSizeEnabled;
    [ObservableProperty] private double _movieCardWidth = 150;
    [ObservableProperty] private string? _searchInputText;
    [ObservableProperty] private string? _selectedTagItem;
    private string _switchMovieOrTv = "movie";

    public TVShowHomeViewModel()
    {
        var pcfg = App.Services.GetRequiredService<SugarRepository<PlayerConfig>>().GetSingle(u => u.Id > 0);
        AppConifg.PlayerConfig =
            pcfg ??
            new PlayerConfig
            {
                AdFilteringEnabled = true,
                DoubanApiEnabled = false,
                HomeAutoLoadDoubanEnabled = false,
                ForceApiNeedSpecialSource = false,
                Timeout = 15000,
                FilterAds = true,
                AutoPlayNext = false
            };
        if (pcfg == null)
            App.Services.GetRequiredService<SugarRepository<PlayerConfig>>().Insert(AppConifg.PlayerConfig);

        DoubanTags = new ObservableCollection<string>();
        MovieCardItems = new ObservableCollection<MovieCardItem>();
        _ = SwitchMovieOrTv("电影");

        _initialized = true;

        _apiSourceTable = App.Services.GetRequiredService<SugarRepository<ApiSource>>();
        var apiSources = _apiSourceTable.GetList();
        AppConifg.UpdateSites(apiSources);
    }

    partial void OnIsAutoPageSizeChanged(bool value)
    {
        IsManualPageSizeEnabled = !value;
        if (!value)
        {
            ManualPageSize = _pageSize;
            return;
        }

        ApplyPageSize(_autoPageSize);
    }

    partial void OnManualPageSizeChanged(int value)
    {
        if (IsAutoPageSize) return;

        var newPageSize = int.Max(1, value);
        if (value != newPageSize)
        {
            ManualPageSize = newPageSize;
            return;
        }

        ApplyPageSize(value);
    }

    public void UpdatePageSize(double width, double height)
    {
        if (!IsAutoPageSize) return;
        if (width <= 0 || height <= 0) return;

        const double itemMinWidth = 150;
        const double itemHeight = 285;
        const double itemSpacing = 8;

        var columns = int.Max(1, (int)(width / (itemMinWidth + itemSpacing)));
        var rows = int.Max(1, (int)(height / itemHeight));
        MovieCardWidth = width < itemMinWidth
            ? itemMinWidth
            : (width - itemSpacing - itemSpacing * columns) / columns;
        _autoPageSize = columns * rows;
        ManualPageSize = _autoPageSize;
        ApplyPageSize(_autoPageSize);
    }

    private void ApplyPageSize(int value)
    {
        var newPageSize = int.Max(1, value);

        if (newPageSize == _pageSize) return;

        _pageSize = newPageSize;
        if (_initialized) DebounceRefreshMovieCards();
    }

    private void DebounceRefreshMovieCards()
    {
        _pageSizeRefreshDebounceTokenSource?.Cancel();
        var tokenSource = new CancellationTokenSource();
        _pageSizeRefreshDebounceTokenSource = tokenSource;
        _ = RefreshMovieCardsAfterDelayAsync(tokenSource);
    }

    private async Task RefreshMovieCardsAfterDelayAsync(CancellationTokenSource tokenSource)
    {
        try
        {
            await Task.Delay(300, tokenSource.Token);
            await Dispatcher.UIThread.InvokeAsync(RefreshMovieCardsAsync);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_pageSizeRefreshDebounceTokenSource, tokenSource))
            {
                _pageSizeRefreshDebounceTokenSource = null;
            }

            tokenSource.Dispose();
        }
    }

    [RelayCommand]
    private async Task SwitchMovieOrTv(string tag)
    {
        _isTagChanged2Refresh = false;
        DoubanTags.Clear();
        if (tag == "电影")
        {
            _defaultMovieTags.ForEach(x => DoubanTags.Add(x));
            _switchMovieOrTv = "movie";
        }
        else if (tag == "电视")
        {
            _defaultTvTags.ForEach(x => DoubanTags.Add(x));
            _switchMovieOrTv = "tv";
        }

        SelectedTagItem = DoubanTags.FirstOrDefault();

        if (!_initialized && AppConifg.PlayerConfig.HomeAutoLoadDoubanEnabled)
            await RefreshMovieCardsAsync();
        else
            await RefreshMovieCardsAsync();
    }

    private async Task RefreshMovieCardsAsync()
    {
        if (_isRefreshingMovieCards)
        {
            _refreshMovieCardsAgain = true;
            return;
        }

        _isRefreshingMovieCards = true;
        try
        {
            do
            {
                _refreshMovieCardsAgain = false;
                await RefreshMovieCardsCoreAsync();
            } while (_refreshMovieCardsAgain);
        }
        finally
        {
            _isRefreshingMovieCards = false;
        }
    }

    private async Task<string> FetchDoubanSubjectsInternal(string type, string tag, string sort, int limit, int start)
    {
        if (_doubanVerifyWindow is not null)
        {
            var encodedTag = Uri.EscapeDataString(tag);
            var url = $"https://movie.douban.com/j/search_subjects?type={type}&tag={encodedTag}&sort={sort}&page_limit={limit}&page_start={start}";
            var result = await _doubanVerifyWindow.FetchApiAsync(url);
            if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("<!DOCTYPE"))
            {
                _doubanVerifyWindow.HideAfterVerification();
                return result;
            }

            throw new InvalidOperationException("豆瓣验证窗口未返回有效数据，请确认验证已完成后再刷新。");
        }

        return await App.Services.GetRequiredService<IWebApi>()
            .FetchDoubanSubjectsByTag(type, tag, sort, limit, start);
    }

    private async Task<string> FetchDoubanSuggestionsInternal(string query)
    {
        if (_doubanVerifyWindow is not null)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://movie.douban.com/j/subject_suggest?q={encodedQuery}";
            var result = await _doubanVerifyWindow.FetchApiAsync(url);
            if (!string.IsNullOrWhiteSpace(result) && !result.StartsWith("<!DOCTYPE"))
            {
                _doubanVerifyWindow.HideAfterVerification();
                return result;
            }

            throw new InvalidOperationException("豆瓣验证窗口未返回有效数据，请确认验证已完成后再搜索。");
        }

        return await App.Services.GetRequiredService<IWebApi>()
            .GetchDoubanSearchSuggestions(query);
    }

    private static bool IsDoubanVerificationRequired(Exception exception)
    {
        return exception is ApiException { StatusCode: HttpStatusCode.Forbidden } ||
               exception is InvalidOperationException { Message: var message } &&
               message.Contains("豆瓣验证窗口未返回有效数据", StringComparison.Ordinal);
    }

    private static string NormalizeDoubanImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return string.Empty;

        var normalized = imageUrl.Replace("\\/", "/").Trim();
        if (normalized.StartsWith("//")) normalized = $"https:{normalized}";
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) normalized = "https://" + normalized[7..];

        return normalized.Replace("img2.doubanio.com", "img9.doubanio.com")
            .Replace("img3.doubanio.com", "img9.doubanio.com");
    }

    private static string GetImageCachePath(string imageUrl)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)));
        var extension = Path.GetExtension(Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : imageUrl);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";

        var cachePath = Path.Combine(GlobalDefine.DataPath, "DoubanImages");
        if (!Directory.Exists(cachePath)) Directory.CreateDirectory(cachePath);
        return Path.Combine(cachePath, $"{hash}{extension}");
    }

    private static async Task<string> GetCachedDoubanImageAsync(string? imageUrl)
    {
        var normalized = NormalizeDoubanImageUrl(imageUrl);
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

        var cacheFile = GetImageCachePath(normalized);
        if (File.Exists(cacheFile)) return cacheFile;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, normalized);
            request.Headers.TryAddWithoutValidation("User-Agent", LunaTV.Base.Constants.UserAgent.GetRandomUserAgent());
            request.Headers.TryAddWithoutValidation("Referer", "https://movie.douban.com/");
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            await using var fileStream = File.Create(cacheFile);
            await response.Content.CopyToAsync(fileStream);
            return cacheFile;
        }
        catch
        {
            return normalized;
        }
    }

    private static List<DoubanSubject> ParseDoubanSubjects(string json)
    {
        var trimmedJson = json.TrimStart();
        if (trimmedJson.Length == 0 || trimmedJson[0] is not ('{' or '[' or '"'))
            throw new InvalidOperationException("豆瓣验证窗口未返回有效数据，请确认验证已完成后再刷新。");

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("豆瓣验证窗口未返回有效数据，请确认验证已完成后再刷新。");
        }

        if (root.ValueKind == JsonValueKind.String)
        {
            var innerJson = root.GetString();
            return string.IsNullOrWhiteSpace(innerJson) ? [] : ParseDoubanSubjects(innerJson);
        }

        if (root.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<DoubanSubject>>(root.GetRawText(), DoubanJsonOptions) ?? [];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("subjects", out var subjectsElement) &&
            subjectsElement.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<DoubanSubject>>(subjectsElement.GetRawText(), DoubanJsonOptions) ?? [];

        return [];
    }

    private async Task<List<DoubanSubject>> FetchDoubanSubjectsPageAsync()
    {
        var subjects = new List<DoubanSubject>();
        var requests = int.Max(1, (int)Math.Ceiling((double)_pageSize / DoubanPageLimit));

        for (var i = 0; i < requests; i++)
        {
            try
            {
                var sts = await FetchDoubanSubjectsInternal(_switchMovieOrTv, SelectedTagItem!, "recommend", DoubanPageLimit,
                    _pageStart + i * DoubanPageLimit);
                var pageSubjects = ParseDoubanSubjects(sts);
                if (pageSubjects.Count == 0) break;
                subjects.AddRange(pageSubjects);
            }
            catch (Exception e) when (subjects.Count > 0 && IsDoubanVerificationRequired(e))
            {
                break;
            }
        }

        return subjects.Take(_pageSize).ToList();
    }

    private async Task RefreshMovieCardsCoreAsync()
    {
        if (SelectedTagItem is null)
        {
            _isTagChanged2Refresh = true;
            return;
        }

        if (AppConifg.PlayerConfig.DoubanApiEnabled is false)
        {
            App.Notification?.Show(new Notification("温馨提示", "豆瓣接口未启动"),
                NotificationType.Information);
            _isTagChanged2Refresh = true;
            return;
        }

        if (_initialized) _ = Loading();

        try
        {
            var subjects = await FetchDoubanSubjectsPageAsync();
            MovieCardItems.Clear();
            foreach (var item in subjects)
            {
                MovieCardItems.Add(new MovieCardItem
                {
                    Name = item.Title,
                    Image = await GetCachedDoubanImageAsync(item.Cover),
                    Score = string.IsNullOrEmpty(item.Rate) ? "暂无" : item.Rate,
                    DoubanUrl = item.Url
                });
            }
        }
        catch (Exception e)
        {
            if (IsDoubanVerificationRequired(e))
            {
                OpenDoubanVerifyWindow(true);
            }
            else
            {
                App.Notification?.Show(new Notification("查找失败", $"豆瓣检索失败：{e.Message}", NotificationType.Error), NotificationType.Error);
            }
        }


        if (_initialized)
            _loadingWaitViewModel.Close();
        _isTagChanged2Refresh = true;
    }

    private void OpenDoubanVerifyWindow(bool showNotification)
    {
        var owner = App.VisualRoot as MainWindow;
        if (_doubanVerifyWindow is not null)
        {
            if (showNotification)
                App.Notification?.Show(new Notification("豆瓣需要验证", "请在弹出的豆瓣窗口中手动完成验证，验证成功后窗口会自动隐藏。", NotificationType.Warning), NotificationType.Warning);

            _doubanVerifyWindow.WaitForVerification();
            _doubanVerifyWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _doubanVerifyWindow.Topmost = true;
            _doubanVerifyWindow.Show(owner);
            _doubanVerifyWindow.Activate();
            return;
        }

        if (showNotification)
            App.Notification?.Show(new Notification("豆瓣需要验证", "请在弹出的豆瓣窗口中手动完成验证，验证成功后窗口会自动隐藏。", NotificationType.Warning), NotificationType.Warning);

        _doubanVerifyWindow = new DoubanVerifyWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Topmost = true
        };
        _doubanVerifyWindow.WaitForVerification();
        _doubanVerifyWindow.Show(owner);
        _doubanVerifyWindow.Activate();
    }

    partial void OnSelectedTagItemChanged(string? value)
    {
        if (_isTagChanged2Refresh) _ = RefreshMovieCardsAsync();
    }

    [RelayCommand]
    private async Task MoreMovieOrTv()
    {
        _isTagChanged2Refresh = false;
        _pageStart += _pageSize;
        if (_pageStart > 9 * DoubanPageLimit) _pageStart = 0;

        await RefreshMovieCardsAsync();
    }

    [RelayCommand]
    private async Task BackHome()
    {
        MovieChecked = true;
        await SwitchMovieOrTv("电影");
    }

    [RelayCommand]
    private async void OpenLocalVideo()
    {
        var videoAll = new FilePickerFileType("All Videos")
        {
            Patterns = new string[6] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv" },
            AppleUniformTypeIdentifiers = new string[1] { "public.video" },
            MimeTypes = new string[1] { "video/*" }
        };


        var file = await App.StorageProvider?.OpenFilePickerAsync(
            new FilePickerOpenOptions()
            {
                Title = "打开文件",
                FileTypeFilter = new[]
                {
                    videoAll,
                    FilePickerFileTypes.All,
                },
                AllowMultiple = false,
            });

        if (file is { Count: > 0 })
        {
            var win = new MpvPlayerWindow();
            (App.VisualRoot as MainWindow)?.Hide();
            win.Show();
            if (win.DataContext is MpvPlayerWindowModel videoModel)
            {
                videoModel.MediaUrl = file[0].Path.LocalPath;
                videoModel.Title = file[0].Path.LocalPath.Substring(file[0].Path.LocalPath.LastIndexOf('\\') + 1);
            }
        }
    }

    [RelayCommand]
    private async Task NaviSearch(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (AppConifg.PlayerConfig.DoubanApiEnabled is false)
        {
            App.Notification?.Show(new Notification("温馨提示", "豆瓣接口未启动"),
                NotificationType.Information);
            return;
        }

        _ = Loading();

        try
        {
            var sts = await FetchDoubanSuggestionsInternal(text);
            var json = JsonSerializer.Deserialize<List<DoubanSuggestionSubject>>(sts,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // 处理大小写不敏感
                });
            MovieCardItems.Clear();
            if (json is not null)
                foreach (var item in json)
                {
                    MovieCardItems.Add(new MovieCardItem
                    {
                        Name = item.Title,
                        Image = await GetCachedDoubanImageAsync(item.Img),
                        Score = "暂无",
                        DoubanUrl = item.Url
                    });
                }
        }
        catch (Exception e)
        {
            App.Notification?.Show(new Notification("查找失败", "豆瓣检索失败", NotificationType.Error), NotificationType.Error);
        }


        _loadingWaitViewModel.Close();
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

public partial class MovieCardItem : ViewModelBase
{
    [ObservableProperty] private string? _doubanUrl;

    [ObservableProperty] private string? _image;
    [ObservableProperty] private string? _name;

    [ObservableProperty] private string? _score;


    [RelayCommand]
    private async Task Search(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var mvm = App.Services.GetRequiredService<MainViewModel>();
        var searchMenuItem = mvm.Items.FirstOrDefault(x => x.Name == "搜索");
        if (searchMenuItem is null) return;

        mvm.SelectedItem = searchMenuItem;
        if (mvm.GetControl(searchMenuItem.Name) is not TVShowSearchView { DataContext: TVShowSearchViewModel svm }) return;

        if (svm.IsSearching)
        {
            svm.StopCurrentSearch();
            while (svm.IsSearching)
            {
                await Task.Delay(100);
            }
        }

        svm.InputMovieTvName = name;
        svm.IsAdultMode = false;
        await svm.Search(name);
    }
}
