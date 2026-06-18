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
        /// Get available streams for a video using the InnerTube API (IOS client).
        /// IOS client returns direct URLs (no cipher) + HLS manifest.
        /// </summary>
        public async Task<PlayerResponse> GetPlayerResponseAsync(string videoId)
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

            var result = new PlayerResponse();
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
                ParsePlayerResponse(json, result);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private void ParsePlayerResponse(string json, PlayerResponse result)
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

            // Extract HLS manifest URL (for native adaptive playback)
            if (streamingData.ContainsKey("hlsManifestUrl"))
            {
                result.HlsManifestUrl = streamingData.GetNamedString("hlsManifestUrl");
            }

            // Parse muxed formats (audio+video)
            if (streamingData.ContainsKey("formats"))
            {
                ParseFormats(streamingData.GetNamedArray("formats"), result.Formats, true);
            }

            // Parse adaptive formats (video-only and audio-only)
            if (streamingData.ContainsKey("adaptiveFormats"))
            {
                ParseFormats(streamingData.GetNamedArray("adaptiveFormats"), result.Formats, false);
            }

            if (result.Formats.Count == 0)
                throw new Exception("No formats found in streaming data");
        }

        private void ParseFormats(JsonArray formats, List<StreamFormat> output, bool isMuxed)
        {
            for (uint i = 0; i < formats.Count; i++)
            {
                var f = formats.GetObjectAt(i);
                var fmt = new StreamFormat();

                fmt.IsMuxed = isMuxed;
                fmt.Itag = f.ContainsKey("itag") ? (int)f.GetNamedNumber("itag") : 0;
                fmt.Height = f.ContainsKey("height") ? (int)f.GetNamedNumber("height") : 0;
                fmt.Width = f.ContainsKey("width") ? (int)f.GetNamedNumber("width") : 0;
                fmt.Bitrate = f.ContainsKey("bitrate") ? (int)f.GetNamedNumber("bitrate") : 0;
                fmt.QualityLabel = f.ContainsKey("qualityLabel") ? f.GetNamedString("qualityLabel") : "";
                fmt.MimeType = f.ContainsKey("mimeType") ? f.GetNamedString("mimeType") : "";
                fmt.Fps = f.ContainsKey("fps") ? (int)f.GetNamedNumber("fps") : 0;
                fmt.AudioSampleRate = f.ContainsKey("audioSampleRate") ? int.Parse(f.GetNamedString("audioSampleRate")) : 0;
                fmt.AudioChannels = f.ContainsKey("audioChannels") ? (int)f.GetNamedNumber("audioChannels") : 0;
                fmt.ApproxDurationMs = f.ContainsKey("approxDurationMs") ? long.Parse(f.GetNamedString("approxDurationMs")) : 0;
                fmt.ContentLength = f.ContainsKey("contentLength") ? long.Parse(f.GetNamedString("contentLength")) : 0;

                // Extract codecs from mimeType (e.g. video/mp4; codecs="avc1.640028")
                var mime = fmt.MimeType;
                var codecsIdx = mime.IndexOf("codecs=\"");
                if (codecsIdx >= 0)
                {
                    var start = codecsIdx + 8;
                    var end = mime.IndexOf("\"", start);
                    if (end > start) fmt.Codecs = mime.Substring(start, end - start);
                }

                // Determine if this is a video stream
                fmt.IsVideo = fmt.MimeType.StartsWith("video/");

                // Parse byte ranges for DASH
                if (f.ContainsKey("initRange"))
                {
                    var ir = f.GetNamedObject("initRange");
                    fmt.InitRangeStart = long.Parse(ir.GetNamedString("start"));
                    fmt.InitRangeEnd = long.Parse(ir.GetNamedString("end"));
                }
                if (f.ContainsKey("indexRange"))
                {
                    var xr = f.GetNamedObject("indexRange");
                    fmt.IndexRangeStart = long.Parse(xr.GetNamedString("start"));
                    fmt.IndexRangeEnd = long.Parse(xr.GetNamedString("end"));
                }

                // Get URL or signatureCipher
                if (f.ContainsKey("url"))
                {
                    fmt.Url = f.GetNamedString("url");
                }
                else if (f.ContainsKey("signatureCipher"))
                {
                    fmt.SignatureCipher = f.GetNamedString("signatureCipher");
                }
                else if (f.ContainsKey("cipher"))
                {
                    fmt.SignatureCipher = f.GetNamedString("cipher");
                }
                else
                {
                    continue; // No URL available at all
                }

                output.Add(fmt);
            }
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
