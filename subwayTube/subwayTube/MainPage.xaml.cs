using System;
using System.Collections.ObjectModel;
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
            // Show player overlay
            PlayerOverlay.Visibility = Visibility.Visible;
            _isPlayerOpen = true;
            UpdateBackButtonVisibility();

            PlayerTitle.Text = title;
            PlayerVideoTitle.Text = title;
            PlayerVideoAuthor.Text = author;
            PlayerLoadingRing.IsActive = true;

            // Use YouTube's embed player via WebView — avoids needing poToken + signature deciphering
            var embedHtml = "<!DOCTYPE html><html><head>" +
                "<meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
                "<style>*{margin:0;padding:0}html,body{width:100%;height:100%;background:#000}" +
                "iframe{width:100%;height:100%;border:0}</style></head><body>" +
                "<iframe src='https://www.youtube.com/embed/" + Uri.EscapeDataString(videoId) +
                "?autoplay=1&rel=0&playsinline=1' allow='autoplay' allowfullscreen></iframe>" +
                "</body></html>";

            VideoWebView.NavigateToString(embedHtml);
            PlayerLoadingRing.IsActive = false;
        }

        private void ShowDebug(string videoId, Services.InnerTubeService.PlayerResult result)
        {
            var info = "=== PLAYER DEBUG ===\n\n";
            info += "VideoId: " + videoId + "\n";
            info += "HTTP Status: " + result.StatusCode + "\n";
            info += "Error: " + (result.Error ?? "none") + "\n";
            info += "StreamUrl: " + (result.StreamUrl ?? "null") + "\n\n";
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
            VideoWebView.NavigateToString("");
            PlayerOverlay.Visibility = Visibility.Collapsed;
            DebugOverlay.Visibility = Visibility.Collapsed;
            _isPlayerOpen = false;
            UpdateBackButtonVisibility();
        }

        private void VideoWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            // Allow YouTube embeds and about:blank, block everything else
            if (args.Uri != null)
            {
                var host = args.Uri.Host;
                if (host != "www.youtube.com" && host != "youtube.com" &&
                    host != "www.youtube-nocookie.com" && host != "accounts.google.com" &&
                    args.Uri.Scheme != "about")
                {
                    args.Cancel = true;
                }
            }
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

        /// <summary>
        /// Extract a video ID from a YouTube URL, or return null if not a YouTube URL.
        /// </summary>
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
