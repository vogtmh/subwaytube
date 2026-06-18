using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using subwayTube.Models;
using subwayTube.Services;

namespace subwayTube
{
    public sealed partial class MainPage : Page
    {
        private readonly InnerTubeService _innerTube = new InnerTubeService();
        private readonly ObservableCollection<VideoResult> _results = new ObservableCollection<VideoResult>();
        private bool _isPlayerOpen;
        private List<StreamFormat> _currentFormats;
        private string _currentVideoId;
        private bool _ignoreQualityChange;
        private PlayerResponse _lastPlayerResponse;
        private string _lastPlayedUrl;
        private InMemoryRandomAccessStream _currentStream;
        private string _cachedHlsUrl;
        private readonly Windows.Web.Http.HttpClient _streamClient;

        public MainPage()
        {
            this.InitializeComponent();
            ResultsList.ItemsSource = _results;

            // HTTP client with IOS User-Agent filter — ensures ALL requests
            // (including AdaptiveMediaSource Range requests) get the correct UA
            _streamClient = new Windows.Web.Http.HttpClient(new IosUserAgentFilter());

            // Handle hardware back button (Windows 10 Mobile)
            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;

            // Handle media playback failures
            VideoPlayer.MediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                await PerformSearch();
            }
        }

        private async System.Threading.Tasks.Task PerformSearch()
        {
            var query = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(query))
                return;

            string directVideoId = ExtractVideoId(query);
            if (directVideoId != null)
            {
                await PlayVideo(directVideoId, query, "", "");
                return;
            }

            _results.Clear();
            ErrorText.Visibility = Visibility.Collapsed;
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

