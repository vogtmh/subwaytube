using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Media.Core;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using subwayTube.Models;
using subwayTube.Services;

namespace subwayTube
{
    public sealed partial class MainPage : Page
    {
        private readonly InnerTubeService _innerTube = new InnerTubeService();
        private readonly DataService _data = new DataService();
        private readonly ObservableCollection<VideoResult> _results = new ObservableCollection<VideoResult>();
        private readonly ObservableCollection<VideoResult> _feedItems = new ObservableCollection<VideoResult>();
        private bool _isPlayerOpen;
        private bool _isChannelOpen;
        private List<StreamFormat> _currentFormats;
        private string _currentVideoId;
        private string _currentAuthorId;
        private string _currentAuthor;
        private string _currentThumbnailUrl;
        private bool _ignoreQualityChange;
        private PlayerResponse _lastPlayerResponse;
        private string _lastPlayedUrl;
        private InMemoryRandomAccessStream _currentStream;
        private string _cachedHlsUrl;
        private StreamFormat _cachedMuxedFormat;
        private readonly Windows.Web.Http.HttpClient _streamClient;
        private int _activeTab; // 0=Feed, 1=Search, 2=Favorites
        private int _favSubTab; // 0=Channels, 1=History
        private bool _feedLoaded;
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _darkBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 17, 17));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _darkGrayBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _accentBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));

        public MainPage()
        {
            this.InitializeComponent();
            ResultsList.ItemsSource = _results;
            FeedList.ItemsSource = _feedItems;

            _streamClient = new Windows.Web.Http.HttpClient(new IosUserAgentFilter());

            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
            VideoPlayer.MediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            // Load data and show feed
            InitAsync();
        }

        private async void InitAsync()
        {
            await _data.LoadAllAsync();
            ChannelsList.ItemsSource = _data.Subscriptions;
            HistoryList.ItemsSource = _data.History;
            await LoadFeedAsync();
        }

        // ==================== TAB SWITCHING ====================

        private void NavFeed_Click(object sender, RoutedEventArgs e) { ShowFeed(); }
        private void NavSearch_Click(object sender, RoutedEventArgs e) { ShowSearch(); }
        private void NavFavorites_Click(object sender, RoutedEventArgs e) { ShowFavorites(); }

        private void ShowFeed()
        {
            _activeTab = 0;
            FeedPanel.Visibility = Visibility.Visible;
            SearchPanel.Visibility = Visibility.Collapsed;
            FavoritesPanel.Visibility = Visibility.Collapsed;
            NavFeed.Background = _accentBrush;
            NavSearch.Background = _darkBrush;
            NavFavorites.Background = _darkBrush;
            RefreshButton.Visibility = Visibility.Visible;
            UpdateBackButtonVisibility();
        }

        private void ShowSearch()
        {
            _activeTab = 1;
            FeedPanel.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Visible;
            FavoritesPanel.Visibility = Visibility.Collapsed;
            NavFeed.Background = _darkBrush;
            NavSearch.Background = _accentBrush;
            NavFavorites.Background = _darkBrush;
            RefreshButton.Visibility = Visibility.Collapsed;
            UpdateSearchHistoryVisibility();
            UpdateBackButtonVisibility();
        }

        private void ShowFavorites()
        {
            _activeTab = 2;
            FeedPanel.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Collapsed;
            FavoritesPanel.Visibility = Visibility.Visible;
            NavFeed.Background = _darkBrush;
            NavSearch.Background = _darkBrush;
            NavFavorites.Background = _accentBrush;
            RefreshButton.Visibility = Visibility.Collapsed;
            UpdateFavoritesView();
            UpdateBackButtonVisibility();
        }

        // ==================== FEED ====================

        private async System.Threading.Tasks.Task LoadFeedAsync()
        {
            if (_data.Subscriptions.Count == 0)
            {
                FeedEmptyText.Visibility = Visibility.Visible;
                FeedList.Visibility = Visibility.Collapsed;
                _feedLoaded = true;
                return;
            }

            FeedEmptyText.Visibility = Visibility.Collapsed;
            FeedLoadingRing.IsActive = true;
            FeedList.Visibility = Visibility.Collapsed;

            var allVideos = new List<VideoResult>();
            var semaphore = new SemaphoreSlim(5);
            var tasks = new List<System.Threading.Tasks.Task>();

            foreach (var sub in _data.Subscriptions.ToList())
            {
                tasks.Add(System.Threading.Tasks.Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var videos = await _innerTube.GetChannelVideosAsync(sub.AuthorId);
                        lock (allVideos)
                        {
                            allVideos.AddRange(videos);
                        }
                    }
                    catch { }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);

            // Sort by published text (rough sort — newest first)
            // Take top 50
            var sorted = allVideos.Take(50).ToList();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _feedItems.Clear();
                foreach (var v in sorted)
                    _feedItems.Add(v);

                FeedLoadingRing.IsActive = false;
                FeedList.Visibility = Visibility.Visible;
                _feedLoaded = true;

                if (_feedItems.Count == 0)
                {
                    FeedEmptyText.Text = "No recent videos from your channels";
                    FeedEmptyText.Visibility = Visibility.Visible;
                }
            });
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _feedLoaded = false;
            await LoadFeedAsync();
        }

        private async void FeedList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = e.ClickedItem as VideoResult;
            if (video == null) return;
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl, video.AuthorId);
        }

        // ==================== SEARCH ====================

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
                await PerformSearch();
        }

        private async System.Threading.Tasks.Task PerformSearch()
        {
            var query = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query))
                return;

            string directVideoId = ExtractVideoId(query);
            if (directVideoId != null)
            {
                await PlayVideo(directVideoId, query, "", "", "");
                return;
            }

            // Save search history
            await _data.AddSearchHistoryItem(query);

            _results.Clear();
            ErrorText.Visibility = Visibility.Collapsed;
            SearchHistoryList.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            try
            {
                var results = await _innerTube.SearchAsync(query);
                foreach (var r in results)
                    _results.Add(r);

                if (results.Count == 0)
                {
                    ErrorText.Text = "No results found.";
                    ErrorText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Search failed: " + ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void UpdateSearchHistoryVisibility()
        {
            if (_results.Count == 0 && _data.SearchHistory.Count > 0)
            {
                SearchHistoryList.ItemsSource = _data.SearchHistory.AsEnumerable().Reverse().ToList();
                SearchHistoryList.Visibility = Visibility.Visible;
                ResultsList.Visibility = Visibility.Collapsed;
            }
            else
            {
                SearchHistoryList.Visibility = Visibility.Collapsed;
                ResultsList.Visibility = Visibility.Visible;
            }
        }

        private async void SearchHistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as SearchHistoryItem;
            if (item == null) return;
            SearchBox.Text = item.Query;
            await PerformSearch();
        }

        private async void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = e.ClickedItem as VideoResult;
            if (video == null) return;
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl, video.AuthorId);
        }

        // ==================== FAVORITES ====================

        private void FavChannelsTab_Click(object sender, RoutedEventArgs e)
        {
            _favSubTab = 0;
            UpdateFavoritesView();
        }

        private void FavHistoryTab_Click(object sender, RoutedEventArgs e)
        {
            _favSubTab = 1;
            UpdateFavoritesView();
        }

        private void UpdateFavoritesView()
        {
            if (_favSubTab == 0)
            {
                FavChannelsTab.Background = _accentBrush;
                FavHistoryTab.Background = _darkGrayBrush;
                ChannelsList.Visibility = Visibility.Visible;
                HistoryList.Visibility = Visibility.Collapsed;
                ChannelsList.ItemsSource = _data.Subscriptions;

                if (_data.Subscriptions.Count == 0)
                {
                    FavEmptyText.Text = "No subscribed channels yet";
                    FavEmptyText.Visibility = Visibility.Visible;
                }
                else
                {
                    FavEmptyText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                FavChannelsTab.Background = _darkGrayBrush;
                FavHistoryTab.Background = _accentBrush;
                ChannelsList.Visibility = Visibility.Collapsed;
                HistoryList.Visibility = Visibility.Visible;
                HistoryList.ItemsSource = _data.History.AsEnumerable().Reverse().ToList();

                if (_data.History.Count == 0)
                {
                    FavEmptyText.Text = "No watch history yet";
                    FavEmptyText.Visibility = Visibility.Visible;
                }
                else
                {
                    FavEmptyText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ChannelsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var sub = e.ClickedItem as Subscription;
            if (sub == null) return;
            OpenChannel(sub.AuthorId, sub.Author, sub.ThumbnailUrl);
        }

        private async void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as HistoryItem;
            if (item == null) return;
            await PlayVideo(item.VideoId, item.Title, item.Author, item.ThumbnailUrl, item.AuthorId);
        }

        // ==================== CHANNEL VIEWER ====================

        private async void OpenChannel(string authorId, string author, string thumbnailUrl)
        {
            _isChannelOpen = true;
            ChannelOverlay.Visibility = Visibility.Visible;
            ChannelName.Text = author;
            ChannelLoadingRing.IsActive = true;
            ChannelVideosList.Visibility = Visibility.Collapsed;
            UpdateBackButtonVisibility();

            try
            {
                ChannelAvatar.ImageSource = new BitmapImage(new Uri(thumbnailUrl));
            }
            catch { }

            // Update heart icon
            ChannelHeartIcon.Glyph = _data.IsSubscribed(authorId) ? "\uEB52" : "\uEB51";

            // Store for subscribe toggle
            _currentAuthorId = authorId;
            _currentAuthor = author;
            _currentThumbnailUrl = thumbnailUrl;

            try
            {
                var videos = await _innerTube.GetChannelVideosAsync(authorId);
                ChannelVideosList.ItemsSource = videos;
                ChannelVideosList.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ChannelName.Text = author + " - Error: " + ex.Message;
            }
            finally
            {
                ChannelLoadingRing.IsActive = false;
            }
        }

        private void CloseChannel_Click(object sender, RoutedEventArgs e)
        {
            CloseChannel();
        }

        private void CloseChannel()
        {
            ChannelOverlay.Visibility = Visibility.Collapsed;
            _isChannelOpen = false;
            UpdateBackButtonVisibility();
        }

        private async void ChannelSubscribe_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentAuthorId)) return;
            await _data.ToggleSubscription(_currentAuthorId, _currentAuthor, _currentThumbnailUrl);
            ChannelHeartIcon.Glyph = _data.IsSubscribed(_currentAuthorId) ? "\uEB52" : "\uEB51";
        }

        private async void ChannelVideosList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = e.ClickedItem as VideoResult;
            if (video == null) return;
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl, video.AuthorId);
        }

        // ==================== VIDEO PLAYER ====================

        private async System.Threading.Tasks.Task PlayVideo(string videoId, string title, string author, string thumbnailUrl, string authorId = "")
        {
            _currentVideoId = videoId;
            _currentAuthorId = authorId;
            _currentAuthor = author;
            _currentThumbnailUrl = thumbnailUrl;

            PlayerOverlay.Visibility = Visibility.Visible;
            _isPlayerOpen = true;
            UpdateBackButtonVisibility();

            PlayerTitle.Visibility = Visibility.Collapsed;
            PlayerVideoTitle.Text = title;
            PlayerVideoAuthor.Text = author;
            PlayerLoadingRing.IsActive = true;

            // Update heart icon
            PlayerHeartIcon.Glyph = (!string.IsNullOrEmpty(authorId) && _data.IsSubscribed(authorId)) ? "\uEB52" : "\uEB51";

            // Record history
            await _data.AddHistoryItem(videoId, title, thumbnailUrl, authorId, author);

            try
            {
                PlayerVideoAuthor.Text = "Fetching video info...";
                var playerResponse = await _innerTube.GetPlayerResponseAsync(videoId);

                if (playerResponse.Error != null)
                {
                    PlayerLoadingRing.IsActive = false;
                    ShowDebug(videoId, playerResponse);
                    return;
                }

                _lastPlayerResponse = playerResponse;
                _cachedHlsUrl = playerResponse.HlsManifestUrl;

                var dashMpd = DashManifestGenerator.Generate(playerResponse.Formats);

                var avcVideoFormats = playerResponse.Formats
                    .Where(f => f.IsVideo && !f.IsMuxed && f.Url != null && f.Codecs != null && f.Codecs.StartsWith("avc1"))
                    .OrderByDescending(f => f.Height)
                    .ToList();
                _currentFormats = avcVideoFormats;

                _ignoreQualityChange = true;
                QualitySelector.Items.Clear();

                if (dashMpd != null)
                    QualitySelector.Items.Add("Auto (DASH)");
                if (!string.IsNullOrEmpty(playerResponse.HlsManifestUrl))
                    QualitySelector.Items.Add("Auto (HLS)");

                foreach (var fmt in avcVideoFormats)
                    QualitySelector.Items.Add(fmt.QualityLabel);

                QualitySelector.SelectedIndex = 0;
                _ignoreQualityChange = false;

                string playError = null;

                if (dashMpd != null)
                {
                    try
                    {
                        PlayerVideoAuthor.Text = "Loading DASH stream...";
                        await PlayDash(dashMpd);
                        playError = null;
                    }
                    catch (Exception dashEx)
                    {
                        playError = "DASH: " + dashEx.Message;
                    }
                }

                if (playError != null || dashMpd == null)
                {
                    if (!string.IsNullOrEmpty(playerResponse.HlsManifestUrl))
                    {
                        try
                        {
                            PlayerVideoAuthor.Text = "Loading HLS stream...";
                            await PlayHls(playerResponse.HlsManifestUrl);
                            playError = null;
                        }
                        catch (Exception hlsEx)
                        {
                            playError = (playError ?? "") + "\nHLS: " + hlsEx.Message;
                        }
                    }
                }

                if (playError != null || (dashMpd == null && string.IsNullOrEmpty(playerResponse.HlsManifestUrl)))
                {
                    try
                    {
                        PlayerVideoAuthor.Text = "Fetching muxed format...";
                        var muxedFmt = await _innerTube.GetMuxedFormatAsync(videoId);
                        if (muxedFmt != null)
                        {
                            _cachedMuxedFormat = muxedFmt;
                            if (!QualitySelector.Items.Contains("360p (muxed)"))
                            {
                                _ignoreQualityChange = true;
                                QualitySelector.Items.Add("360p (muxed)");
                                _ignoreQualityChange = false;
                            }
                            PlayerVideoAuthor.Text = "Downloading 360p...";
                            await PlayMuxedUrl(muxedFmt.Url, muxedFmt.ContentLength);
                            playError = null;
                        }
                    }
                    catch (Exception muxedEx)
                    {
                        playError = (playError ?? "") + "\nMuxed: " + muxedEx.Message;
                    }
                }

                if (playError != null)
                {
                    playerResponse.Error = playError;
                    ShowDebug(videoId, playerResponse);
                    PlayerLoadingRing.IsActive = false;
                    return;
                }

                PlayerVideoAuthor.Text = author;
                PlayerLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                PlayerVideoAuthor.Text = "Error: " + ex.Message;
                PlayerLoadingRing.IsActive = false;
            }
        }

        private async System.Threading.Tasks.Task PlayHls(string hlsUrl)
        {
            _lastPlayedUrl = hlsUrl;
            var hlsSource = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(hlsUrl), _streamClient);
            if (hlsSource.Status == AdaptiveMediaSourceCreationStatus.Success)
            {
                var mediaSource = MediaSource.CreateFromAdaptiveMediaSource(hlsSource.MediaSource);
                VideoPlayer.Source = mediaSource;
            }
            else
            {
                throw new Exception("HLS failed: " + hlsSource.Status);
            }
        }

        private async System.Threading.Tasks.Task PlayDash(string dashMpd)
        {
            _lastPlayedUrl = "DASH manifest (local)";

            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(dashMpd);
            _currentStream = new InMemoryRandomAccessStream();
            await _currentStream.WriteAsync(bytes.AsBuffer());
            _currentStream.Seek(0);

            var dashSource = await AdaptiveMediaSource.CreateFromStreamAsync(
                _currentStream,
                new System.Uri("https://www.youtube.com/dash"),
                "application/dash+xml",
                _streamClient);

            if (dashSource.Status == AdaptiveMediaSourceCreationStatus.Success)
            {
                var adaptiveSource = dashSource.MediaSource;
                adaptiveSource.InitialBitrate = adaptiveSource.AvailableBitrates.Max();
                adaptiveSource.DownloadRequested += OnDashDownloadRequested;
                var mediaSource = MediaSource.CreateFromAdaptiveMediaSource(adaptiveSource);
                VideoPlayer.Source = mediaSource;
            }
            else
            {
                throw new Exception("DASH failed: " + dashSource.Status);
            }
        }

        private async void OnDashDownloadRequested(AdaptiveMediaSource sender, AdaptiveMediaSourceDownloadRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var reqMsg = new Windows.Web.Http.HttpRequestMessage(
                    Windows.Web.Http.HttpMethod.Get, args.ResourceUri);

                if (args.ResourceByteRangeOffset.HasValue && args.ResourceByteRangeLength.HasValue)
                {
                    var start = args.ResourceByteRangeOffset.Value;
                    var end = start + args.ResourceByteRangeLength.Value - 1;
                    reqMsg.Headers.TryAppendWithoutValidation("Range", "bytes=" + start + "-" + end);
                }
                else if (args.ResourceByteRangeOffset.HasValue)
                {
                    var start = args.ResourceByteRangeOffset.Value;
                    reqMsg.Headers.TryAppendWithoutValidation("Range", "bytes=" + start + "-" + (start + 10485759));
                }
                else
                {
                    reqMsg.Headers.TryAppendWithoutValidation("Range", "bytes=0-10485759");
                }

                var response = await _streamClient.SendRequestAsync(reqMsg);
                var buffer = await response.Content.ReadAsBufferAsync();
                args.Result.Buffer = buffer;
            }
            catch { }
            finally
            {
                deferral.Complete();
            }
        }

        private async System.Threading.Tasks.Task PlayDirectUrl(string url, string mimeType = "video/mp4", long contentLength = 0)
        {
            _lastPlayedUrl = url;

            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            var reqMsg = new Windows.Web.Http.HttpRequestMessage(
                Windows.Web.Http.HttpMethod.Get, new Uri(url));
            long rangeEnd = contentLength > 0 ? contentLength - 1 : 500000000;
            reqMsg.Headers.TryAppendWithoutValidation("Range", "bytes=0-" + rangeEnd);

            var response = await _streamClient.SendRequestAsync(reqMsg);
            var buffer = await response.Content.ReadAsBufferAsync();
            _currentStream = new InMemoryRandomAccessStream();
            await _currentStream.WriteAsync(buffer);
            _currentStream.Seek(0);

            var baseMime = mimeType;
            var semiIdx = baseMime.IndexOf(';');
            if (semiIdx >= 0) baseMime = baseMime.Substring(0, semiIdx).Trim();

            VideoPlayer.Source = MediaSource.CreateFromStream(_currentStream, baseMime);
        }

        private async System.Threading.Tasks.Task PlayMuxedUrl(string url, long contentLength = 0)
        {
            _lastPlayedUrl = url;

            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            var reqMsg = new Windows.Web.Http.HttpRequestMessage(
                Windows.Web.Http.HttpMethod.Get, new Uri(url));
            long rangeEnd = contentLength > 0 ? contentLength - 1 : 500000000;
            reqMsg.Headers.TryAppendWithoutValidation("Range", "bytes=0-" + rangeEnd);

            var response = await _streamClient.SendRequestAsync(reqMsg);
            var buffer = await response.Content.ReadAsBufferAsync();
            _currentStream = new InMemoryRandomAccessStream();
            await _currentStream.WriteAsync(buffer);
            _currentStream.Seek(0);

            VideoPlayer.Source = MediaSource.CreateFromStream(_currentStream, "video/mp4");
        }

        private async void MediaPlayer_MediaFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                PlayerLoadingRing.IsActive = false;

                var info = "=== MEDIA PLAYBACK ERROR ===\n\n";
                info += "Error: " + args.Error + "\n";
                info += "Message: " + (args.ErrorMessage ?? "(none)") + "\n";
                info += "Extended code: 0x" + (args.ExtendedErrorCode?.HResult.ToString("X8") ?? "?") + "\n";
                info += "Played URL: " + (_lastPlayedUrl ?? "null") + "\n";
                info += "VideoId: " + (_currentVideoId ?? "null") + "\n\n";

                if (_lastPlayerResponse != null)
                {
                    info += "HLS URL: " + (_lastPlayerResponse.HlsManifestUrl ?? "null") + "\n";
                    info += "Formats found: " + _lastPlayerResponse.Formats.Count + "\n";
                    foreach (var f in _lastPlayerResponse.Formats)
                    {
                        info += "  - itag " + f.Itag + " " + f.QualityLabel + " " + f.MimeType
                            + (f.IsMuxed ? " [muxed]" : "") + " url=" + (f.Url != null ? "yes" : "no") + "\n";
                    }
                    var dashMpd = DashManifestGenerator.Generate(_lastPlayerResponse.Formats);
                    if (dashMpd != null)
                    {
                        info += "\n=== DASH MANIFEST ===\n" + dashMpd + "\n";
                    }
                    info += "\n=== RAW RESPONSE ===\n";
                    info += _lastPlayerResponse.RawJson ?? "";
                }

                DebugText.Text = info;
                DebugOverlay.Visibility = Visibility.Visible;
            });
        }

        private async void QualitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreQualityChange || QualitySelector.SelectedIndex < 0)
                return;

            var selectedText = QualitySelector.SelectedItem as string;
            if (selectedText == null)
                return;

            try
            {
                if (selectedText == "Auto (DASH)" && _lastPlayerResponse != null)
                {
                    var dashMpd = DashManifestGenerator.Generate(_lastPlayerResponse.Formats);
                    if (dashMpd != null)
                        await PlayDash(dashMpd);
                }
                else if (selectedText == "Auto (HLS)" && !string.IsNullOrEmpty(_cachedHlsUrl))
                {
                    await PlayHls(_cachedHlsUrl);
                }
                else if (selectedText == "360p (muxed)" && _cachedMuxedFormat != null)
                {
                    PlayerVideoAuthor.Text = "Downloading 360p...";
                    await PlayMuxedUrl(_cachedMuxedFormat.Url, _cachedMuxedFormat.ContentLength);
                    PlayerVideoAuthor.Text = "";
                }
                else if (_currentFormats != null)
                {
                    int offset = 0;
                    if (QualitySelector.Items.Contains("Auto (DASH)")) offset++;
                    if (QualitySelector.Items.Contains("Auto (HLS)")) offset++;
                    if (QualitySelector.Items.Contains("360p (muxed)")) offset++;
                    int formatIdx = QualitySelector.SelectedIndex - offset;
                    if (formatIdx >= 0 && formatIdx < _currentFormats.Count)
                    {
                        var fmt = _currentFormats[formatIdx];
                        PlayerVideoAuthor.Text = "Downloading " + fmt.QualityLabel + "...";
                        await PlayDirectUrl(fmt.Url, fmt.MimeType, fmt.ContentLength);
                        PlayerVideoAuthor.Text = "";
                    }
                }
            }
            catch (Exception ex)
            {
                PlayerVideoAuthor.Text = "Quality change error: " + ex.Message;
            }
        }

        // ==================== PLAYER BUTTONS ====================

        private async void PlayerSubscribe_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentAuthorId)) return;
            await _data.ToggleSubscription(_currentAuthorId, _currentAuthor, _currentThumbnailUrl);
            PlayerHeartIcon.Glyph = _data.IsSubscribed(_currentAuthorId) ? "\uEB52" : "\uEB51";
        }

        private void PlayerShare_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentVideoId)) return;
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText("https://youtu.be/" + _currentVideoId);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            // Brief visual feedback
            PlayerVideoAuthor.Text = "Link copied!";
        }

        // ==================== DEBUG ====================

        private void ShowDebug(string videoId, PlayerResponse result)
        {
            var info = "=== PLAYER DEBUG ===\n\n";
            info += "VideoId: " + videoId + "\n";
            info += "HTTP Status: " + result.StatusCode + "\n";
            info += "Error: " + (result.Error ?? "none") + "\n";
            info += "HLS URL: " + (result.HlsManifestUrl ?? "null") + "\n";
            info += "Formats found: " + result.Formats.Count + "\n\n";
            info += "=== REQUEST BODY ===\n" + (result.RequestBody ?? "") + "\n\n";

            var dashMpd = DashManifestGenerator.Generate(result.Formats);
            if (dashMpd != null)
            {
                info += "=== DASH MANIFEST ===\n" + dashMpd + "\n\n";
            }

            info += "=== RAW RESPONSE ===\n";
            info += result.RawJson ?? "";

            DebugText.Text = info;
            DebugOverlay.Visibility = Visibility.Visible;
        }

        private void CloseDebug_Click(object sender, RoutedEventArgs e)
        {
            DebugOverlay.Visibility = Visibility.Collapsed;
        }

        private void CopyDebug_Click(object sender, RoutedEventArgs e)
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(DebugText.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        }

        private async void SaveDebug_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });
            picker.SuggestedFileName = "debug_" + (_currentVideoId ?? "log");
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await Windows.Storage.FileIO.WriteTextAsync(file, DebugText.Text);
            }
        }

        // ==================== CLOSE / BACK ====================

        private void ClosePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            ClosePlayer();
        }

        private void ClosePlayer()
        {
            VideoPlayer.Source = null;
            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }
            PlayerOverlay.Visibility = Visibility.Collapsed;
            DebugOverlay.Visibility = Visibility.Collapsed;
            _isPlayerOpen = false;
            _currentFormats = null;
            UpdateBackButtonVisibility();
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (DebugOverlay.Visibility == Visibility.Visible)
            {
                DebugOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            else if (_isPlayerOpen)
            {
                ClosePlayer();
                e.Handled = true;
            }
            else if (_isChannelOpen)
            {
                CloseChannel();
                e.Handled = true;
            }
            else if (_activeTab != 0)
            {
                ShowFeed();
                e.Handled = true;
            }
        }

        private void UpdateBackButtonVisibility()
        {
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                (_isPlayerOpen || _isChannelOpen || _activeTab != 0)
                    ? AppViewBackButtonVisibility.Visible
                    : AppViewBackButtonVisibility.Collapsed;
        }

        private string ExtractVideoId(string input)
        {
            if (input.StartsWith("https://youtu.be/", StringComparison.OrdinalIgnoreCase))
            {
                var id = input.Substring("https://youtu.be/".Length);
                var qIdx = id.IndexOf('?');
                if (qIdx >= 0) id = id.Substring(0, qIdx);
                if (id.Length >= 11) return id.Substring(0, 11);
            }

            if (input.Contains("youtube.com/watch"))
            {
                try
                {
                    var uri = new Uri(input);
                    var query = uri.Query;
                    if (query.StartsWith("?")) query = query.Substring(1);
                    foreach (var param in query.Split('&'))
                    {
                        var parts = param.Split('=');
                        if (parts.Length == 2 && parts[0] == "v" && parts[1].Length >= 11)
                            return parts[1].Substring(0, 11);
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
