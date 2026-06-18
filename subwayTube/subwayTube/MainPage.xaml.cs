using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Media.Core;
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
        private readonly PlayerService _playerService = new PlayerService();
        private readonly ObservableCollection<VideoResult> _results = new ObservableCollection<VideoResult>();
        private bool _isPlayerOpen;
        private List<StreamFormat> _currentFormats;
        private string _currentVideoId;
        private bool _ignoreQualityChange;

        public MainPage()
        {
            this.InitializeComponent();
            ResultsList.ItemsSource = _results;
            _playerService.SetWebView(JsEngine);

            // Handle hardware back button (Windows 10 Mobile)
            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
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

            // Handle direct YouTube URLs
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
                {
                    _results.Add(r);
                }

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

            // Show player overlay
            PlayerOverlay.Visibility = Visibility.Visible;
            _isPlayerOpen = true;
            UpdateBackButtonVisibility();

            PlayerTitle.Text = title;
            PlayerVideoTitle.Text = title;
            PlayerVideoAuthor.Text = author;
            PlayerLoadingRing.IsActive = true;

            try
            {
                // Step 1: Prepare the JS decipher engine
                PlayerVideoAuthor.Text = "Preparing decipher engine...";
                await _playerService.PrepareDecipherAsync(videoId);

                // Step 2: Get player response with all formats
                PlayerVideoAuthor.Text = "Fetching video info...";
                var playerResponse = await _innerTube.GetPlayerResponseAsync(videoId);

                if (playerResponse.Error != null)
                {
                    PlayerLoadingRing.IsActive = false;
                    ShowDebug(videoId, playerResponse);
                    return;
                }

                // Step 3: Filter to video formats, muxed preferred, sorted by height
                var videoFormats = playerResponse.Formats
                    .Where(f => f.IsVideo)
                    .OrderByDescending(f => f.IsMuxed)
                    .ThenByDescending(f => f.Height)
                    .ToList();

                if (videoFormats.Count == 0)
                {
                    PlayerLoadingRing.IsActive = false;
                    playerResponse.Error = "No video formats available";
                    ShowDebug(videoId, playerResponse);
                    return;
                }

                _currentFormats = videoFormats;

                // Step 4: Populate quality selector
                _ignoreQualityChange = true;
                QualitySelector.Items.Clear();
                foreach (var fmt in videoFormats)
                {
                    QualitySelector.Items.Add(fmt.DisplayLabel);
                }

                // Select default quality
                int defaultIdx = FindDefaultQualityIndex(videoFormats);
                QualitySelector.SelectedIndex = defaultIdx;
                _ignoreQualityChange = false;

                // Step 5: Decipher and play
                PlayerVideoAuthor.Text = "Deciphering stream URL...";
                await PlayFormat(videoFormats[defaultIdx]);

                PlayerVideoAuthor.Text = author;
                PlayerLoadingRing.IsActive = false;
            }
            catch (Exception ex)
            {
                PlayerVideoAuthor.Text = "Error: " + ex.Message;
                PlayerLoadingRing.IsActive = false;
            }
        }

        private async System.Threading.Tasks.Task PlayFormat(StreamFormat format)
        {
            string url;

            if (format.SignatureCipher != null)
            {
                url = await _playerService.DecipherUrlAsync(format.SignatureCipher);
            }
            else if (format.Url != null)
            {
                url = await _playerService.DecipherDirectUrlAsync(format.Url);
            }
            else
            {
                throw new Exception("Format has no URL or signatureCipher");
            }

            VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(url));
        }

        private int FindDefaultQualityIndex(List<StreamFormat> formats)
        {
            // Prefer muxed 720p
            for (int i = 0; i < formats.Count; i++)
                if (formats[i].IsMuxed && formats[i].Height == 720) return i;
            // Then muxed 480p
            for (int i = 0; i < formats.Count; i++)
                if (formats[i].IsMuxed && formats[i].Height == 480) return i;
            // Then muxed 360p
            for (int i = 0; i < formats.Count; i++)
                if (formats[i].IsMuxed && formats[i].Height == 360) return i;
            // Then any muxed
            for (int i = 0; i < formats.Count; i++)
                if (formats[i].IsMuxed) return i;
            // Then 720p adaptive
            for (int i = 0; i < formats.Count; i++)
                if (formats[i].Height == 720) return i;
            return 0;
        }

        private async void QualitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreQualityChange || _currentFormats == null || QualitySelector.SelectedIndex < 0)
                return;

            var format = _currentFormats[QualitySelector.SelectedIndex];
            try
            {
                await PlayFormat(format);
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
            info += "Formats found: " + result.Formats.Count + "\n\n";
            info += "=== REQUEST BODY ===\n" + (result.RequestBody ?? "") + "\n\n";
            info += "=== RAW RESPONSE (first 5000 chars) ===\n";
            var raw = result.RawJson ?? "";
            info += raw.Substring(0, Math.Min(5000, raw.Length));

            DebugText.Text = info;
            DebugOverlay.Visibility = Visibility.Visible;
        }

        private void CloseDebug_Click(object sender, RoutedEventArgs e)
        {
            DebugOverlay.Visibility = Visibility.Collapsed;
        }

        private void ClosePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            ClosePlayer();
        }

        private void ClosePlayer()
        {
            VideoPlayer.Source = null;
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
