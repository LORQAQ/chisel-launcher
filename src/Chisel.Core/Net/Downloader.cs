using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Chisel.Core.Net;

public sealed class DownloadItem
{
    public required string Url { get; init; }
    public string? MirrorUrl { get; init; }
    public string? FallbackUrl { get; init; }
    public required string Destination { get; init; }
    public string? Sha1 { get; init; }
    public long Size { get; init; }
    public bool VerifyExisting { get; init; } = true;
    public string Label { get; init; } = "";
}

public sealed class DownloadProgress
{
    private long _completedBytes;
    private long _inFlightBytes;
    private long _completedFiles;
    private long _totalBytes;
    private long _totalFiles;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _speedGate = new();
    private long _sampleBytes;
    private long _sampleMs;
    private double _speed;

    public void Plan(long bytes, int files)
    {
        Interlocked.Add(ref _totalBytes, bytes);
        Interlocked.Add(ref _totalFiles, files);
    }

    public void Stream(long delta) => Interlocked.Add(ref _inFlightBytes, delta);

    public void Complete(DownloadItem item, long streamedBytes = 0)
    {
        if (streamedBytes != 0) Interlocked.Add(ref _inFlightBytes, -streamedBytes);
        Interlocked.Add(ref _completedBytes, item.Size > 0 ? item.Size : Math.Max(0, streamedBytes));
        Interlocked.Add(ref _completedFiles, 1);
    }

    public long CompletedBytes => Math.Max(0, Interlocked.Read(ref _completedBytes) + Interlocked.Read(ref _inFlightBytes));
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public int CompletedFiles => (int)Math.Min(int.MaxValue, Interlocked.Read(ref _completedFiles));
    public int TotalFiles => (int)Math.Min(int.MaxValue, Interlocked.Read(ref _totalFiles));

    public double BytesPerSecond
    {
        get
        {
            var now = _clock.ElapsedMilliseconds;
            var bytes = CompletedBytes;
            lock (_speedGate)
            {
                var elapsed = now - _sampleMs;
                if (elapsed < 600) return _speed;
                var delta = Math.Max(0, bytes - _sampleBytes);
                var instant = delta * 1000.0 / elapsed;
                _speed = _speed <= 0 ? instant : _speed * 0.55 + instant * 0.45;
                if (delta == 0) _speed *= 0.7;
                _sampleBytes = bytes;
                _sampleMs = now;
                return _speed;
            }
        }
    }
}

public sealed class Downloader
{
    private readonly string _source;

    public Downloader(string source) => _source = source;

    public async Task DownloadAllAsync(
        IReadOnlyList<DownloadItem> items,
        int concurrency,
        DownloadProgress progress,
        Action<string>? onItem,
        CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Math.Clamp(concurrency, 1, 32));
        var errors = new List<Exception>();
        var errorLock = new object();
        var hosts = await SourceProbe.ResolveAsync(_source, items, ct);

        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                onItem?.Invoke(item.Label);
                await DownloadOneAsync(item, progress, hosts.PreferMirror(item), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                lock (errorLock) errors.Add(new IOException($"下载失败：{item.Label}", ex));
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
        if (errors.Count > 0) throw errors[0];
    }

    private async Task DownloadOneAsync(DownloadItem item, DownloadProgress progress, bool preferMirror, CancellationToken ct)
    {
        var destination = item.Destination;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            var length = new FileInfo(destination).Length;
            var sizeOk = item.Size <= 0 || length == item.Size;
            if (sizeOk && (!item.VerifyExisting || string.IsNullOrEmpty(item.Sha1) || await HashMatchesAsync(destination, item.Sha1, ct)))
            {
                progress.Complete(item);
                return;
            }
            try { File.Delete(destination); } catch { }
        }

        var urls = BuildUrls(item, preferMirror);
        Exception? last = null;
        foreach (var url in urls)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var temp = destination + ".part";
                var streamed = 0L;
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    if (item.Size >= 8 * 1024 * 1024)
                    {
                        var ranged = await TrySegmentedAsync(url, item, temp, progress, ct);
                        if (ranged >= 0)
                        {
                            streamed = ranged;
                            File.Move(temp, destination, overwrite: true);
                            progress.Complete(item, ranged);
                            return;
                        }
                    }

