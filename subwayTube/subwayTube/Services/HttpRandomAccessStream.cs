using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.Web.Http;

namespace subwayTube.Services
{
    /// <summary>
    /// An <see cref="IRandomAccessStreamWithContentType"/> that streams a remote HTTP
    /// resource on demand using HTTP Range requests, instead of downloading the whole
    /// file up front. The MediaPlayer reads only the bytes it currently needs, so
    /// playback can start almost immediately and seeking does not require a full download.
    ///
    /// Requests are issued through the supplied <see cref="HttpClient"/> (which carries
    /// the IOS YouTube User-Agent via <see cref="IosUserAgentFilter"/>), so YouTube
    /// googlevideo URLs return 206 Partial Content rather than 403.
    /// </summary>
    public sealed class HttpRandomAccessStream : IRandomAccessStreamWithContentType
    {
        private readonly HttpClient _client;
        private readonly Uri _uri;
        private ulong _size;
        private string _contentType = "video/mp4";
        private ulong _position;

        private HttpRandomAccessStream(HttpClient client, Uri uri)
        {
            _client = client;
            _uri = uri;
        }

        /// <summary>
        /// Creates the stream and probes the resource for its total size and content type.
        /// </summary>
        public static IAsyncOperation<HttpRandomAccessStream> CreateAsync(HttpClient client, Uri uri)
        {
            var stream = new HttpRandomAccessStream(client, uri);
            return AsyncInfo.Run(async cancellationToken =>
            {
                await stream.InitializeAsync(cancellationToken);
                return stream;
            });
        }

        private async Task InitializeAsync(CancellationToken token)
        {
            // Ask for a single byte so the server replies with a Content-Range that
            // reveals the total length, without transferring the whole file.
            var request = new HttpRequestMessage(HttpMethod.Get, _uri);
            request.Headers.TryAppendWithoutValidation("Range", "bytes=0-0");

            var response = await _client
                .SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .AsTask(token);

            var ct = response.Content.Headers.ContentType;
            if (ct != null && !string.IsNullOrEmpty(ct.MediaType))
                _contentType = ct.MediaType;

            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange != null && contentRange.Length.HasValue)
                _size = contentRange.Length.Value;
            else if (response.Content.Headers.ContentLength.HasValue)
                _size = response.Content.Headers.ContentLength.Value;

            response.Dispose();
        }

        public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(IBuffer buffer, uint count, InputStreamOptions options)
        {
            return AsyncInfo.Run<IBuffer, uint>(async (cancellationToken, progress) =>
            {
                ulong start = _position;
                ulong end = start + count - 1;
                if (_size > 0 && end > _size - 1)
                    end = _size - 1;

                if (_size > 0 && start >= _size)
                    return buffer; // nothing left to read

                var request = new HttpRequestMessage(HttpMethod.Get, _uri);
                request.Headers.TryAppendWithoutValidation("Range", "bytes=" + start + "-" + end);

                var response = await _client
                    .SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .AsTask(cancellationToken);

                var result = await response.Content.ReadAsBufferAsync().AsTask(cancellationToken);
                response.Dispose();

                _position += result.Length;
                return result;
            });
        }

        public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer)
        {
            throw new NotSupportedException("HttpRandomAccessStream is read-only.");
        }

        public IAsyncOperation<bool> FlushAsync()
        {
            throw new NotSupportedException("HttpRandomAccessStream is read-only.");
        }

        public IInputStream GetInputStreamAt(ulong position)
        {
            var clone = CloneInternal();
            clone._position = position;
            return clone;
        }

        public IOutputStream GetOutputStreamAt(ulong position)
        {
            throw new NotSupportedException("HttpRandomAccessStream is read-only.");
        }

        public IRandomAccessStream CloneStream()
        {
            return CloneInternal();
        }

        private HttpRandomAccessStream CloneInternal()
        {
            return new HttpRandomAccessStream(_client, _uri)
            {
                _size = _size,
                _contentType = _contentType,
                _position = _position
            };
        }

        public void Seek(ulong position)
        {
            _position = position;
        }

        public bool CanRead => true;

        public bool CanWrite => false;

        public ulong Position => _position;

        public ulong Size
        {
            get { return _size; }
            set { _size = value; }
        }

        public string ContentType => _contentType;

        public void Dispose()
        {
        }
    }
}
