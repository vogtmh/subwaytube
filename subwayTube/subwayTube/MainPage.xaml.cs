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
using Windows.UI.Xaml.Media;
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
        private readonly ObservableCollection<VideoResult> _channelResults = new ObservableCollection<VideoResult>();
        private readonly ObservableCollection<VideoResult> _feedItems = new ObservableCollection<VideoResult>();
        private readonly List<VideoResult> _allFeedVideos = new List<VideoResult>();
        private const int FeedBatchSize = 30; // videos rendered per lazy-load batch
        private const int FeedMaxTotal = 150; // hard cap on total videos kept in the feed
        private ScrollViewer _feedScrollViewer;
        private bool _isPlayerOpen;
        private int _playSession; // increments each time a video is opened/closed to invalidate stale playback
        private bool _isChannelOpen;
        private List<StreamFormat> _currentFormats;
        private string _currentVideoId;
        private string _currentAuthorId;
        private string _currentAuthor;
        private string _currentThumbnailUrl;
        private List<ChannelRef> _currentChannels;
        private bool _ignoreQualityChange;
        private PlayerResponse _lastPlayerResponse;
        private string _lastPlayedUrl;
        private IRandomAccessStream _currentStream;
        private string _cachedHlsUrl;
        private StreamFormat _cachedMuxedFormat;
        private readonly Windows.Web.Http.HttpClient _streamClient;
        private readonly Windows.System.Display.DisplayRequest _displayRequest = new Windows.System.Display.DisplayRequest();
        private bool _displayRequestActive;
        private int _activeTab; // 0=Feed, 1=Search, 2=Favorites
        private int _favSubTab; // 0=Channels, 1=History
        private int _searchTab; // 0=Videos, 1=Shorts, 2=Channels
        private int _channelTab; // 0=Videos, 1=Shorts
        private bool _feedLoaded;
        private int _scaleMode; // 0=full, 1=half (2 per row), 2=third (3 per row)
        private static readonly Windows.Storage.ApplicationDataContainer _localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _darkBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 17, 17));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _darkGrayBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 51));
        private static readonly Windows.UI.Xaml.Media.SolidColorBrush _accentBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 215));

        public MainPage()
        {
            this.InitializeComponent();
            ResultsList.ItemsSource = _results;
            ChannelResultsList.ItemsSource = _channelResults;
            FeedList.ItemsSource = _feedItems;

            _streamClient = new Windows.Web.Http.HttpClient(new IosUserAgentFilter());

            this.SizeChanged += Page_SizeChanged;

            // Restore saved scaling mode
            if (_localSettings.Values.ContainsKey("scaleMode"))
                _scaleMode = (int)_localSettings.Values["scaleMode"];

            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
            VideoPlayer.MediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            VideoPlayer.MediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;

            // Load data and show feed
            InitAsync();
        }

        private async void InitAsync()
        {
            await _data.LoadAllAsync();
            ChannelsList.ItemsSource = _data.Subscriptions;
            HistoryList.ItemsSource = _data.History;
            ShowFeed();
            await LoadFeedAsync();
        }

        // ==================== SCALING ====================

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyScaling();
        }

        private void ScaleButton_Click(object sender, RoutedEventArgs e)
        {
            _scaleMode = (_scaleMode + 1) % 3;
            _localSettings.Values["scaleMode"] = _scaleMode;
            ApplyScaling();
        }

        private void ApplyScaling()
        {
            double pageWidth = this.ActualWidth;
            if (pageWidth <= 0) return;

            double itemWidth;

            switch (_scaleMode)
            {
                case 1: // 2 per row
                    itemWidth = pageWidth / 2 - 2;
                    break;
                case 2: // 3 per row
                    itemWidth = pageWidth / 3 - 2;
                    break;
                default: // full width
                    itemWidth = pageWidth - 2;
                    break;
            }

            // Show the matching size icon (reused from the old app): full / quarters / niners
            string sizeIcon = _scaleMode == 0 ? "display_fullsize"
                : _scaleMode == 1 ? "display_quarters"
                : "display_niners";
            ScaleImage.Source = new BitmapImage(new Uri("ms-appx:///Assets/" + sizeIcon + ".png"));

            // Apply to all GridViews
            ApplyGridViewSize(FeedList, itemWidth);
            ApplyGridViewSize(ResultsList, itemWidth);
            ApplyGridViewSize(ChannelVideosList, itemWidth);
        }

        private void ApplyGridViewSize(GridView gridView, double itemWidth)
        {
            if (gridView == null) return;
            var panel = gridView.ItemsPanelRoot as ItemsWrapGrid;
            if (panel != null)
            {
                panel.ItemWidth = itemWidth;
            }
        }

        // ==================== TAB SWITCHING ====================

        private void NavFeed_Click(object sender, RoutedEventArgs e) { ShowFeed(); }
        private void NavSearch_Click(object sender, RoutedEventArgs e) { ShowSearch(); }
        private void NavFavorites_Click(object sender, RoutedEventArgs e) { ShowFavorites(); }
        private void NavSettings_Click(object sender, RoutedEventArgs e) { ShowSettings(); }

        private void ShowFeed()
        {
            _activeTab = 0;
            ResetSearch();
            FeedPanel.Visibility = Visibility.Visible;
            SearchPanel.Visibility = Visibility.Collapsed;
            FavoritesPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            NavFeed.Background = _accentBrush;
            NavSearch.Background = _darkBrush;
            NavFavorites.Background = _darkBrush;
            NavSettings.Background = _darkBrush;
            RefreshButton.Visibility = Visibility.Visible;
            ScaleButton.Visibility = Visibility.Visible;
            UpdateBackButtonVisibility();
        }

        private void ShowSearch()
        {
            _activeTab = 1;
            FeedPanel.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Visible;
            FavoritesPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            NavFeed.Background = _darkBrush;
            NavSearch.Background = _accentBrush;
            NavFavorites.Background = _darkBrush;
            NavSettings.Background = _darkBrush;
            RefreshButton.Visibility = Visibility.Collapsed;
            ScaleButton.Visibility = Visibility.Visible;
            UpdateSearchHistoryVisibility();
            UpdateBackButtonVisibility();
        }

        private void ShowFavorites()
        {
            _activeTab = 2;
            ResetSearch();
            FeedPanel.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Collapsed;
            FavoritesPanel.Visibility = Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Collapsed;
            NavFeed.Background = _darkBrush;
            NavSearch.Background = _darkBrush;
            NavFavorites.Background = _accentBrush;
            NavSettings.Background = _darkBrush;
            RefreshButton.Visibility = Visibility.Collapsed;
            ScaleButton.Visibility = Visibility.Collapsed;
            UpdateFavoritesView();
            UpdateBackButtonVisibility();
        }

        private void ShowSettings()
        {
            _activeTab = 3;
            ResetSearch();
            FeedPanel.Visibility = Visibility.Collapsed;
            SearchPanel.Visibility = Visibility.Collapsed;
            FavoritesPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            NavFeed.Background = _darkBrush;
            NavSearch.Background = _darkBrush;
            NavFavorites.Background = _darkBrush;
            NavSettings.Background = _accentBrush;
            RefreshButton.Visibility = Visibility.Collapsed;
            ScaleButton.Visibility = Visibility.Collapsed;
            SettingsStatus.Text = "";
            UpdateSettingsInfo();
            UpdateBackButtonVisibility();
        }

        // ==================== SETTINGS ====================

        private void UpdateSettingsInfo()
        {
            var package = Windows.ApplicationModel.Package.Current;
            SettingsProductName.Text = package.DisplayName;
            var v = package.Id.Version;
            SettingsVersion.Text = "Version " + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;

            SettingsBuildDate.Text = "Built " + BuildInfo.Date;
            UpdateDownloadFolderDisplay();
        }

        private async void UpdateDownloadFolderDisplay()
        {
            string token = _localSettings.Values["downloadFolderToken"] as string;
            if (string.IsNullOrEmpty(token) ||
                !Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
            {
                DownloadFolderPath.Text = "Not set";
                DownloadFolderUsage.Text = "";
                return;
            }

            try
            {
                var folder = await Windows.Storage.AccessCache.StorageApplicationPermissions
                    .FutureAccessList.GetFolderAsync(token);
                DownloadFolderPath.Text = folder.Path;

                DownloadFolderUsage.Text = "Calculating usage...";
                ulong totalBytes = await GetFolderSizeAsync(folder);
                DownloadFolderUsage.Text = "Used: " + FormatBytes(totalBytes);
            }
            catch
            {
                DownloadFolderPath.Text = "Not set";
                DownloadFolderUsage.Text = "";
            }
        }

        private static async System.Threading.Tasks.Task<ulong> GetFolderSizeAsync(Windows.Storage.StorageFolder folder)
        {
            ulong total = 0;
            var files = await folder.GetFilesAsync(
                Windows.Storage.Search.CommonFileQuery.OrderByName);
            foreach (var file in files)
            {
                var props = await file.GetBasicPropertiesAsync();
                total += props.Size;
            }

            var subFolders = await folder.GetFoldersAsync();
            foreach (var sub in subFolders)
                total += await GetFolderSizeAsync(sub);

            return total;
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0
                ? bytes + " " + units[unit]
                : size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
        }

        private async void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = "subwayTube-backup-" + DateTime.Now.ToString("yyyy-MM-dd")
                };
                picker.FileTypeChoices.Add("subwayTube backup", new List<string> { ".json" });

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                    return;

                var json = _data.ExportBackup();
                await Windows.Storage.FileIO.WriteTextAsync(file, json);
                SettingsStatus.Text = "Backup saved to " + file.Name;
            }
            catch (Exception ex)
            {
                SettingsStatus.Text = "Backup failed: " + ex.Message;
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
                };
                picker.FileTypeFilter.Add(".json");

                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return;

                var json = await Windows.Storage.FileIO.ReadTextAsync(file);
                bool ok = await _data.ImportBackupAsync(json);
                if (ok)
                {
                    UpdateFavoritesView();
                    await LoadFeedAsync();
                    SettingsStatus.Text = "Backup restored.";
                }
                else
                {
                    SettingsStatus.Text = "Restore failed: invalid backup file.";
                }
            }
            catch (Exception ex)
            {
                SettingsStatus.Text = "Restore failed: " + ex.Message;
            }
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Clear history",
                Content = "Are you sure you want to clear your watch history?",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await _data.ClearHistory();
            UpdateFavoritesView();
            SettingsStatus.Text = "History cleared.";
        }

        private async void ChangeDownloadFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker
                {
                    SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
                };
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                if (folder == null)
                    return;

                string token = Windows.Storage.AccessCache.StorageApplicationPermissions
                    .FutureAccessList.Add(folder);
                _localSettings.Values["downloadFolderToken"] = token;
                DownloadFolderPath.Text = folder.Path;
                SettingsStatus.Text = "Download folder updated.";
            }
            catch (Exception ex)
            {
                SettingsStatus.Text = "Could not set folder: " + ex.Message;
            }
        }

        // ==================== DOWNLOADS ====================

        private bool _isDownloading;

        /// <summary>
        /// Updates the player download icon to reflect whether the given video is
        /// already downloaded (filled/accent) or not (outline).
        /// </summary>
        private void UpdateDownloadIcon(string videoId)
        {
            bool downloaded = !string.IsNullOrEmpty(videoId) && _data.IsDownloaded(videoId);
            // E73E = completed/checkmark box, E896 = download arrow
            PlayerDownloadIcon.Glyph = downloaded ? "\uE73E" : "\uE896";
            PlayerDownloadIcon.Foreground = downloaded
                ? _accentBrush
                : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.White);
        }

        /// <summary>
        /// Returns the configured download folder, or null if none is set / no longer
        /// accessible. Pass promptIfMissing to ask the user to pick one first.
        /// </summary>
        private async System.Threading.Tasks.Task<Windows.Storage.StorageFolder> GetDownloadFolderAsync(bool promptIfMissing)
        {
            string token = _localSettings.Values["downloadFolderToken"] as string;
            if (!string.IsNullOrEmpty(token) &&
                Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
            {
                try
                {
                    return await Windows.Storage.AccessCache.StorageApplicationPermissions
                        .FutureAccessList.GetFolderAsync(token);
                }
                catch { }
            }

            if (!promptIfMissing)
                return null;

            var dialog = new ContentDialog
            {
                Title = "No download folder",
                Content = "Please select a folder for your downloads first.",
                PrimaryButtonText = "Select folder",
                CloseButtonText = "Cancel"
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return null;

            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
            };
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
                return null;

            string newToken = Windows.Storage.AccessCache.StorageApplicationPermissions
                .FutureAccessList.Add(folder);
            _localSettings.Values["downloadFolderToken"] = newToken;
            return folder;
        }

        private async void PlayerDownload_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentVideoId))
                return;
            await StartDownloadAsync(_currentVideoId, _currentAuthor, PlayerVideoTitle.Text,
                _currentAuthorId, _currentThumbnailUrl);
        }

        private async void ThumbnailDownload_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var video = btn?.Tag as VideoResult;
            if (video == null)
                return;
            await StartDownloadAsync(video.VideoId, video.Author, video.Title,
                video.AuthorId, video.ThumbnailUrl);
        }

        private async System.Threading.Tasks.Task StartDownloadAsync(
            string videoId, string author, string title, string authorId, string thumbnailUrl)
        {
            if (_isDownloading)
            {
                await ShowMessageAsync("Download in progress", "Please wait for the current download to finish.");
                return;
            }

            // Already downloaded? Indicate and ask for confirmation before re-downloading.
            if (_data.IsDownloaded(videoId))
            {
                var confirm = new ContentDialog
                {
                    Title = "Already downloaded",
                    Content = "\"" + title + "\" is already downloaded. Download it again?",
                    PrimaryButtonText = "Download again",
                    CloseButtonText = "Cancel"
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                    return;

                // Remove the previous file so we don't leave an orphaned copy behind.
                var existing = _data.GetDownload(videoId);
                if (existing != null)
                {
                    try
                    {
                        var prevFolder = await GetDownloadFolderAsync(false);
                        if (prevFolder != null)
                        {
                            var prevFile = await prevFolder.GetFileAsync(existing.FileName);
                            await prevFile.DeleteAsync();
                        }
                    }
                    catch { }
                }
            }

            var folder = await GetDownloadFolderAsync(true);
            if (folder == null)
                return;

            _isDownloading = true;
            DownloadProgress.Value = 0;
            DownloadProgressText.Text = "Preparing: " + title;
            DownloadProgressBar.Visibility = Visibility.Visible;

            try
            {
                var muxedFmt = await _innerTube.GetMuxedFormatAsync(videoId);
                if (muxedFmt == null || string.IsNullOrEmpty(muxedFmt.Url))
                {
                    await ShowMessageAsync("Download failed", "No downloadable 360p stream is available for this video.");
                    return;
                }

                string fileName = SanitizeFileName(title, videoId) + ".mp4";
                var file = await folder.CreateFileAsync(fileName, Windows.Storage.CreationCollisionOption.GenerateUniqueName);

                long totalBytes = await DownloadToFileAsync(muxedFmt.Url, file, title);

                // Cache thumbnail locally for offline display
                string thumbLocalUri = await CacheThumbnailAsync(videoId, thumbnailUrl);

                var record = new DownloadItem
                {
                    VideoId = videoId,
                    Title = title,
                    Author = author,
                    AuthorId = authorId,
                    ThumbnailUrl = thumbnailUrl,
                    ThumbnailLocalUri = thumbLocalUri,
                    FileName = file.Name,
                    FileSize = totalBytes
                };
                await _data.AddDownloadRecord(record);

                if (_activeTab == 2 && _favSubTab == 2)
                    UpdateFavoritesView();

                if (_isPlayerOpen && _currentVideoId == videoId)
                    UpdateDownloadIcon(videoId);

                DownloadProgressText.Text = "Downloaded: " + title;
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Download failed", ex.Message);
            }
            finally
            {
                _isDownloading = false;
                DownloadProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async System.Threading.Tasks.Task<long> DownloadToFileAsync(
            string url, Windows.Storage.StorageFile file, string title)
        {
            // Probe total size with a tiny range request
            ulong totalSize = 0;
            try
            {
                var probe = new Windows.Web.Http.HttpRequestMessage(
                    Windows.Web.Http.HttpMethod.Get, new Uri(url));
                probe.Headers.TryAppendWithoutValidation("Range", "bytes=0-0");
                var probeResp = await _streamClient.SendRequestAsync(
                    probe, Windows.Web.Http.HttpCompletionOption.ResponseHeadersRead);
                if (probeResp.Content.Headers.ContentRange != null &&
                    probeResp.Content.Headers.ContentRange.Length.HasValue)
                    totalSize = probeResp.Content.Headers.ContentRange.Length.Value;
                probeResp.Dispose();
            }
            catch { }

            long written = 0;
            const int chunkSize = 2 * 1024 * 1024; // 2 MB chunks

            using (var fileStream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            using (var output = fileStream.GetOutputStreamAt(0))
            {
                while (true)
                {
                    long start = written;
                    long end = start + chunkSize - 1;

                    var req = new Windows.Web.Http.HttpRequestMessage(
                        Windows.Web.Http.HttpMethod.Get, new Uri(url));
                    req.Headers.TryAppendWithoutValidation("Range", "bytes=" + start + "-" + end);

                    var resp = await _streamClient.SendRequestAsync(
                        req, Windows.Web.Http.HttpCompletionOption.ResponseHeadersRead);
                    var buffer = await resp.Content.ReadAsBufferAsync();
                    resp.Dispose();

                    if (buffer.Length == 0)
                        break;

                    await output.WriteAsync(buffer);
                    written += buffer.Length;

                    if (totalSize > 0)
                    {
                        double pct = (double)written / totalSize * 100.0;
                        DownloadProgress.Value = pct > 100 ? 100 : pct;
                        DownloadProgressText.Text = title + " — " + (int)DownloadProgress.Value + "%";
                    }
                    else
                    {
                        DownloadProgressText.Text = title + " — " + FormatBytes((ulong)written);
                    }

                    // Last chunk was smaller than requested -> end of file
                    if (totalSize > 0 && (ulong)written >= totalSize)
                        break;
                    if (buffer.Length < (uint)chunkSize)
                        break;
                }

                await output.FlushAsync();
            }

            return written;
        }

        private async System.Threading.Tasks.Task<string> CacheThumbnailAsync(string videoId, string thumbnailUrl)
        {
            if (string.IsNullOrEmpty(thumbnailUrl))
                return null;
            try
            {
                var thumbsFolder = await Windows.Storage.ApplicationData.Current.LocalFolder
                    .CreateFolderAsync("thumbs", Windows.Storage.CreationCollisionOption.OpenIfExists);
                var thumbFile = await thumbsFolder.CreateFileAsync(
                    videoId + ".jpg", Windows.Storage.CreationCollisionOption.ReplaceExisting);

                var resp = await _streamClient.GetAsync(new Uri(thumbnailUrl));
                var buffer = await resp.Content.ReadAsBufferAsync();
                await Windows.Storage.FileIO.WriteBufferAsync(thumbFile, buffer);
                return thumbFile.Path;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFileName(string title, string fallback)
        {
            if (string.IsNullOrWhiteSpace(title))
                return fallback;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in title)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            var name = sb.ToString().Trim();
            if (name.Length > 80)
                name = name.Substring(0, 80);
            return string.IsNullOrEmpty(name) ? fallback : name;
        }

        private async void DownloadsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as DownloadItem;
            if (item == null)
                return;
            await PlayDownloadedAsync(item);
        }

        private async System.Threading.Tasks.Task PlayDownloadedAsync(DownloadItem item)
        {
            var folder = await GetDownloadFolderAsync(false);
            if (folder == null)
            {
                await ShowMessageAsync("Cannot play", "The download folder is no longer accessible.");
                return;
            }

            Windows.Storage.StorageFile file;
            try
            {
                file = await folder.GetFileAsync(item.FileName);
            }
            catch
            {
                await ShowMessageAsync("File missing", "The downloaded file could not be found. It may have been moved or deleted.");
                return;
            }

            int session = ++_playSession;

            _currentVideoId = item.VideoId;
            _currentAuthorId = item.AuthorId;
            _currentAuthor = item.Author;
            _currentThumbnailUrl = item.ThumbnailUrl;
            _currentChannels = null;

            PlayerOverlay.Visibility = Visibility.Visible;
            _isPlayerOpen = true;
            UpdateBackButtonVisibility();

            PlayerTitle.Visibility = Visibility.Collapsed;
            PlayerVideoTitle.Text = item.Title;
            PlayerVideoAuthor.Text = item.Author;
            PlayerLoadingRing.IsActive = false;
            PlayerHeartIcon.Glyph = (!string.IsNullOrEmpty(item.AuthorId) && _data.IsSubscribed(item.AuthorId)) ? "\uEB52" : "\uEB51";
            UpdateDownloadIcon(item.VideoId);

            _ignoreQualityChange = true;
            QualitySelector.Items.Clear();
            QualitySelector.Items.Add("360p (offline)");
            QualitySelector.SelectedIndex = 0;
            _ignoreQualityChange = false;

            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            if (session != _playSession || !_isPlayerOpen)
                return;

            _lastPlayedUrl = file.Path;
            VideoPlayer.Source = MediaSource.CreateFromStorageFile(file);
        }

        private async void DownloadDelete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as DownloadItem;
            if (item == null)
                return;

            var dialog = new ContentDialog
            {
                Title = "Delete download",
                Content = "Delete \"" + item.Title + "\" from your downloads?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel"
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            // Delete the video file
            try
            {
                var folder = await GetDownloadFolderAsync(false);
                if (folder != null)
                {
                    var file = await folder.GetFileAsync(item.FileName);
                    await file.DeleteAsync();
                }
            }
            catch { }

            // Delete the cached thumbnail
            try
            {
                if (!string.IsNullOrEmpty(item.ThumbnailLocalUri))
                {
                    var thumbFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.ThumbnailLocalUri);
                    await thumbFile.DeleteAsync();
                }
            }
            catch { }

            await _data.RemoveDownloadRecord(item.VideoId);
            UpdateFavoritesView();
        }

        private async System.Threading.Tasks.Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
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

            // Sort by published date (newest first), keep up to FeedMaxTotal
            var sorted = allVideos
                .OrderBy(v => ParsePublishedAgeMinutes(v.PublishedText))
                .Take(FeedMaxTotal)
                .ToList();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _allFeedVideos.Clear();
                _allFeedVideos.AddRange(sorted);

                _feedItems.Clear();
                // Render only the first batch; the rest load on scroll
                foreach (var v in _allFeedVideos.Take(FeedBatchSize))
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

        /// <summary>
        /// Appends the next batch of feed videos to the visible list (lazy loading).
        /// </summary>
        private void LoadMoreFeedItems()
        {
            int alreadyShown = _feedItems.Count;
            if (alreadyShown >= _allFeedVideos.Count)
                return; // everything is already displayed

            foreach (var v in _allFeedVideos.Skip(alreadyShown).Take(FeedBatchSize))
                _feedItems.Add(v);
        }

        /// <summary>
        /// Hooks into the GridView's internal ScrollViewer so we can detect when the
        /// user nears the bottom and load the next batch of videos.
        /// </summary>
        private void FeedList_Loaded(object sender, RoutedEventArgs e)
        {
            if (_feedScrollViewer != null)
                return;

            _feedScrollViewer = FindChildScrollViewer(FeedList);
            if (_feedScrollViewer != null)
                _feedScrollViewer.ViewChanged += FeedScrollViewer_ViewChanged;
        }

        private void FeedScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_feedScrollViewer == null)
                return;

            // Trigger the next batch when within ~1.5 viewport heights of the bottom
            double distanceToBottom = _feedScrollViewer.ScrollableHeight - _feedScrollViewer.VerticalOffset;
            if (distanceToBottom <= _feedScrollViewer.ViewportHeight * 1.5)
                LoadMoreFeedItems();
        }

        private static ScrollViewer FindChildScrollViewer(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                    return sv;

                var result = FindChildScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// Parses a relative published string (e.g. "4 hours ago", "2 days ago",
        /// "1 month ago") into an approximate age in minutes for sorting.
        /// Smaller value = newer. Unparseable strings sort to the end.
        /// </summary>
        private static double ParsePublishedAgeMinutes(string publishedText)
        {
            if (string.IsNullOrEmpty(publishedText))
                return double.MaxValue;

            var text = publishedText.ToLowerInvariant();

            // Handle "Streamed X ago" / "Premiered X ago" prefixes
            int agoIdx = text.IndexOf(" ago");
            if (agoIdx < 0)
                return double.MaxValue;

            // Find the first number in the string
            var parts = text.Split(' ');
            double number = 0;
            string unit = null;
            for (int i = 0; i < parts.Length; i++)
            {
                if (double.TryParse(parts[i], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out number))
                {
                    if (i + 1 < parts.Length)
                        unit = parts[i + 1];
                    break;
                }
            }

            if (unit == null)
                return double.MaxValue;

            if (unit.StartsWith("second")) return number / 60.0;
            if (unit.StartsWith("minute")) return number;
            if (unit.StartsWith("hour")) return number * 60.0;
            if (unit.StartsWith("day")) return number * 1440.0;
            if (unit.StartsWith("week")) return number * 10080.0;
            if (unit.StartsWith("month")) return number * 43200.0;
            if (unit.StartsWith("year")) return number * 525600.0;

            return double.MaxValue;
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
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl, video.AuthorId, video.Channels);
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

            bool channelTab = _searchTab == 2;
            string kind = _searchTab == 2 ? "channel" : (_searchTab == 1 ? "short" : "video");

            _results.Clear();
            _channelResults.Clear();
            ErrorText.Visibility = Visibility.Collapsed;
            SearchHistoryList.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = channelTab ? Visibility.Collapsed : Visibility.Visible;
            ChannelResultsList.Visibility = channelTab ? Visibility.Visible : Visibility.Collapsed;
            LoadingRing.IsActive = true;

            try
            {
                var results = await _innerTube.SearchAsync(query, kind);
                var target = channelTab ? _channelResults : _results;
                foreach (var r in results)
                    target.Add(r);

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

        private void UpdateSearchTabStyles()
        {
            SearchVideosTab.Background = _searchTab == 0 ? _accentBrush : _darkGrayBrush;
            SearchShortsTab.Background = _searchTab == 1 ? _accentBrush : _darkGrayBrush;
            SearchChannelsTab.Background = _searchTab == 2 ? _accentBrush : _darkGrayBrush;
        }

        private async void SearchVideosTab_Click(object sender, RoutedEventArgs e)
        {
            if (_searchTab == 0) return;
            _searchTab = 0;
            UpdateSearchTabStyles();
            await PerformSearch();
        }

        private async void SearchShortsTab_Click(object sender, RoutedEventArgs e)
        {
            if (_searchTab == 1) return;
            _searchTab = 1;
            UpdateSearchTabStyles();
            await PerformSearch();
        }

        private async void SearchChannelsTab_Click(object sender, RoutedEventArgs e)
        {
            if (_searchTab == 2) return;
            _searchTab = 2;
            UpdateSearchTabStyles();
            await PerformSearch();
        }

        private void ChannelResultsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ch = e.ClickedItem as VideoResult;
            if (ch == null || string.IsNullOrEmpty(ch.AuthorId)) return;
            OpenChannel(ch.AuthorId, ch.Title, ch.ThumbnailUrl);
        }

        private void ResetSearch()
        {
            // Clear results and the query, then fall back to the search history list
            // so the search tab starts fresh next time it is opened.
            _searchTab = 0;
            _results.Clear();
            _channelResults.Clear();
            SearchBox.Text = "";
            ErrorText.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = false;
            UpdateSearchTabStyles();
            UpdateSearchHistoryVisibility();
        }

        private void UpdateSearchHistoryVisibility()
        {
            bool channelTab = _searchTab == 2;
            bool hasResults = _results.Count > 0 || _channelResults.Count > 0;

            if (!hasResults && _data.SearchHistory.Count > 0)
            {
                SearchHistoryList.ItemsSource = _data.SearchHistory.AsEnumerable().Reverse().ToList();
                SearchHistoryList.Visibility = Visibility.Visible;
                ResultsList.Visibility = Visibility.Collapsed;
                ChannelResultsList.Visibility = Visibility.Collapsed;
            }
            else
            {
                SearchHistoryList.Visibility = Visibility.Collapsed;
                ResultsList.Visibility = channelTab ? Visibility.Collapsed : Visibility.Visible;
                ChannelResultsList.Visibility = channelTab ? Visibility.Visible : Visibility.Collapsed;
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
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl, video.AuthorId, video.Channels);
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

        private void FavDownloadsTab_Click(object sender, RoutedEventArgs e)
        {
            _favSubTab = 2;
            UpdateFavoritesView();
        }

        private void UpdateFavoritesView()
        {
            FavChannelsTab.Background = _favSubTab == 0 ? _accentBrush : _darkGrayBrush;
            FavHistoryTab.Background = _favSubTab == 1 ? _accentBrush : _darkGrayBrush;
            FavDownloadsTab.Background = _favSubTab == 2 ? _accentBrush : _darkGrayBrush;

            ChannelsList.Visibility = _favSubTab == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryList.Visibility = _favSubTab == 1 ? Visibility.Visible : Visibility.Collapsed;
            DownloadsList.Visibility = _favSubTab == 2 ? Visibility.Visible : Visibility.Collapsed;

            if (_favSubTab == 0)
            {
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
            else if (_favSubTab == 1)
            {
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
            else
            {
                DownloadsList.ItemsSource = _data.Downloads;
                if (_data.Downloads.Count == 0)
                {
                    FavEmptyText.Text = "No downloaded videos yet";
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
            _channelTab = 0;
            UpdateChannelTabStyles();
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

            await LoadChannelContent();
        }

        private async System.Threading.Tasks.Task LoadChannelContent()
        {
            ChannelLoadingRing.IsActive = true;
            ChannelVideosList.Visibility = Visibility.Collapsed;
            ChannelVideosList.ItemsSource = null;

            try
            {
                string kind = _channelTab == 1 ? "short" : "video";
                var videos = await _innerTube.GetChannelVideosAsync(_currentAuthorId, kind);
                ChannelVideosList.ItemsSource = videos;
                ChannelVideosList.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ChannelName.Text = _currentAuthor + " - Error: " + ex.Message;
            }
            finally
            {
                ChannelLoadingRing.IsActive = false;
            }
        }

        private void UpdateChannelTabStyles()
        {
            ChannelVideosTab.Background = _channelTab == 0 ? _accentBrush : _darkGrayBrush;
            ChannelShortsTab.Background = _channelTab == 1 ? _accentBrush : _darkGrayBrush;
        }

        private async void ChannelVideosTab_Click(object sender, RoutedEventArgs e)
        {
            if (_channelTab == 0) return;
            _channelTab = 0;
            UpdateChannelTabStyles();
            await LoadChannelContent();
        }

        private async void ChannelShortsTab_Click(object sender, RoutedEventArgs e)
        {
            if (_channelTab == 1) return;
            _channelTab = 1;
            UpdateChannelTabStyles();
            await LoadChannelContent();
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
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl, video.AuthorId, video.Channels);
        }

        // ==================== VIDEO PLAYER ====================

        private async System.Threading.Tasks.Task PlayVideo(string videoId, string title, string author, string thumbnailUrl, string authorId = "", List<ChannelRef> channels = null)
        {
            int session = ++_playSession;

            // Guard against null strings — assigning null to a XAML Text property or
            // passing it across the WinRT boundary throws ArgumentNullException.
            title = title ?? "";
            author = author ?? "";
            thumbnailUrl = thumbnailUrl ?? "";
            authorId = authorId ?? "";

            _currentVideoId = videoId;
            _currentAuthorId = authorId;
            _currentAuthor = author;
            _currentThumbnailUrl = thumbnailUrl;
            _currentChannels = channels;

            PlayerOverlay.Visibility = Visibility.Visible;
            _isPlayerOpen = true;
            UpdateBackButtonVisibility();

            PlayerTitle.Visibility = Visibility.Collapsed;
            PlayerVideoTitle.Text = title;
            PlayerVideoAuthor.Text = author;
            PlayerLoadingRing.IsActive = true;

            // Update heart icon
            UpdatePlayerHeartIcon();
            UpdateDownloadIcon(videoId);

            // Record history
            await _data.AddHistoryItem(videoId, title, thumbnailUrl, authorId, author);

            try
            {
                PlayerVideoAuthor.Text = "Fetching video info...";
                var playerResponse = await _innerTube.GetPlayerResponseAsync(videoId);

                // Player was closed or another video was opened while we were fetching.
                if (session != _playSession)
                    return;

                if (playerResponse.Error != null)
                {
                    PlayerLoadingRing.IsActive = false;
                    ShowDebug(videoId, playerResponse);
                    return;
                }

                _lastPlayerResponse = playerResponse;
                _cachedHlsUrl = playerResponse.HlsManifestUrl;

                // If we opened the video without channel info (e.g. direct video id,
                // or a result whose byline had no channel), fall back to the primary
                // channel from the player response so the follow button still works.
                if ((_currentChannels == null || _currentChannels.Count == 0) &&
                    string.IsNullOrEmpty(_currentAuthorId) &&
                    !string.IsNullOrEmpty(playerResponse.ChannelId))
                {
                    _currentAuthorId = playerResponse.ChannelId;
                    if (string.IsNullOrEmpty(_currentAuthor))
                        _currentAuthor = playerResponse.Author;
                    UpdatePlayerHeartIcon();
                }

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
                        await PlayDash(dashMpd, session);
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
                            await PlayHls(playerResponse.HlsManifestUrl, session);
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

                        if (session != _playSession)
                            return;

                        if (muxedFmt != null)
                        {
                            _cachedMuxedFormat = muxedFmt;

                            // DASH/HLS (and any adaptive qualities) failed, so the only
                            // thing that actually plays is the muxed 360p stream. Replace
                            // the whole selector with just that option so the dropdown is
                            // not misleading.
                            _ignoreQualityChange = true;
                            QualitySelector.Items.Clear();
                            QualitySelector.Items.Add("360p (muxed)");
                            QualitySelector.SelectedIndex = 0;
                            _currentFormats = null;
                            _ignoreQualityChange = false;

                            PlayerVideoAuthor.Text = "Buffering 360p...";
                            await PlayMuxedUrl(muxedFmt.Url, session, muxedFmt.ContentLength);
                            playError = null;
                        }
                    }
                    catch (Exception muxedEx)
                    {
                        playError = (playError ?? "") + "\nMuxed: " + muxedEx.Message;
                    }
                }

                if (session != _playSession)
                    return;

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

        private async System.Threading.Tasks.Task PlayHls(string hlsUrl, int session)
        {
            _lastPlayedUrl = hlsUrl;
            var hlsSource = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(hlsUrl), _streamClient);

            // Player was closed or switched while the source was being created.
            if (session != _playSession || !_isPlayerOpen)
            {
                hlsSource.MediaSource?.Dispose();
                return;
            }

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

        private async System.Threading.Tasks.Task PlayDash(string dashMpd, int session)
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

            // Player was closed or switched while the source was being created.
            if (session != _playSession || !_isPlayerOpen)
            {
                dashSource.MediaSource?.Dispose();
                return;
            }

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

        private async System.Threading.Tasks.Task PlayDirectUrl(string url, int session, string mimeType = "video/mp4", long contentLength = 0)
        {
            _lastPlayedUrl = url;

            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            var baseMime = mimeType;
            var semiIdx = baseMime.IndexOf(';');
            if (semiIdx >= 0) baseMime = baseMime.Substring(0, semiIdx).Trim();

            // Stream on demand via HTTP Range requests instead of downloading the
            // whole file first, so playback starts quickly and uses less memory.
            var stream = await HttpRandomAccessStream.CreateAsync(_streamClient, new Uri(url));

            // Player was closed or switched while we were probing the stream.
            if (session != _playSession || !_isPlayerOpen)
            {
                stream.Dispose();
                return;
            }

            _currentStream = stream;

            VideoPlayer.Source = MediaSource.CreateFromStream(stream, baseMime);
        }

        private async System.Threading.Tasks.Task PlayMuxedUrl(string url, int session, long contentLength = 0)
        {
            _lastPlayedUrl = url;

            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            // Stream on demand via HTTP Range requests instead of downloading the
            // whole file first, so playback starts quickly and uses less memory.
            var stream = await HttpRandomAccessStream.CreateAsync(_streamClient, new Uri(url));

            // Player was closed or switched while we were probing the stream.
            if (session != _playSession || !_isPlayerOpen)
            {
                stream.Dispose();
                return;
            }

            _currentStream = stream;

            VideoPlayer.Source = MediaSource.CreateFromStream(stream, "video/mp4");
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
                        await PlayDash(dashMpd, _playSession);
                }
                else if (selectedText == "Auto (HLS)" && !string.IsNullOrEmpty(_cachedHlsUrl))
                {
                    await PlayHls(_cachedHlsUrl, _playSession);
                }
                else if (selectedText == "360p (muxed)" && _cachedMuxedFormat != null)
                {
                    PlayerVideoAuthor.Text = "Buffering 360p...";
                    await PlayMuxedUrl(_cachedMuxedFormat.Url, _playSession, _cachedMuxedFormat.ContentLength);
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
                        PlayerVideoAuthor.Text = "Buffering " + fmt.QualityLabel + "...";
                        await PlayDirectUrl(fmt.Url, _playSession, fmt.MimeType, fmt.ContentLength);
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
            var channels = GetCurrentChannelList();
            if (channels.Count == 0)
                return;

            if (channels.Count == 1)
            {
                var ch = channels[0];
                await _data.ToggleSubscription(ch.ChannelId, ch.Name, _currentThumbnailUrl);
            }
            else
            {
                await ShowChannelSelectionDialogAsync(channels);
            }

            UpdatePlayerHeartIcon();
        }

        /// <summary>
        /// Returns the de-duplicated list of channels credited on the current video.
        /// Falls back to the single primary author when no collaboration list exists.
        /// </summary>
        private List<ChannelRef> GetCurrentChannelList()
        {
            var list = new List<ChannelRef>();

            if (_currentChannels != null)
            {
                foreach (var c in _currentChannels)
                {
                    if (c == null || string.IsNullOrEmpty(c.ChannelId))
                        continue;
                    bool exists = false;
                    foreach (var e in list)
                        if (e.ChannelId == c.ChannelId) { exists = true; break; }
                    if (!exists)
                        list.Add(c);
                }
            }

            if (list.Count == 0 && !string.IsNullOrEmpty(_currentAuthorId))
                list.Add(new ChannelRef { Name = _currentAuthor, ChannelId = _currentAuthorId });

            return list;
        }

        /// <summary>
        /// Sets the player follow icon to filled when any credited channel is followed.
        /// </summary>
        private void UpdatePlayerHeartIcon()
        {
            bool anySubscribed = false;
            foreach (var ch in GetCurrentChannelList())
            {
                if (_data.IsSubscribed(ch.ChannelId)) { anySubscribed = true; break; }
            }
            PlayerHeartIcon.Glyph = anySubscribed ? "\uEB52" : "\uEB51";
        }

        /// <summary>
        /// Shows a dialog listing every channel credited on a collaboration video so
        /// the user can follow/unfollow each one individually.
        /// </summary>
        private async System.Threading.Tasks.Task ShowChannelSelectionDialogAsync(List<ChannelRef> channels)
        {
            var panel = new StackPanel();

            foreach (var ch in channels)
            {
                var channel = ch; // capture per-iteration
                var btn = new Button
                {
                    Tag = channel,
                    Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = FollowButtonText(channel)
                };
                btn.Click += async (s, args) =>
                {
                    await _data.ToggleSubscription(channel.ChannelId, channel.Name, _currentThumbnailUrl);
                    btn.Content = FollowButtonText(channel);
                    UpdatePlayerHeartIcon();
                };
                panel.Children.Add(btn);
            }

            var dialog = new ContentDialog
            {
                Title = "Select channel",
                Content = panel,
                CloseButtonText = "Done"
            };
            await dialog.ShowAsync();
        }

        private string FollowButtonText(ChannelRef channel)
        {
            string name = string.IsNullOrEmpty(channel.Name) ? "Channel" : channel.Name;
            return (_data.IsSubscribed(channel.ChannelId) ? "\u2713 Following  " : "Follow  ") + name;
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
            // Invalidate any in-flight playback setup so a late-arriving stream
            // does not start playing after the player has been closed.
            _playSession++;

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
            ReleaseDisplayRequest();
            UpdateBackButtonVisibility();
        }

        // Keeps the screen awake while a video is actively playing, and lets it
        // dim/lock normally when paused, buffering stalls, the video ends, or the
        // player is closed.
        private async void PlaybackSession_PlaybackStateChanged(
            Windows.Media.Playback.MediaPlaybackSession sender, object args)
        {
            var state = sender.PlaybackState;
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (state == Windows.Media.Playback.MediaPlaybackState.Playing)
                    ActivateDisplayRequest();
                else
                    ReleaseDisplayRequest();
            });
        }

        private void ActivateDisplayRequest()
        {
            if (_displayRequestActive)
                return;
            _displayRequest.RequestActive();
            _displayRequestActive = true;
        }

        private void ReleaseDisplayRequest()
        {
            if (!_displayRequestActive)
                return;
            try { _displayRequest.RequestRelease(); }
            catch { }
            _displayRequestActive = false;
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
