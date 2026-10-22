using System.Net.Http;

namespace Chisel.Core.Net;

public static class HttpTransfer
{
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    public static Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
        => GetAsync(url, ct, null, null);

    public static async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct, long? from, long? to)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(HeaderTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (from.HasValue)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from.Value, to);
        try
        {
            return await Web.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("连接下载服务器超时。");
        }
    }

    public static async Task<int> ReadAsync(Stream input, Memory<byte> buffer, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(IdleTimeout);
        try
        {
            return await input.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("下载数据读取超时。");
        }
    }
}
