using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
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

        public ObservableCollection<Subscription> Subscriptions { get; private set; } = new ObservableCollection<Subscription>();
        public ObservableCollection<HistoryItem> History { get; private set; } = new ObservableCollection<HistoryItem>();
        public List<SearchHistoryItem> SearchHistory { get; private set; } = new List<SearchHistoryItem>();

        public async Task LoadAllAsync()
        {
            var subs = await LoadListAsync<Subscription>(SubscriptionsFile);
            Subscriptions = new ObservableCollection<Subscription>(subs ?? new List<Subscription>());
            var hist = await LoadListAsync<HistoryItem>(HistoryFile);
            History = new ObservableCollection<HistoryItem>(hist ?? new List<HistoryItem>());
            SearchHistory = await LoadListAsync<SearchHistoryItem>(SearchHistoryFile) ?? new List<SearchHistoryItem>();
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
            var serializer = new DataContractJsonSerializer(typeof(List<T>));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, list);
                var json = Encoding.UTF8.GetString(stream.ToArray());
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
        }
    }
}
