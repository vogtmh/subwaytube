using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;
using subwayTube.Models;

namespace subwayTube.Services
{
    public class InnerTubeService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // InnerTube API endpoints
        private const string SearchUrl = "https://www.youtube.com/youtubei/v1/search?key=AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";
        private const string PlayerUrl = "https://www.youtube.com/youtubei/v1/player?key=AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";

        // Client version strings — from youtubei.js Constants.ts
        private const string WebClientVersion = "2.20260206.01.00";
        private const string IosClientVersion = "20.11.6";
        private const string IosUserAgent = "com.google.ios.youtube/20.11.6 (iPhone10,4; U; CPU iOS 16_7_7 like Mac OS X)";
        private const string IosDeviceModel = "iPhone10,4";

        static InnerTubeService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        }

        /// <summary>
        /// Search YouTube for videos using the InnerTube API (WEB client).
        /// </summary>
        public async Task<List<VideoResult>> SearchAsync(string query)
        {
            var body = new JsonObject
            {
                ["context"] = new JsonObject
                {
                    ["client"] = new JsonObject
                    {
                        ["clientName"] = JsonValue.CreateStringValue("WEB"),
                        ["clientVersion"] = JsonValue.CreateStringValue(WebClientVersion),
                        ["hl"] = JsonValue.CreateStringValue("en"),
                        ["gl"] = JsonValue.CreateStringValue("US")
                    }
                },
                ["query"] = JsonValue.CreateStringValue(query)
            };

            var content = new StringContent(body.Stringify(), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(SearchUrl, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return ParseSearchResults(json);
        }

        /// <summary>
        /// Get a direct 360p stream URL for a video using the InnerTube API (IOS client).
        /// The IOS client typically returns direct stream URLs without signature deciphering.
        /// </summary>
        public async Task<PlayerResult> GetStreamUrlAsync(string videoId)
        {
            var body = new JsonObject
            {
                ["context"] = new JsonObject
                {
                    ["client"] = new JsonObject
                    {
                        ["clientName"] = JsonValue.CreateStringValue("IOS"),
                        ["clientVersion"] = JsonValue.CreateStringValue(IosClientVersion),
                        ["deviceModel"] = JsonValue.CreateStringValue(IosDeviceModel),
                        ["hl"] = JsonValue.CreateStringValue("en"),
                        ["gl"] = JsonValue.CreateStringValue("US")
                    }
                },
                ["videoId"] = JsonValue.CreateStringValue(videoId),
                ["contentCheckOk"] = JsonValue.CreateBooleanValue(true),
                ["racyCheckOk"] = JsonValue.CreateBooleanValue(true)
            };

            var content = new StringContent(body.Stringify(), System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, PlayerUrl);
            request.Content = content;
            request.Headers.TryAddWithoutValidation("User-Agent", IosUserAgent);

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            var result = new PlayerResult();
            result.RawJson = json;
            result.RequestBody = body.Stringify();
            result.StatusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                result.Error = "HTTP " + result.StatusCode;
                return result;
            }

            try
            {
                result.StreamUrl = ParseStreamUrl(json);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        public class PlayerResult
        {
            public string StreamUrl { get; set; }
            public string RawJson { get; set; }
            public string RequestBody { get; set; }
            public string Error { get; set; }
            public int StatusCode { get; set; }
        }

        private List<VideoResult> ParseSearchResults(string json)
        {
            var results = new List<VideoResult>();

            JsonObject root;
            if (!JsonObject.TryParse(json, out root))
                return results;

            // Navigate: contents -> twoColumnSearchResultsRenderer -> primaryContents ->
            //           sectionListRenderer -> contents[] -> itemSectionRenderer -> contents[]
            try
            {
                var contents = root.GetNamedObject("contents")
                    .GetNamedObject("twoColumnSearchResultsRenderer")
                    .GetNamedObject("primaryContents")
                    .GetNamedObject("sectionListRenderer")
                    .GetNamedArray("contents");

                for (uint i = 0; i < contents.Count; i++)
                {
                    var section = contents.GetObjectAt(i);
                    if (!section.ContainsKey("itemSectionRenderer"))
                        continue;

                    var items = section.GetNamedObject("itemSectionRenderer")
                        .GetNamedArray("contents");

                    for (uint j = 0; j < items.Count; j++)
                    {
                        var item = items.GetObjectAt(j);
                        if (!item.ContainsKey("videoRenderer"))
                            continue;

                        var video = item.GetNamedObject("videoRenderer");
                        var result = ParseVideoRenderer(video);
                        if (result != null)
                            results.Add(result);
                    }
                }
            }
            catch (Exception)
            {
                // JSON structure mismatch — return whatever we found so far
            }

            return results;
        }

        private VideoResult ParseVideoRenderer(JsonObject video)
        {
            try
            {
                var videoId = video.GetNamedString("videoId");

                var title = GetTextFromRuns(video.GetNamedObject("title"));

                string author = "";
                if (video.ContainsKey("ownerText"))
                    author = GetTextFromRuns(video.GetNamedObject("ownerText"));
                else if (video.ContainsKey("longBylineText"))
                    author = GetTextFromRuns(video.GetNamedObject("longBylineText"));

                string duration = "";
                if (video.ContainsKey("lengthText"))
                    duration = GetSimpleText(video.GetNamedObject("lengthText"));

                string thumbnailUrl = "";
                if (video.ContainsKey("thumbnail"))
                {
                    var thumbs = video.GetNamedObject("thumbnail").GetNamedArray("thumbnails");
                    if (thumbs.Count > 0)
                    {
                        // Pick the last (highest resolution) thumbnail
                        thumbnailUrl = thumbs.GetObjectAt((uint)(thumbs.Count - 1)).GetNamedString("url");
                        // Fix protocol-relative URLs
                        if (thumbnailUrl.StartsWith("//"))
                            thumbnailUrl = "https:" + thumbnailUrl;
                    }
                }

                string viewCount = "";
                if (video.ContainsKey("viewCountText"))
                    viewCount = GetSimpleTextOrRuns(video.GetNamedObject("viewCountText"));
                else if (video.ContainsKey("shortViewCountText"))
                    viewCount = GetSimpleTextOrRuns(video.GetNamedObject("shortViewCountText"));

                return new VideoResult
                {
                    VideoId = videoId,
                    Title = title,
                    Author = author,
                    Duration = duration,
                    ThumbnailUrl = thumbnailUrl,
                    ViewCount = viewCount
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string ParseStreamUrl(string json)
        {
            JsonObject root;
            if (!JsonObject.TryParse(json, out root))
                throw new Exception("Invalid JSON response from player API");

            // Check playability
            if (root.ContainsKey("playabilityStatus"))
            {
                var status = root.GetNamedObject("playabilityStatus");
                var statusStr = status.GetNamedString("status");
                if (statusStr != "OK")
                {
                    string reason = "unknown";
                    if (status.ContainsKey("reason"))
                        reason = status.GetNamedString("reason");
                    else if (status.ContainsKey("messages"))
                        reason = status.GetNamedArray("messages").GetStringAt(0);
                    throw new Exception("Playback blocked: " + statusStr + " - " + reason);
                }
            }

            if (!root.ContainsKey("streamingData"))
                throw new Exception("No streaming data in response");

            var streamingData = root.GetNamedObject("streamingData");

            // Option 1: Try HLS manifest (adaptive streaming, works natively in MediaPlayerElement)
            if (streamingData.ContainsKey("hlsManifestUrl"))
            {
                return streamingData.GetNamedString("hlsManifestUrl");
            }

            // Option 2: Try muxed formats (contains both audio+video)
            if (streamingData.ContainsKey("formats"))
            {
                var formats = streamingData.GetNamedArray("formats");
                string fallbackUrl = null;
                int formatCount = 0;
                int cipheredCount = 0;

                for (uint i = 0; i < formats.Count; i++)
                {
                    var format = formats.GetObjectAt(i);
                    formatCount++;

                    if (!format.ContainsKey("url"))
                    {
                        cipheredCount++;
                        continue;
                    }

                    string url = format.GetNamedString("url");

                    // Prefer 360p
                    if (format.ContainsKey("qualityLabel"))
                    {
                        string quality = format.GetNamedString("qualityLabel");
                        if (quality.Contains("360"))
                            return url;
                    }

                    if (format.ContainsKey("quality"))
                    {
                        string quality = format.GetNamedString("quality");
                        if (quality == "medium")
                            return url;
                    }

                    if (fallbackUrl == null)
                        fallbackUrl = url;
                }

                if (fallbackUrl != null)
                    return fallbackUrl;

                if (cipheredCount > 0)
                    throw new Exception(formatCount + " formats found but all " + cipheredCount + " require signature deciphering");
            }

            throw new Exception("No playable formats found in streaming data");
        }

        // Helper: extract text from a "runs" array
        private string GetTextFromRuns(JsonObject textObj)
        {
            if (textObj.ContainsKey("simpleText"))
                return textObj.GetNamedString("simpleText");

            if (textObj.ContainsKey("runs"))
            {
                var runs = textObj.GetNamedArray("runs");
                var text = "";
                for (uint i = 0; i < runs.Count; i++)
                {
                    text += runs.GetObjectAt(i).GetNamedString("text");
                }
                return text;
            }

            return "";
        }

        // Helper: try simpleText first, then runs
        private string GetSimpleText(JsonObject textObj)
        {
            if (textObj.ContainsKey("simpleText"))
                return textObj.GetNamedString("simpleText");
            return GetTextFromRuns(textObj);
        }

        private string GetSimpleTextOrRuns(JsonObject textObj)
        {
            return GetSimpleText(textObj);
        }
    }
}