                    using var response = await HttpTransfer.GetAsync(url, ct);
                    response.EnsureSuccessStatusCode();
                    await using (var input = await response.Content.ReadAsStreamAsync(ct))
                    await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
                    {
                        var buffer = new byte[131072];
                        long total = 0;
                        int read;
                        var guard = new StallGuard();
                        while ((read = await HttpTransfer.ReadAsync(input, buffer, ct)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                            sha.AppendData(buffer, 0, read);
                            total += read;
                            streamed += read;
                            progress.Stream(read);
                            guard.Observe(total);
                        }
                        if (item.Size > 0 && total != item.Size)
                            throw new IOException($"大小不符（期望 {item.Size}，收到 {total}）");
                        if (!string.IsNullOrEmpty(item.Sha1))
                        {
                            var actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                            if (!string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
                                throw new IOException($"SHA-1 不符（期望 {item.Sha1[..Math.Min(8, item.Sha1.Length)]}）");
                        }
                    }
                    File.Move(temp, destination, overwrite: true);
                    progress.Complete(item, streamed);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    throw;
                }
                catch (SlowSourceException ex)
                {
                    last = ex;
                    if (streamed != 0) progress.Stream(-streamed);
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (streamed != 0) progress.Stream(-streamed);
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    await Task.Delay(320 * (attempt + 1), ct);
                }
            }
        }
        throw new IOException($"所有下载源均失败：{last?.Message}", last);
    }

    private async Task<long> TrySegmentedAsync(
        string url,
        DownloadItem item,
        string temp,
        DownloadProgress progress,
        CancellationToken ct)
    {
        const int maxSegments = 4;
        var size = item.Size;
        if (size < 8 * 1024 * 1024) return -1;

        using (var probe = await HttpTransfer.GetAsync(url, ct, 0, 0))
        {
            if (probe.StatusCode != System.Net.HttpStatusCode.PartialContent) return -1;
            var range = probe.Content.Headers.ContentRange;
            if (range?.Length is { } length && length != size) return -1;
        }

        var segments = (int)Math.Min(maxSegments, Math.Max(2, size / (4 * 1024 * 1024)));
        var partSize = (size + segments - 1) / segments;
        var parts = new string[segments];
        var streamedTotal = 0L;
        try
        {
            var tasks = Enumerable.Range(0, segments).Select(async index =>
            {
                var from = index * partSize;
                var to = Math.Min(size - 1, from + partSize - 1);
                var expected = to - from + 1;
                var partPath = temp + ".seg" + index;
                parts[index] = partPath;

                // 上一轮失败时保留的分段可以接着用：同一个目标文件按字节区间寻址，
                // 换源续传也是安全的，最后的 SHA-1 会兜底。
                var have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                if (have > expected)
                {
                    File.Delete(partPath);
                    have = 0;
                }

                if (have < expected)
                {
                    using var response = await HttpTransfer.GetAsync(url, ct, from + have, to);
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                        throw new IOException("服务器不支持分段下载");
                    await using var input = await response.Content.ReadAsStreamAsync(ct);
                    await using var output = new FileStream(partPath,
                        have > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
                    if (have > 0) output.Seek(have, SeekOrigin.Begin);
                    var buffer = new byte[131072];
                    long partTotal = have;
                    int read;
                    var guard = new StallGuard();
                    while ((read = await HttpTransfer.ReadAsync(input, buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        partTotal += read;
                        Interlocked.Add(ref streamedTotal, read);
                        progress.Stream(read);
                        guard.Observe(partTotal);
                    }

                    if (partTotal < expected) throw new IOException("分段提前断流");
                }
            }).ToList();

            await Task.WhenAll(tasks);

            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                foreach (var part in parts)
                {
                    await using var input = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                    await input.CopyToAsync(output, 131072, ct);
                }
            }

            var actualSize = new FileInfo(temp).Length;
            if (actualSize != size) throw new IOException($"大小不符（期望 {size}，收到 {actualSize}）");
            if (!string.IsNullOrEmpty(item.Sha1) && !await HashMatchesAsync(temp, item.Sha1, ct))
                throw new IOException("SHA-1 不符");

            DeleteParts(parts);
            return streamedTotal;
        }
        catch (SlowSourceException)
        {
            // 慢源要往上抛，让调用方换下一个地址；保留分段，下一个源从已有字节续传，
            // 而不是把 20 多 MB 直接丢掉重来。
            if (streamedTotal > 0) progress.Stream(-streamedTotal);
            throw;
        }
        catch
        {
            if (streamedTotal > 0) progress.Stream(-streamedTotal);
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            DeleteParts(parts);
            return -1;
        }
    }

    private static void DeleteParts(IReadOnlyList<string?> parts)
    {
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            try { if (File.Exists(part)) File.Delete(part); } catch { }
        }
    }

    /// <summary>
    /// 能连上但只有几 KB/s 的源必须被放弃：IdleTimeout 要 60 秒收不到任何字节才触发，
    /// 涓流式慢速永远绕得过它，一个 23 MB 的库能挂一个小时。连续两个窗口都慢、
    /// 且整段均值也慢才判死，避免误杀刚起步的连接。
    /// </summary>
    private sealed class StallGuard
    {
        private const int WindowMs = 5000;
        private const double FloorBytesPerSecond = 24 * 1024;

        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _windowStartMs;
        private long _windowStartBytes;
        private int _slowWindows;

        public void Observe(long totalBytes)
        {
            var now = _watch.ElapsedMilliseconds;
            var elapsed = now - _windowStartMs;
            if (elapsed < WindowMs) return;

            var rate = (totalBytes - _windowStartBytes) * 1000.0 / elapsed;
            _windowStartMs = now;
            _windowStartBytes = totalBytes;
            if (rate >= FloorBytesPerSecond)
            {
                _slowWindows = 0;
                return;
            }

            var overall = now > 0 ? totalBytes * 1000.0 / now : 0;
            if (overall < FloorBytesPerSecond && ++_slowWindows >= 2)
                throw new SlowSourceException($"下载速度过低（{rate / 1024:F0} KB/s）");
        }
    }

    /// <summary>源活着但太慢：同一条地址上重试没有意义，直接换下一个源。</summary>
    private sealed class SlowSourceException : IOException
    {
        public SlowSourceException(string message) : base(message) { }
    }

    private IReadOnlyList<string> BuildUrls(DownloadItem item, bool preferMirror)
    {
        var list = new List<string>(3);
        if (preferMirror && !string.IsNullOrWhiteSpace(item.MirrorUrl)) list.Add(item.MirrorUrl!);
        if (!string.IsNullOrWhiteSpace(item.Url)) list.Add(item.Url);
        if (!string.IsNullOrWhiteSpace(item.MirrorUrl) &&
            !list.Contains(item.MirrorUrl!, StringComparer.OrdinalIgnoreCase))
            list.Add(item.MirrorUrl!);
        if (!string.IsNullOrWhiteSpace(item.FallbackUrl) &&
            !list.Contains(item.FallbackUrl!, StringComparer.OrdinalIgnoreCase))
            list.Add(item.FallbackUrl!);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var hash = await SHA1.HashDataAsync(stream, ct);
            return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
