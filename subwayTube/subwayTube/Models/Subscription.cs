using System.Runtime.Serialization;

namespace subwayTube.Models
{
    [DataContract]
    public class Subscription
    {
        [DataMember]
        public string AuthorId { get; set; }
        [DataMember]
        public string Author { get; set; }
        [DataMember]
        public string ThumbnailUrl { get; set; }
    }

    [DataContract]
    public class HistoryItem
    {
        [DataMember]
        public string VideoId { get; set; }
        [DataMember]
        public string Title { get; set; }
        [DataMember]
        public string ThumbnailUrl { get; set; }
        [DataMember]
        public string AuthorId { get; set; }
        [DataMember]
        public string Author { get; set; }
    }

    [DataContract]
    public class SearchHistoryItem
    {
        [DataMember]
        public string Query { get; set; }
    }
}
