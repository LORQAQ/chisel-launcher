using System.Diagnostics;

namespace Chisel.Core.Net;

public static class SourceProbe
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object CacheGate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _at = -60_000;
    private static bool _preferMirror;

    public static async Task<bool> PreferMirrorAsync(string source, CancellationToken ct)
    {
        if (string.Equals(source, "mirror", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(source, "official", StringComparison.OrdinalIgnoreCase)) return false;

        lock (CacheGate)
        {
            if (Clock.ElapsedMilliseconds - _at < 10 * 60_000) return _preferMirror;
        }

        await Gate.WaitAsync(ct);
        try
        {
            lock (CacheGate)
            {
                if (Clock.ElapsedMilliseconds - _at < 10 * 60_000) return _preferMirror;
            }

            var mirrorTask = LatencyAsync(Mirror.Manifest(), ct);
            var officialTask = LatencyAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", ct);
            await Task.WhenAll(mirrorTask, officialTask);
            var mirror = mirrorTask.Result;
            var official = officialTask.Result;

            _preferMirror = mirror >= 0 && (official < 0 || mirror <= official * 1.25);
            lock (CacheGate) _at = Clock.ElapsedMilliseconds;
            return _preferMirror;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<long> LatencyAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var watch = Stopwatch.StartNew();
            using var response = await HttpTransfer.GetAsync(url, cts.Token, 0, 0);
            var code = (int)response.StatusCode;
            if (code is < 200 or >= 400) return -1;
            return watch.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
