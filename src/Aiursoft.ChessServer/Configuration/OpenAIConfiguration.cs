namespace Aiursoft.ChessServer.Configuration;

public class OpenAIConfiguration
{
    public string CompletionApiUrl { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 60;
    public bool IsConfigured => Uri.TryCreate(CompletionApiUrl, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http";
}