        private async void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = e.ClickedItem as VideoResult;
            if (video == null) return;
            await PlayVideo(video.VideoId, video.Title, video.Author, video.ThumbnailUrl);
        }

        private async System.Threading.Tasks.Task PlayVideo(string videoId, string title, string author, string thumbnailUrl)
        {
            _currentVideoId = videoId;

            PlayerOverlay.Visibility = Visibility.Visible;
            _isPlayerOpen = true;
            UpdateBackButtonVisibility();

            PlayerTitle.Text = title;
            PlayerVideoTitle.Text = title;
            PlayerVideoAuthor.Text = author;
            PlayerLoadingRing.IsActive = true;

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

                // Try DASH manifest first (combines video + audio adaptively)
                var dashMpd = DashManifestGenerator.Generate(playerResponse.Formats);

                // Get available video qualities for the selector
                var avcVideoFormats = playerResponse.Formats
                    .Where(f => f.IsVideo && !f.IsMuxed && f.Url != null && f.Codecs != null && f.Codecs.StartsWith("avc1"))
                    .OrderByDescending(f => f.Height)
                    .ToList();
                _currentFormats = avcVideoFormats;

                // Populate quality selector
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

                // Play: DASH > HLS > direct download
                if (dashMpd != null)
                {
                    PlayerVideoAuthor.Text = "Loading DASH stream...";
                    await PlayDash(dashMpd);
                }
                else if (!string.IsNullOrEmpty(playerResponse.HlsManifestUrl))
                {
                    PlayerVideoAuthor.Text = "Loading HLS stream...";
                    await PlayHls(playerResponse.HlsManifestUrl);
                }
                else if (avcVideoFormats.Count > 0)
                {
                    var fmt = avcVideoFormats[0];
                    PlayerVideoAuthor.Text = "Downloading " + fmt.QualityLabel + "...";
                    await PlayDirectUrl(fmt.Url, fmt.MimeType, fmt.ContentLength);
                }
                else
                {
                    playerResponse.Error = "No playable formats found";
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

        /// <summary>
        /// Plays a locally-generated DASH MPD manifest via AdaptiveMediaSource.
        /// The IosUserAgentFilter ensures all segment Range requests get the correct UA.
        /// </summary>
        private async System.Threading.Tasks.Task PlayDash(string dashMpd)
        {
            _lastPlayedUrl = "DASH manifest (local)";

            // Dispose previous stream
            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            // Write MPD XML to an in-memory stream
            var bytes = System.Text.Encoding.UTF8.GetBytes(dashMpd);
            _currentStream = new InMemoryRandomAccessStream();
            await _currentStream.WriteAsync(bytes.AsBuffer());
            _currentStream.Seek(0);

            // Create adaptive source from the DASH manifest stream, using our filter-based HttpClient
            var dashSource = await AdaptiveMediaSource.CreateFromStreamAsync(
                _currentStream,
                new System.Uri("https://www.youtube.com/dash"),
                "application/dash+xml",
                _streamClient);

            if (dashSource.Status == AdaptiveMediaSourceCreationStatus.Success)
            {
                var adaptiveSource = dashSource.MediaSource;
                // Start at highest available bitrate (default is lowest)
                adaptiveSource.InitialBitrate = adaptiveSource.AvailableBitrates.Max();
                // Intercept downloads to add Range header (YouTube 403s without it)
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
            // YouTube returns 403 for requests without a bounded Range header.
            // We intercept every request and fetch with a proper Range header ourselves.
            var deferral = args.GetDeferral();
            try
            {
                var reqMsg = new Windows.Web.Http.HttpRequestMessage(
                    Windows.Web.Http.HttpMethod.Get, args.ResourceUri);

                // If the framework provides byte range info, use it; otherwise request first 10MB
                if (args.ResourceByteRangeOffset.HasValue && args.ResourceByteRangeLength.HasValue)
                {
                    var start = args.ResourceByteRangeOffset.Value;
                    var end = start + args.ResourceByteRangeLength.Value - 1;
                    reqMsg.Headers.Add("Range", "bytes=" + start + "-" + end);
                }
                else if (args.ResourceByteRangeOffset.HasValue)
                {
                    var start = args.ResourceByteRangeOffset.Value;
                    // Bounded range - request a large chunk 
                    reqMsg.Headers.Add("Range", "bytes=" + start + "-" + (start + 10485759));
                }
                else
                {
                    // No range info at all - request from start with bounded range
                    reqMsg.Headers.Add("Range", "bytes=0-10485759");
                }

                var response = await _streamClient.SendRequestAsync(reqMsg);
                var buffer = await response.Content.ReadAsBufferAsync();
                args.Result.Buffer = buffer;
            }
            catch
            {
                // Let the player handle the error naturally
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>
        /// Downloads the stream via HttpClient with IOS User-Agent, buffers it,
        /// then plays from the in-memory stream. This avoids the 403 that
        /// MediaPlayerElement gets when it fetches the URL with a Windows UA.
        /// </summary>
        private async System.Threading.Tasks.Task PlayDirectUrl(string url, string mimeType = "video/mp4", long contentLength = 0)
        {
            _lastPlayedUrl = url;

            // Dispose previous stream
            if (_currentStream != null)
            {
                _currentStream.Dispose();
                _currentStream = null;
            }

            // YouTube requires bounded Range header or returns 403
            var reqMsg = new Windows.Web.Http.HttpRequestMessage(
                Windows.Web.Http.HttpMethod.Get, new Uri(url));
            long rangeEnd = contentLength > 0 ? contentLength - 1 : 500000000;
            reqMsg.Headers.Add("Range", "bytes=0-" + rangeEnd);

            var response = await _streamClient.SendRequestAsync(reqMsg);
            response.EnsureSuccessStatusCode();

            var buffer = await response.Content.ReadAsBufferAsync();
            _currentStream = new InMemoryRandomAccessStream();
            await _currentStream.WriteAsync(buffer);
            _currentStream.Seek(0);

            // Extract base mime type (strip codecs parameter)
            var baseMime = mimeType;
            var semiIdx = baseMime.IndexOf(';');
            if (semiIdx >= 0) baseMime = baseMime.Substring(0, semiIdx).Trim();

            VideoPlayer.Source = MediaSource.CreateFromStream(_currentStream, baseMime);
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
                    // Show generated DASH manifest
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
                else if (_currentFormats != null)
                {
                    // Calculate offset for DASH/HLS entries
                    int offset = 0;
                    if (QualitySelector.Items.Contains("Auto (DASH)")) offset++;
                    if (QualitySelector.Items.Contains("Auto (HLS)")) offset++;
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

        private void ShowDebug(string videoId, PlayerResponse result)
        {
            var info = "=== PLAYER DEBUG ===\n\n";
            info += "VideoId: " + videoId + "\n";
            info += "HTTP Status: " + result.StatusCode + "\n";
            info += "Error: " + (result.Error ?? "none") + "\n";
            info += "HLS URL: " + (result.HlsManifestUrl ?? "null") + "\n";
            info += "Formats found: " + result.Formats.Count + "\n\n";
            info += "=== REQUEST BODY ===\n" + (result.RequestBody ?? "") + "\n\n";

            // Show generated DASH manifest if available
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
            if (_isPlayerOpen)
            {
                ClosePlayer();
                e.Handled = true;
            }
        }

        private void UpdateBackButtonVisibility()
        {
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                _isPlayerOpen
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
