using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using subwayTube.Models;

namespace subwayTube.Services
{
    public class DataService
    {
        private const string SubscriptionsFile = "subscriptions.json";
        private const string HistoryFile = "history.json";
        private const string SearchHistoryFile = "searchhistory.json";
        private const string DownloadsFile = "downloads.json";
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        public ObservableCollection<Subscription> Subscriptions { get; private set; } = new ObservableCollection<Subscription>();
        public ObservableCollection<HistoryItem> History { get; private set; } = new ObservableCollection<HistoryItem>();
        public List<SearchHistoryItem> SearchHistory { get; private set; } = new List<SearchHistoryItem>();
        public ObservableCollection<DownloadItem> Downloads { get; private set; } = new ObservableCollection<DownloadItem>();

        public async Task LoadAllAsync()
        {
            var subs = await LoadListAsync<Subscription>(SubscriptionsFile);
            Subscriptions = new ObservableCollection<Subscription>(subs ?? new List<Subscription>());
            var hist = await LoadListAsync<HistoryItem>(HistoryFile);
            History = new ObservableCollection<HistoryItem>(hist ?? new List<HistoryItem>());
            SearchHistory = await LoadListAsync<SearchHistoryItem>(SearchHistoryFile) ?? new List<SearchHistoryItem>();
            var dls = await LoadListAsync<DownloadItem>(DownloadsFile);
            Downloads = new ObservableCollection<DownloadItem>(dls ?? new List<DownloadItem>());
        }

        // Subscriptions
        public bool IsSubscribed(string authorId)
        {
            foreach (var s in Subscriptions)
                if (s.AuthorId == authorId) return true;
            return false;
        }

        public async Task AddSubscription(string authorId, string author, string thumbnailUrl)
        {
            if (IsSubscribed(authorId)) return;
            var item = new Subscription { AuthorId = authorId, Author = author, ThumbnailUrl = thumbnailUrl };
            // Insert in sorted position
            int idx = 0;
            while (idx < Subscriptions.Count && string.Compare(Subscriptions[idx].Author, author, StringComparison.OrdinalIgnoreCase) < 0)
                idx++;
            Subscriptions.Insert(idx, item);
            await SaveListAsync(Subscriptions.ToList(), SubscriptionsFile);
        }

        public async Task RemoveSubscription(string authorId)
        {
            for (int i = Subscriptions.Count - 1; i >= 0; i--)
                if (Subscriptions[i].AuthorId == authorId)
                    Subscriptions.RemoveAt(i);
            await SaveListAsync(Subscriptions.ToList(), SubscriptionsFile);
        }

        public async Task ToggleSubscription(string authorId, string author, string thumbnailUrl)
        {
            if (IsSubscribed(authorId))
                await RemoveSubscription(authorId);
            else
                await AddSubscription(authorId, author, thumbnailUrl);
        }

        // Play history
        public async Task AddHistoryItem(string videoId, string title, string thumbnailUrl, string authorId, string author)
        {
            // Remove existing entry for this video (avoid duplicates)
            for (int i = History.Count - 1; i >= 0; i--)
                if (History[i].VideoId == videoId)
                    History.RemoveAt(i);
            History.Add(new HistoryItem
            {
                VideoId = videoId,
                Title = title,
                ThumbnailUrl = thumbnailUrl,
                AuthorId = authorId,
                Author = author
            });
            // Keep only last 200 entries
            while (History.Count > 200)
                History.RemoveAt(0);
            await SaveListAsync(History.ToList(), HistoryFile);
        }

        // Search history
        public async Task AddSearchHistoryItem(string query)
        {
            SearchHistory.RemoveAll(s => s.Query == query);
            SearchHistory.Add(new SearchHistoryItem { Query = query });
            if (SearchHistory.Count > 50)
                SearchHistory.RemoveRange(0, SearchHistory.Count - 50);
            await SaveListAsync(SearchHistory, SearchHistoryFile);
        }

        // Clear play history
        public async Task ClearHistory()
        {
            History.Clear();
            await SaveListAsync(History.ToList(), HistoryFile);
        }

        // Downloads
        public bool IsDownloaded(string videoId)
        {
            foreach (var d in Downloads)
                if (d.VideoId == videoId) return true;
            return false;
        }

        public DownloadItem GetDownload(string videoId)
        {
            foreach (var d in Downloads)
                if (d.VideoId == videoId) return d;
            return null;
        }

        public async Task AddDownloadRecord(DownloadItem item)
        {
            // Remove any existing record for the same video first
            for (int i = Downloads.Count - 1; i >= 0; i--)
                if (Downloads[i].VideoId == item.VideoId)
                    Downloads.RemoveAt(i);
            Downloads.Insert(0, item);
            await SaveListAsync(Downloads.ToList(), DownloadsFile);
        }

        public async Task RemoveDownloadRecord(string videoId)
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
                if (Downloads[i].VideoId == videoId)
                    Downloads.RemoveAt(i);
            await SaveListAsync(Downloads.ToList(), DownloadsFile);
        }

        // Backup / restore (transfers subscriptions, history and search history,
        // but not downloaded videos)
        public string ExportBackup()
        {
            var backup = new BackupData
            {
                Info = "subwayTube backup",
                Subscriptions = Subscriptions.ToList(),
                History = History.ToList(),
                SearchHistory = SearchHistory.ToList()
            };
            var serializer = new DataContractJsonSerializer(typeof(BackupData));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, backup);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public async Task<bool> ImportBackupAsync(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            BackupData backup;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(BackupData));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    backup = serializer.ReadObject(stream) as BackupData;
                }
            }
            catch
            {
                return false;
            }

            if (backup == null)
                return false;

            Subscriptions.Clear();
            foreach (var s in backup.Subscriptions ?? new List<Subscription>())
                Subscriptions.Add(s);

            History.Clear();
            foreach (var h in backup.History ?? new List<HistoryItem>())
                History.Add(h);

            SearchHistory = backup.SearchHistory ?? new List<SearchHistoryItem>();

            await SaveListAsync(Subscriptions.ToList(), SubscriptionsFile);
            await SaveListAsync(History.ToList(), HistoryFile);
            await SaveListAsync(SearchHistory, SearchHistoryFile);
            return true;
        }

        // Generic JSON persistence
        private async Task<List<T>> LoadListAsync<T>(string fileName)
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(fileName);
                var json = await FileIO.ReadTextAsync(file);
                if (string.IsNullOrEmpty(json)) return new List<T>();
                var serializer = new DataContractJsonSerializer(typeof(List<T>));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(stream) as List<T> ?? new List<T>();
                }
            }
            catch (FileNotFoundException)
            {
                return new List<T>();
            }
            catch
            {
                return new List<T>();
            }
        }

        private async Task SaveListAsync<T>(List<T> list, string fileName)
        {
            await _saveLock.WaitAsync();
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(List<T>));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, list);
                    var json = Encoding.UTF8.GetString(stream.ToArray());
                    var folder = ApplicationData.Current.LocalFolder;
                    var tempName = fileName + ".tmp";
                    var tempFile = await folder.CreateFileAsync(tempName, CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(tempFile, json);
                    await tempFile.RenameAsync(fileName, NameCollisionOption.ReplaceExisting);
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }
    }
}
