using System.Collections.Generic;

namespace subwayTube.Models
{
    public class StreamFormat
    {
        public int Itag { get; set; }
        public string QualityLabel { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public string MimeType { get; set; }
        public int Bitrate { get; set; }

        /// <summary>
        /// Direct URL (if available, already deciphered). Null if signatureCipher is set.
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Encrypted signature cipher string. Null if Url is set directly.
        /// </summary>
        public string SignatureCipher { get; set; }

        /// <summary>
        /// Whether this is a muxed format (audio+video combined).
        /// </summary>
        public bool IsMuxed { get; set; }

        /// <summary>
        /// Whether this is a video stream (vs audio-only).
        /// </summary>
        public bool IsVideo { get; set; }

        // DASH manifest fields
        public string Codecs { get; set; }
        public int Fps { get; set; }
        public long ContentLength { get; set; }
        public long ApproxDurationMs { get; set; }
        public long InitRangeStart { get; set; }
        public long InitRangeEnd { get; set; }
        public long IndexRangeStart { get; set; }
        public long IndexRangeEnd { get; set; }
        public int AudioSampleRate { get; set; }
        public int AudioChannels { get; set; }

        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(QualityLabel))
                    return QualityLabel + (IsMuxed ? "" : " (video only)");
                return Height > 0 ? Height + "p" : "itag " + Itag;
            }
        }
    }

    public class PlayerResponse
    {
        public string RawJson { get; set; }
        public string RequestBody { get; set; }
        public int StatusCode { get; set; }
        public string Error { get; set; }
        public string HlsManifestUrl { get; set; }
        public string Author { get; set; }
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public List<StreamFormat> Formats { get; set; } = new List<StreamFormat>();
    }
}
