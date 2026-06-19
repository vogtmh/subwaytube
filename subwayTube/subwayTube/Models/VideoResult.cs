namespace subwayTube.Models
{
    public class ChannelRef
    {
        public string Name { get; set; }
        public string ChannelId { get; set; }
    }

    public class VideoResult
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string AuthorId { get; set; }
        public string Duration { get; set; }
        public string ThumbnailUrl { get; set; }
        public string ViewCount { get; set; }
        public string PublishedText { get; set; }
        public string AuthorThumbnailUrl { get; set; }
        // All channels credited on the video (collaborations have more than one).
        public System.Collections.Generic.List<ChannelRef> Channels { get; set; }
        // "video", "short" or "channel" — distinguishes search result types.
        public string Kind { get; set; } = "video";
    }
}
