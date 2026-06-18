using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.Media.Core;
using Windows.Media.Streaming.Adaptive;
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

        public MainPage()
        {
            this.InitializeComponent();
            ResultsList.ItemsSource = _results;

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

                // Collect video formats for quality selector
                var videoFormats = playerResponse.Formats
                    .Where(f => f.IsVideo && f.Url != null)
                    .OrderByDescending(f => f.Height)
                    .ToList();

                _currentFormats = videoFormats;

                // Populate quality selector
                _ignoreQualityChange = true;
                QualitySelector.Items.Clear();

                // Add HLS option first (adaptive, auto quality with audio)
                if (!string.IsNullOrEmpty(playerResponse.HlsManifestUrl))
                {
                    QualitySelector.Items.Add("Auto (HLS)");
                }

                foreach (var fmt in videoFormats)
                    QualitySelector.Items.Add(fmt.DisplayLabel);

                // Default to HLS if available, otherwise first format
                QualitySelector.SelectedIndex = 0;
                _ignoreQualityChange = false;

                // Play HLS if available, otherwise fall back to direct URL
                if (!string.IsNullOrEmpty(playerResponse.HlsManifestUrl))
                {
                    PlayerVideoAuthor.Text = "Loading HLS stream...";
                    await PlayHls(playerResponse.HlsManifestUrl);
                }
                else if (videoFormats.Count > 0)
                {
                    PlayerVideoAuthor.Text = "Loading stream...";
                    PlayDirectUrl(videoFormats[0].Url);
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
            var hlsSource = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(hlsUrl));
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

        private void PlayDirectUrl(string url)
        {
            VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(url));
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
                if (selectedText == "Auto (HLS)")
                {
                    // Re-fetch HLS URL (or cache it)
                    var response = await _innerTube.GetPlayerResponseAsync(_currentVideoId);
                    if (!string.IsNullOrEmpty(response.HlsManifestUrl))
                        await PlayHls(response.HlsManifestUrl);
                }
                else if (_currentFormats != null)
                {
                    // Find the format matching the selected label
                    // Account for the HLS entry offset
                    int offset = QualitySelector.Items.Contains("Auto (HLS)") ? 1 : 0;
                    int formatIdx = QualitySelector.SelectedIndex - offset;
                    if (formatIdx >= 0 && formatIdx < _currentFormats.Count)
                    {
                        PlayDirectUrl(_currentFormats[formatIdx].Url);
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
