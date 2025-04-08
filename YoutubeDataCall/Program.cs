using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Net;
using System.Threading.Channels;

namespace YoutubeDataCall
{
    public class Program
    {
        private const string CHANNEL_NAME = "@Letsplay";
        private const string NUMBER_OF_VIDEOS = "1000";
        private const string START_DATE = "2013-03-25T00:00:00Z";
        private const string END_DATE = "2025-04-02T23:59:59Z";
        // New constant for video title search (set the prefix you want)
        private const string SEARCHABLE_VIDEO_TITLE = "GTA V";

        private static string? API_KEY;
        static async Task Main()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            API_KEY = configuration["YouTube:API_KEY"];
            if (string.IsNullOrEmpty(API_KEY))
            {
                Console.WriteLine("API_KEY not found in appsettings.json");
                return;
            }

            // Get the channel ID from the channel name
            string? channelId = await GetChannelIdAsync(CHANNEL_NAME);
            if (string.IsNullOrEmpty(channelId))
            {
                Console.WriteLine("Channel not found.");
                return;
            }
            Console.WriteLine($"Channel ID: {channelId}");

            // Get video IDs for the channel (this now includes prefix filtering on the title)
            List<string> videoIds = await GetVideoIdsAsync(channelId);
            if (videoIds.Count == 0)
            {
                Console.WriteLine("No videos found for this channel with the specified title prefix.");
                return;
            }

            // Get details (including view counts) for the videos
            await GetVideoDetailsAsync(videoIds);
        }

        // Searches for a channel by name and returns its channel ID.
        private static async Task<string?> GetChannelIdAsync(string channelName)
        {
            using HttpClient client = new();

            // Using the search endpoint to find the channel
            string url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=channel&q={Uri.EscapeDataString(channelName)}&key={API_KEY}";
            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Error fetching channel info.");
                return null;
            }
            string content = await response.Content.ReadAsStringAsync();
            dynamic? json = JsonConvert.DeserializeObject(content);

            if (json?.items.Count == 0)
                return null;

            // Depending on the response structure, the channel ID may be in different locations.
            string? channelId = json?.items[0].id.channelId;
            if (string.IsNullOrEmpty(channelId))
            {
                channelId = json?.items[0].snippet.channelId;
            }
            return channelId;
        }

        // Retrieves a list of video IDs for the specified channel and filters by title prefix.
        private static async Task<List<string>> GetVideoIdsAsync(string channelId)
        {
            // Corrected instantiation of the list.
            List<string> videoIds = [];
            using HttpClient client = new();

            // Build the URL for search by channel and date range.
            // Here we use the "q" parameter to narrow down the results, but note this doesn't enforce prefix matching.
            string url = $"https://www.googleapis.com/youtube/v3/search?part=snippet" +
                         $"&channelId={channelId}" +
                         $"&maxResults={NUMBER_OF_VIDEOS}" +
                         $"&type=video" +
                         $"&order=viewCount" +
                         $"&publishedAfter={START_DATE}" +
                         $"&publishedBefore={END_DATE}";

            // Optionally, adding the title search term helps narrow the result set.
            if (!string.IsNullOrEmpty(SEARCHABLE_VIDEO_TITLE))
            {
                url += $"&q={Uri.EscapeDataString(SEARCHABLE_VIDEO_TITLE)}";
            }

            url += $"&key={API_KEY}";

            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Error fetching video list.");
                return videoIds;
            }
            string content = await response.Content.ReadAsStringAsync();
            dynamic? json = JsonConvert.DeserializeObject(content);

            foreach (var item in json?.items!)
            {
                string rawTitle = item.snippet.title;
                string videoTitle = WebUtility.HtmlDecode(rawTitle).Trim();
                // Enforce the title prefix condition (case-insensitive)
                if (!string.IsNullOrEmpty(SEARCHABLE_VIDEO_TITLE) &&
                    !videoTitle.Contains(SEARCHABLE_VIDEO_TITLE, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Skipping video: {videoTitle}, Searching for: {SEARCHABLE_VIDEO_TITLE}");
                    continue;
                }

                string videoId = item.id.videoId;
                if (!string.IsNullOrEmpty(videoId))
                {
                    videoIds.Add(videoId);
                }
            }
            return videoIds;
        }

        // Fetches and prints the title, view count, and published date of each video.
        private static async Task GetVideoDetailsAsync(List<string> videoIds)
        {
            using HttpClient client = new();
            // Create a comma-separated list of video IDs.
            string ids = string.Join(",", videoIds);
            // Use the videos endpoint to get video details (snippet and statistics).
            string url = $"https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics&id={ids}&key={API_KEY}";
            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Error fetching video details.");
                return;
            }
            string content = await response.Content.ReadAsStringAsync();
            dynamic? json = JsonConvert.DeserializeObject(content);

            Console.WriteLine("{0,-90} {1,15} {2,20}", "Title", "Views", "Published Date");
            Console.WriteLine(new string('-', 90 + 15 + 20 + 2)); // total width (including spaces)

            foreach (var video in json?.items!)
            {
                string title = video.snippet.title;
                string datePublished = video.snippet.publishedAt;
                string viewCountString = video.statistics.viewCount;
                string formattedViewCount = long.TryParse(viewCountString, out long viewCount)
                                              ? viewCount.ToString("N0")
                                              : viewCountString;

                Console.WriteLine("{0,-90} {1,15} {2,20}", title, formattedViewCount, datePublished);
            }
        }
    }
}
