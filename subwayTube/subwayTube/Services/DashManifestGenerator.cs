using System.Collections.Generic;
using System.Linq;
using System.Text;
using subwayTube.Models;

namespace subwayTube.Services
{
    /// <summary>
    /// Generates a DASH MPD manifest from InnerTube adaptive formats.
    /// This allows AdaptiveMediaSource to combine separate video + audio streams.
    /// </summary>
    public static class DashManifestGenerator
    {
        public static string Generate(List<StreamFormat> formats)
        {
            // Only use formats with direct URLs and byte range info
            var dashFormats = formats.Where(f => f.Url != null && f.IndexRangeEnd > 0 && !f.IsMuxed).ToList();

            if (dashFormats.Count == 0)
                return null;

            // Get duration from first format
            var durationMs = dashFormats.Max(f => f.ApproxDurationMs);
            if (durationMs <= 0) durationMs = 1;
            var durationSec = durationMs / 1000.0;

            var videoFormats = dashFormats.Where(f => f.IsVideo).OrderByDescending(f => f.Height).ToList();
            var audioFormats = dashFormats.Where(f => !f.IsVideo).OrderByDescending(f => f.Bitrate).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" ");
            sb.Append("profiles=\"urn:mpeg:dash:profile:isoff-main:2011\" ");
            sb.Append("type=\"static\" ");
            sb.Append("mediaPresentationDuration=\"PT" + durationSec.ToString("F3") + "S\" ");
            sb.AppendLine("minBufferTime=\"PT2S\">");
            sb.AppendLine("  <Period>");

            // Video AdaptationSet
            if (videoFormats.Count > 0)
            {
                sb.AppendLine("    <AdaptationSet mimeType=\"video/mp4\" subsegmentAlignment=\"true\" startWithSAP=\"1\">");
                foreach (var f in videoFormats)
                {
                    // Only include codecs supported by Windows 10 Mobile (avc1)
                    if (f.Codecs == null || !f.Codecs.StartsWith("avc1"))
                        continue;

                    sb.Append("      <Representation id=\"" + f.Itag + "\"");
                    sb.Append(" codecs=\"" + EscapeXml(f.Codecs) + "\"");
                    sb.Append(" bandwidth=\"" + f.Bitrate + "\"");
                    sb.Append(" width=\"" + f.Width + "\"");
                    sb.Append(" height=\"" + f.Height + "\"");
                    if (f.Fps > 0)
                        sb.Append(" frameRate=\"" + f.Fps + "\"");
                    sb.AppendLine(">");
                    sb.AppendLine("        <BaseURL>" + EscapeXml(f.Url) + "</BaseURL>");
                    sb.Append("        <SegmentBase indexRange=\"" + f.IndexRangeStart + "-" + f.IndexRangeEnd + "\">");
                    sb.Append("<Initialization range=\"" + f.InitRangeStart + "-" + f.InitRangeEnd + "\"/>");
                    sb.AppendLine("</SegmentBase>");
                    sb.AppendLine("      </Representation>");
                }
                sb.AppendLine("    </AdaptationSet>");
            }

            // Audio AdaptationSet
            if (audioFormats.Count > 0)
            {
                sb.AppendLine("    <AdaptationSet mimeType=\"audio/mp4\" subsegmentAlignment=\"true\" startWithSAP=\"1\">");
                foreach (var f in audioFormats)
                {
                    // Only include mp4a codecs (AAC) for compatibility
                    if (f.Codecs == null || !f.Codecs.StartsWith("mp4a"))
                        continue;

                    sb.Append("      <Representation id=\"" + f.Itag + "\"");
                    sb.Append(" codecs=\"" + EscapeXml(f.Codecs) + "\"");
                    sb.Append(" bandwidth=\"" + f.Bitrate + "\"");
                    if (f.AudioSampleRate > 0)
                        sb.Append(" audioSamplingRate=\"" + f.AudioSampleRate + "\"");
                    sb.AppendLine(">");
                    if (f.AudioChannels > 0)
                    {
                        sb.AppendLine("        <AudioChannelConfiguration schemeIdUri=\"urn:mpeg:dash:23003:3:audio_channel_configuration:2011\" value=\"" + f.AudioChannels + "\"/>");
                    }
                    sb.AppendLine("        <BaseURL>" + EscapeXml(f.Url) + "</BaseURL>");
                    sb.Append("        <SegmentBase indexRange=\"" + f.IndexRangeStart + "-" + f.IndexRangeEnd + "\">");
                    sb.Append("<Initialization range=\"" + f.InitRangeStart + "-" + f.InitRangeEnd + "\"/>");
                    sb.AppendLine("</SegmentBase>");
                    sb.AppendLine("      </Representation>");
                }
                sb.AppendLine("    </AdaptationSet>");
            }

            sb.AppendLine("  </Period>");
            sb.AppendLine("</MPD>");

            return sb.ToString();
        }

        private static string EscapeXml(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
