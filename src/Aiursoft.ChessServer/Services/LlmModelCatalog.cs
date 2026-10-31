using System.Net.Http.Headers;
using System.Text.Json;
using Aiursoft.ChessServer.Configuration;
using Aiursoft.Scanner.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aiursoft.ChessServer.Services;

public class LlmModelCatalog(IHttpClientFactory clients, IMemoryCache cache, IOptions<OpenAIConfiguration> options) : IScopedDependency
{
    public async Task<string[]> GetModelsAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.IsConfigured) return [];
        var endpoint = new Uri(configuration.CompletionApiUrl);
        const string suffix = "/chat/completions";
        if (!endpoint.AbsolutePath.EndsWith(suffix, StringComparison.Ordinal)) return [];
        var modelsUri = new UriBuilder(endpoint) { Path = endpoint.AbsolutePath[..^suffix.Length] + "/models", Query = string.Empty }.Uri;
        var key = (typeof(LlmModelCatalog), modelsUri, configuration.Token);
        if (cache.TryGetValue<string[]>(key, out var cached)) return cached!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
        if (!string.IsNullOrEmpty(configuration.Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);
        using var response = await clients.CreateClient().SendAsync(request, deadline.Token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token);
        var models = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        cache.Set(key, models, TimeSpan.FromMinutes(1));
        return models;
    }
}
