using System.Runtime.Serialization;

namespace subwayTube.Models
{    [DataContract]
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

    [DataContract]
    public class DownloadItem
    {
        [DataMember]
        public string VideoId { get; set; }
        [DataMember]
        public string Title { get; set; }
        [DataMember]
        public string Author { get; set; }
        [DataMember]
        public string AuthorId { get; set; }
        [DataMember]
        public string ThumbnailUrl { get; set; }
        [DataMember]
        public string ThumbnailLocalUri { get; set; }
        [DataMember]
        public string FileName { get; set; }
        [DataMember]
        public long FileSize { get; set; }

        public string SizeText
        {
            get
            {
                string[] units = { "bytes", "KB", "MB", "GB" };
                double size = FileSize;
                int unit = 0;
                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }
                return unit == 0
                    ? FileSize + " " + units[unit]
                    : size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[unit];
            }
        }

        public string DisplayThumbnail
        {
            get { return string.IsNullOrEmpty(ThumbnailLocalUri) ? ThumbnailUrl : ThumbnailLocalUri; }
        }
    }

    [DataContract]
    public class BackupData
    {
        [DataMember]
        public string Info { get; set; }
        [DataMember]
        public System.Collections.Generic.List<Subscription> Subscriptions { get; set; }
        [DataMember]
        public System.Collections.Generic.List<HistoryItem> History { get; set; }
        [DataMember]
        public System.Collections.Generic.List<SearchHistoryItem> SearchHistory { get; set; }
    }
}