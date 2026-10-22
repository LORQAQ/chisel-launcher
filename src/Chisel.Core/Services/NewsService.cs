using System.Text.Json;
using System.Text.Json.Serialization;
using Chisel.Core.Models;
using Chisel.Core.Net;

namespace Chisel.Core.Services;

public sealed class NewsService
{
    private const string NewsUrl = "https://launchercontent.mojang.com/v2/news.json";
    private const string PatchNotesUrl = "https://launchercontent.mojang.com/v2/javaPatchNotes.json";
    private const string AssetBase = "https://launchercontent.mojang.com";
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<NewsItem>? _news;
    private List<NewsItem>? _patches;
    private DateTimeOffset _newsAt;
    private DateTimeOffset _patchesAt;

    public NewsService(AppPaths paths) => _paths = paths;

    public async Task<List<NewsItem>> GetNewsAsync(bool force, CancellationToken ct)
    {
        if (!force && _news != null && DateTimeOffset.UtcNow - _newsAt < TimeSpan.FromMinutes(20)) return _news;
        await _gate.WaitAsync(ct);
        try
        {
            if (!force && _news != null && DateTimeOffset.UtcNow - _newsAt < TimeSpan.FromMinutes(20)) return _news;
            var cachePath = Path.Combine(_paths.CacheDir, "news.json");
            try
            {
                var data = await GetAsync<NewsResponse>(NewsUrl, ct);
                _news = data.Entries
                    .Where(e => e.NewsType?.Any(t => t.Contains("Java", StringComparison.OrdinalIgnoreCase)) == true)
                    .Take(12)
                    .Select(e => new NewsItem
                    {
                        Title = e.Title,
                        Tag = "news",
                        Category = e.Category,
                        Date = e.Date,
                        Text = TrimText(e.Text, 280),
                        ImageUrl = ImageUrl(e.NewsPageImage?.Url ?? e.PlayPageImage?.Url),
                        Link = SafeLink(e.ReadMoreLink)
                    })
                    .ToList();
                if (_news.Count == 0)
                {
                    _news = data.Entries.Take(8).Select(e => new NewsItem
                    {
                        Title = e.Title,
                        Tag = "news",
                        Category = e.Category,
                        Date = e.Date,
                        Text = TrimText(e.Text, 280),
                        ImageUrl = ImageUrl(e.NewsPageImage?.Url ?? e.PlayPageImage?.Url),
                        Link = SafeLink(e.ReadMoreLink)
                    }).ToList();
                }
                _newsAt = DateTimeOffset.UtcNow;
                try { AtomicJson.Write(cachePath, _news); } catch { }
                return _news;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                var cached = AtomicJson.Read(cachePath, () => new List<NewsItem>());
                if (cached.Count > 0)
                {
                    _news = cached;
                    _newsAt = DateTimeOffset.UtcNow.AddMinutes(-15);
                    return cached;
                }
                throw new IOException("暂时无法获取 Minecraft 新闻。");
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<List<NewsItem>> GetPatchNotesAsync(bool force, CancellationToken ct)
    {
        if (!force && _patches != null && DateTimeOffset.UtcNow - _patchesAt < TimeSpan.FromMinutes(30)) return _patches;
        await _gate.WaitAsync(ct);
        try
        {
            if (!force && _patches != null && DateTimeOffset.UtcNow - _patchesAt < TimeSpan.FromMinutes(30)) return _patches;
            var cachePath = Path.Combine(_paths.CacheDir, "patchnotes.json");
            try
            {
                var data = await GetAsync<PatchNotesResponse>(PatchNotesUrl, ct);
                _patches = data.Entries.Take(10).Select(e => new NewsItem
                {
                    Title = e.Title,
                    Tag = "patch",
                    Category = string.Equals(e.Type, "snapshot", StringComparison.OrdinalIgnoreCase)
                        ? "快照 · " + e.Version
                        : "正式版 · " + e.Version,
                    Date = e.Date.Length >= 10 ? e.Date[..10] : e.Date,
                    Text = TrimText(e.ShortText, 220),
                    ImageUrl = ImageUrl(e.Image?.Url)
                }).ToList();
                _patchesAt = DateTimeOffset.UtcNow;
                try { AtomicJson.Write(cachePath, _patches); } catch { }
                return _patches;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                var cached = AtomicJson.Read(cachePath, () => new List<NewsItem>());
                if (cached.Count > 0)
                {
                    _patches = cached;
                    _patchesAt = DateTimeOffset.UtcNow.AddMinutes(-15);
                    return cached;
                }
                throw new IOException("暂时无法获取 Java 版更新记录。");
            }
        }
        finally { _gate.Release(); }
    }

    private static async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        var text = await Web.Client.GetStringAsync(url, cts.Token);
        return JsonSerializer.Deserialize<T>(text, AtomicJson.Compact)
               ?? throw new IOException("Mojang 返回了空内容");
    }

    private static string? ImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.StartsWith('/') ? AssetBase + url : url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    private static string? SafeLink(string? url)
        => !string.IsNullOrWhiteSpace(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;

    private static string TrimText(string? text, int max)
    {
        var value = (text ?? "").Replace('\u202f', ' ').Replace("\r", "").Replace("\n", " ").Trim();
        return value.Length <= max ? value : value[..max].TrimEnd() + "…";
    }

    private sealed class NewsResponse
    {
        [JsonPropertyName("entries")] public List<NewsEntry> Entries { get; set; } = [];
    }

    private sealed class NewsEntry
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("newsType")] public List<string>? NewsType { get; set; }
        [JsonPropertyName("readMoreLink")] public string? ReadMoreLink { get; set; }
        [JsonPropertyName("playPageImage")] public NewsImage? PlayPageImage { get; set; }
        [JsonPropertyName("newsPageImage")] public NewsImage? NewsPageImage { get; set; }
    }

    private sealed class NewsImage
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }

    private sealed class PatchNotesResponse
    {
        [JsonPropertyName("entries")] public List<PatchNoteEntry> Entries { get; set; } = [];
    }

    private sealed class PatchNoteEntry
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("shortText")] public string? ShortText { get; set; }
        [JsonPropertyName("image")] public NewsImage? Image { get; set; }
    }
}
