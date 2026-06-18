using System;
using System.Collections.Generic;
using System.IO;
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

        public List<Subscription> Subscriptions { get; private set; } = new List<Subscription>();
        public List<HistoryItem> History { get; private set; } = new List<HistoryItem>();
        public List<SearchHistoryItem> SearchHistory { get; private set; } = new List<SearchHistoryItem>();

        public async Task LoadAllAsync()
        {
            Subscriptions = await LoadListAsync<Subscription>(SubscriptionsFile) ?? new List<Subscription>();
            History = await LoadListAsync<HistoryItem>(HistoryFile) ?? new List<HistoryItem>();
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
            Subscriptions.Add(new Subscription { AuthorId = authorId, Author = author, ThumbnailUrl = thumbnailUrl });
            Subscriptions.Sort((a, b) => string.Compare(a.Author, b.Author, StringComparison.OrdinalIgnoreCase));
            await SaveListAsync(Subscriptions, SubscriptionsFile);
        }

        public async Task RemoveSubscription(string authorId)
        {
            Subscriptions.RemoveAll(s => s.AuthorId == authorId);
            await SaveListAsync(Subscriptions, SubscriptionsFile);
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
            History.RemoveAll(h => h.VideoId == videoId);
            History.Add(new HistoryItem
            {
                VideoId = videoId,
                Title = title,
                ThumbnailUrl = thumbnailUrl,
                AuthorId = authorId,
                Author = author
            });
            // Keep only last 200 entries
            if (History.Count > 200)
                History.RemoveRange(0, History.Count - 200);
            await SaveListAsync(History, HistoryFile);
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
