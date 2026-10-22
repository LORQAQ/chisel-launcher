using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Chisel.Core.Net;

public static class Web
{
    public static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(8),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 128,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(45),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(20)
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("chisel-launcher", "0.1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static async Task<T> GetJsonAsync<T>(string url, TimeSpan? timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, AtomicJson.Compact, cts.Token);
        if (value is null) throw new InvalidOperationException("服务返回了空内容");
        return value;
    }

    public static async Task<T> PostJsonAsync<T>(string url, object body, CancellationToken ct, string? bearer = null, TimeSpan? timeout = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(body, AtomicJson.Compact), System.Text.Encoding.UTF8, "application/json");
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await Client.SendAsync(request, cts.Token);
        var text = await response.Content.ReadAsStringAsync(cts.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(text, 600)}");
        return JsonSerializer.Deserialize<T>(text, AtomicJson.Compact)
               ?? throw new InvalidOperationException("服务返回了空内容");
    }

    public static async Task<T> GetJsonAsync<T>(string url, CancellationToken ct, string? bearer = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(25));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearer != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await Client.SendAsync(request, cts.Token);
        var text = await response.Content.ReadAsStringAsync(cts.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(text, 600)}");
        return JsonSerializer.Deserialize<T>(text, AtomicJson.Compact)
               ?? throw new InvalidOperationException("服务返回了空内容");
    }

    public static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
