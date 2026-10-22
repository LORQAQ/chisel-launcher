using System.Diagnostics;

namespace Chisel.Core.Net;

public static class SourceProbe
{
    private const long TtlMs = 10 * 60_000;
    private const int CacheCap = 24;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object CacheGate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Dictionary<string, (bool Prefer, long At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static long _globalAt = -60_000;
    private static bool _globalPrefer;

    /// <summary>清单与 API 用的全局判断：官方 piston-meta 对 BMCLAPI。</summary>
    public static async Task<bool> PreferMirrorAsync(string source, CancellationToken ct)
    {
        if (Is(source, "mirror")) return true;
        if (Is(source, "official")) return false;

        lock (CacheGate)
        {
            if (Clock.ElapsedMilliseconds - _globalAt < TtlMs) return _globalPrefer;
        }

        await Gate.WaitAsync(ct);
        try
        {
            lock (CacheGate)
            {
                if (Clock.ElapsedMilliseconds - _globalAt < TtlMs) return _globalPrefer;
            }

            _globalPrefer = await DecideAsync(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                Mirror.Manifest(), ct);
            lock (CacheGate) _globalAt = Clock.ElapsedMilliseconds;
            return _globalPrefer;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// 按条目真实涉及的「官方主机 → 镜像主机」分别探测。清单快不代表 resources.download 的资产 CDN 快，
    /// 用同一个结论决定所有批次会把整批小文件压在慢源上，每个文件都要烧完重试才换源。
    /// </summary>
    public static async Task<HostPreference> ResolveAsync(
        string source, IReadOnlyList<DownloadItem> items, CancellationToken ct)
    {
        if (Is(source, "official")) return HostPreference.Const(false);

        // 显式「镜像优先」也做吞吐比较，但只有慢到官方四分之一以下才换走：
        // 用户选镜像通常因为官方对他更慢，不能拿一次抖动就推翻他的选择。
        var keepMirror = Is(source, "mirror");
        var fallback = keepMirror || await PreferMirrorAsync(source, ct);

        var pairs = new List<(string Key, string Official, string Mirror)>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.MirrorUrl)) continue;
            var key = (keepMirror ? "keep:" : "auto:") + Key(item.Url, item.MirrorUrl!);
            if (pairs.Exists(p => p.Key == key)) continue;
            pairs.Add((key, item.Url, item.MirrorUrl!));
            if (pairs.Count >= CacheCap) break;
        }

        if (pairs.Count == 0) return HostPreference.Const(fallback);

        var decisions = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(string Key, string Official, string Mirror)>();
        lock (CacheGate)
        {
            foreach (var pair in pairs)
            {
                if (Cache.TryGetValue(pair.Key, out var hit) && Clock.ElapsedMilliseconds - hit.At < TtlMs)
                    decisions[pair.Key] = hit.Prefer;
                else pending.Add(pair);
            }
        }

        if (pending.Count > 0)
        {
            await Gate.WaitAsync(ct);
            try
            {
                var probed = await Task.WhenAll(pending.Select(async pair =>
                {
                    lock (CacheGate)
                    {
                        if (Cache.TryGetValue(pair.Key, out var hit) && Clock.ElapsedMilliseconds - hit.At < TtlMs)
                            return (pair.Key, hit.Prefer);
                    }

                    var prefer = await DecideAsync(pair.Official, pair.Mirror, ct, keepMirror);
                    lock (CacheGate)
                    {
                        Cache[pair.Key] = (prefer, Clock.ElapsedMilliseconds);
                        if (Cache.Count > CacheCap)
                        {
                            foreach (var stale in Cache
                                         .OrderByDescending(e => e.Value.At)
                                         .Skip(CacheCap)
                                         .Select(e => e.Key).ToList())
                                Cache.Remove(stale);
                        }
                    }

                    return (pair.Key, prefer);
                }));

                foreach (var (key, prefer) in probed) decisions[key] = prefer;
            }
            finally
            {
                Gate.Release();
            }
        }

        return HostPreference.Cached(fallback, decisions);
    }

    private static async Task<bool> DecideAsync(
        string officialUrl, string mirrorUrl, CancellationToken ct, bool keepMirror = false)
    {
        var officialTask = ThroughputAsync(officialUrl, ct);
        var mirrorTask = ThroughputAsync(mirrorUrl, ct);
        await Task.WhenAll(officialTask, mirrorTask);
        var official = officialTask.Result;
        var mirror = mirrorTask.Result;

        // 按实测吞吐比，而不是首字节延迟：镜像常常是「一次快速 302 + 一个很慢的回源」，
        // 首字节看着差不多，摊到几千个资产文件上能差几十倍。
        if (mirror < 0) return false;
        if (official < 0) return true;
        return keepMirror ? mirror >= official * 0.25 : mirror >= official * 1.2;
    }

    /// <summary>取前 64 KB 采样，返回字节/秒；拿不到返回 -1。</summary>
    private static async Task<double> ThroughputAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await HttpTransfer.GetAsync(url, cts.Token, 0, 65535);
            if (!response.IsSuccessStatusCode) return -1;

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[8192];
            var watch = Stopwatch.StartNew();
            long total = 0;
            int read;
            while (watch.ElapsedMilliseconds < 6000 &&
                   (read = await HttpTransfer.ReadAsync(stream, buffer, cts.Token)) > 0)
                total += read;
            watch.Stop();
            return total <= 0 ? -1 : total * 1000.0 / watch.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }

    private static string Key(string officialUrl, string mirrorUrl) =>
        HostOf(officialUrl) + " -> " + HostOf(mirrorUrl);

    private static string HostOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
        var start = url.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return url;
        var rest = url[(start + 3)..];
        var slash = rest.AsSpan().IndexOfAny('/', '?');
        return slash < 0 ? rest : rest[..slash];
    }

    private static bool Is(string source, string value) =>
        string.Equals(source, value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>一个下载批次的主机偏好：优先走镜像，还是优先走官方。</summary>
public sealed class HostPreference
{
    private readonly bool _fallback;
    private readonly IReadOnlyDictionary<string, bool>? _decisions;

    private HostPreference(bool fallback, IReadOnlyDictionary<string, bool>? decisions)
    {
        _fallback = fallback;
        _decisions = decisions;
    }

    public static HostPreference Const(bool preferMirror) => new(preferMirror, null);

    public static HostPreference Cached(bool fallback, IReadOnlyDictionary<string, bool> decisions) =>
        new(fallback, decisions);

    public bool PreferMirror(DownloadItem item)
    {
        if (_decisions == null || string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.MirrorUrl))
            return _fallback;
        var key = HostOf(item.Url) + " -> " + HostOf(item.MirrorUrl!);
        return _decisions.TryGetValue(key, out var prefer) ? prefer : _fallback;
    }

    private static string HostOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
        return url;
    }
}
