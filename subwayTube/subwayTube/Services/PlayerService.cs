using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml.Controls;

namespace subwayTube.Services
{
    /// <summary>
    /// Handles YouTube stream URL deciphering using a WebView as a JS runtime.
    /// Flow: fetch player JS URL → extract decipher + n-transform functions → run in WebView.
    /// </summary>
    public class PlayerService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private WebView _jsEngine;
        private string _cachedPlayerUrl;
        private string _cachedDecipherFunc;
        private string _cachedNTransformFunc;
        private bool _jsReady;

        static PlayerService()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            }
        }

        public void SetWebView(WebView webView)
        {
            _jsEngine = webView;
        }

        /// <summary>
        /// Get the player base.js URL from a YouTube video page.
        /// </summary>
        public async Task<string> GetPlayerJsUrlAsync(string videoId)
        {
            var url = "https://www.youtube.com/embed/" + videoId;
            var html = await _httpClient.GetStringAsync(url);

            // Look for: "jsUrl":"/s/player/.../base.js"
            var match = Regex.Match(html, @"""jsUrl""\s*:\s*""([^""]+/base\.js)""");
            if (!match.Success)
            {
                // Fallback: try /s/player pattern
                match = Regex.Match(html, @"(/s/player/[^""]+/base\.js)");
            }

            if (!match.Success)
                throw new Exception("Could not find player JS URL in embed page");

            var jsPath = match.Groups[1].Value;
            if (jsPath.StartsWith("/"))
                jsPath = "https://www.youtube.com" + jsPath;

            return jsPath;
        }

        /// <summary>
        /// Download the player JS and extract the decipher and n-transform functions.
        /// </summary>
        public async Task PrepareDecipherAsync(string videoId)
        {
            if (_jsReady && _cachedPlayerUrl != null)
                return;

            var playerUrl = await GetPlayerJsUrlAsync(videoId);

            if (playerUrl == _cachedPlayerUrl && _jsReady)
                return;

            _cachedPlayerUrl = playerUrl;
            var playerJs = await _httpClient.GetStringAsync(playerUrl);

            // Extract the signature decipher function
            _cachedDecipherFunc = ExtractDecipherFunction(playerJs);

            // Extract the n-parameter transform function
            _cachedNTransformFunc = ExtractNTransformFunction(playerJs);

            // Load both functions into the WebView JS engine
            var jsToInject = _cachedDecipherFunc + "\n" + _cachedNTransformFunc;

            // Navigate to blank page first, then inject JS
            _jsEngine.NavigateToString("<html><body></body></html>");
            await WaitForNavigation();

            await _jsEngine.InvokeScriptAsync("eval", new[] { jsToInject });
            _jsReady = true;
        }

        /// <summary>
        /// Decipher a signature cipher string to get a playable URL.
        /// </summary>
        public async Task<string> DecipherUrlAsync(string signatureCipher)
        {
            // signatureCipher is URL-encoded: s=XXX&sp=sig&url=XXX
            var parts = ParseQueryString(signatureCipher);

            string encryptedSig = parts.ContainsKey("s") ? parts["s"] : null;
            string sigParam = parts.ContainsKey("sp") ? parts["sp"] : "signature";
            string baseUrl = parts.ContainsKey("url") ? parts["url"] : null;

            if (baseUrl == null)
                throw new Exception("No URL found in signatureCipher");

            if (encryptedSig != null)
            {
                // Decipher the signature
                var decipheredSig = await RunJsAsync("decipherSignature(decodeURIComponent('" + Uri.EscapeDataString(encryptedSig) + "'))");
                baseUrl += "&" + sigParam + "=" + Uri.EscapeDataString(decipheredSig);
            }

            // Transform the n parameter to avoid throttling
            baseUrl = await TransformNParamAsync(baseUrl);

            return baseUrl;
        }

        /// <summary>
        /// Decipher a direct URL (has url but may need n-param transform).
        /// </summary>
        public async Task<string> DecipherDirectUrlAsync(string url)
        {
            return await TransformNParamAsync(url);
        }

        private async Task<string> TransformNParamAsync(string url)
        {
            // Extract the n parameter
            var nMatch = Regex.Match(url, @"[&?]n=([^&]+)");
            if (!nMatch.Success)
                return url;

            var nValue = Uri.UnescapeDataString(nMatch.Groups[1].Value);
            var transformedN = await RunJsAsync("transformN('" + EscapeJsString(nValue) + "')");

            if (!string.IsNullOrEmpty(transformedN) && transformedN != nValue && transformedN != "undefined")
            {
                url = Regex.Replace(url, @"([&?])n=[^&]+", "$1n=" + Uri.EscapeDataString(transformedN));
            }

            return url;
        }

        private async Task<string> RunJsAsync(string expression)
        {
            var result = await _jsEngine.InvokeScriptAsync("eval", new[] { expression });
            return result;
        }

        /// <summary>
        /// Extract the signature decipher function chain from player JS.
        /// Pattern: finds the function that manipulates the signature via swap/reverse/splice operations.
        /// </summary>
        private string ExtractDecipherFunction(string playerJs)
        {
            // Step 1: Find the top-level decipher function.
            // Pattern: XX=function(a){a=a.split(""); ... ;return a.join("")}
            var funcMatch = Regex.Match(playerJs,
                @"([a-zA-Z0-9$]+)\s*=\s*function\s*\(\s*a\s*\)\s*\{\s*a\s*=\s*a\.split\(\s*""""""\s*\)([\s\S]*?return\s+a\.join\(\s*""""""\s*\))");

            if (!funcMatch.Success)
            {
                // Alternative pattern
                funcMatch = Regex.Match(playerJs,
                    @"(?:function\s+)?([a-zA-Z0-9$]+)\s*\(\s*a\s*\)\s*\{\s*a\s*=\s*a\.split\(\s*""""""\s*\)([\s\S]*?return\s+a\.join\(\s*""""""\s*\))");
            }

            if (!funcMatch.Success)
                throw new Exception("Could not extract decipher function from player JS");

            var funcBody = funcMatch.Value;

            // Step 2: Find the helper object that contains swap/reverse/splice
            // The function body calls something like: Xy.ab(a,3); Xy.cd(a,1); etc.
            var helperMatch = Regex.Match(funcBody, @"([a-zA-Z0-9$]+)\.[a-zA-Z0-9$]+\(");
            if (!helperMatch.Success)
                throw new Exception("Could not find decipher helper object name");

            var helperName = helperMatch.Groups[1].Value;

            // Extract the helper object definition: var Xy={ab:function(a,b){...}, cd:function(a){...}};
            var escapedHelper = Regex.Escape(helperName);
            var helperObjMatch = Regex.Match(playerJs,
                @"(?:var\s+)?" + escapedHelper + @"\s*=\s*\{([\s\S]*?)\};",
                RegexOptions.None);

            string helperObj = "";
            if (helperObjMatch.Success)
            {
                helperObj = "var " + helperName + "={" + helperObjMatch.Groups[1].Value + "};";
            }

            // Build the decipher function
            var result = helperObj + "\nfunction decipherSignature(a){a=a.split(\"\")" +
                         funcMatch.Groups[2].Value + "}";

            return result;
        }

        /// <summary>
        /// Extract the n-parameter transform function from player JS.
        /// This function transforms the 'n' throttle parameter to avoid speed throttling.
        /// </summary>
        private string ExtractNTransformFunction(string playerJs)
        {
            // The n-transform is referenced like: XXX=function(a){...} where the function is called with the n param
            // Pattern: look for the function that's assigned and called with the n parameter
            // Modern pattern: var b=a.split(""),c=[...function body...]
            var nFuncMatch = Regex.Match(playerJs,
                @"([a-zA-Z0-9$]+)\s*=\s*function\s*\(\s*a\s*\)\s*\{\s*var\s+b\s*=\s*a\.split\(\s*""""""\s*\)([\s\S]*?)\};");

            if (nFuncMatch.Success)
            {
                var funcName = nFuncMatch.Groups[1].Value;
                return "function transformN(a){var b=a.split(\"\")" +
                       nFuncMatch.Groups[2].Value + "}";
            }

            // Alternative: enhanced_except pattern used in newer players
            // b=a.get("n")) && (b=XXX[0](b),a.set("n",b)
            var nRefMatch = Regex.Match(playerJs,
                @"&&\s*\(\s*b\s*=\s*([a-zA-Z0-9$]+)\s*\[\s*0\s*\]\s*\(\s*b\s*\)");
            if (nRefMatch.Success)
            {
                var arrayName = nRefMatch.Groups[1].Value;
                // Find: var XXX=[function(a){...}]
                var arrayMatch = Regex.Match(playerJs,
                    Regex.Escape(arrayName) + @"\s*=\s*\[\s*(function\s*\(\s*a\s*\)\s*\{[\s\S]*?\})\s*\]");
                if (arrayMatch.Success)
                {
                    return "var transformN=" + arrayMatch.Groups[1].Value + ";";
                }
            }

            // If we can't find it, return a passthrough function (URLs will be throttled but still work)
            return "function transformN(a){return a;}";
        }

        private Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>();
            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var idx = pair.IndexOf('=');
                if (idx > 0)
                {
                    var key = Uri.UnescapeDataString(pair.Substring(0, idx));
                    var value = Uri.UnescapeDataString(pair.Substring(idx + 1));
                    result[key] = value;
                }
            }
            return result;
        }

        private string EscapeJsString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private TaskCompletionSource<bool> _navTcs;

        private async Task WaitForNavigation()
        {
            _navTcs = new TaskCompletionSource<bool>();
            _jsEngine.NavigationCompleted += OnNavCompleted;
            await _navTcs.Task;
            _jsEngine.NavigationCompleted -= OnNavCompleted;
        }

        private void OnNavCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            _navTcs?.TrySetResult(true);
        }

        public void InvalidateCache()
        {
            _jsReady = false;
            _cachedPlayerUrl = null;
        }
    }
}