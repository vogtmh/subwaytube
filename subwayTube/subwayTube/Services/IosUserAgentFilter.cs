using System;
using Windows.Foundation;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace subwayTube.Services
{
    /// <summary>
    /// HTTP filter that injects the IOS YouTube User-Agent header into every request.
    /// Used with AdaptiveMediaSource so that ALL segment requests (including Range requests)
    /// get the correct User-Agent. Without this, YouTube returns 403.
    /// </summary>
    public sealed class IosUserAgentFilter : IHttpFilter
    {
        private readonly HttpBaseProtocolFilter _inner;

        private const string IosUserAgent =
            "com.google.ios.youtube/20.11.6 (iPhone10,4; U; CPU iOS 16_7_7 like Mac OS X)";

        public IosUserAgentFilter()
        {
            _inner = new HttpBaseProtocolFilter();
        }

        public IAsyncOperationWithProgress<HttpResponseMessage, HttpProgress> SendRequestAsync(HttpRequestMessage request)
        {
            // Replace User-Agent with IOS YouTube client on all requests
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(IosUserAgent);
            return _inner.SendRequestAsync(request);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
