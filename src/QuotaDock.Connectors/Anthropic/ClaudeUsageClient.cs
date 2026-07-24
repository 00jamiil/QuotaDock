using System.Net;
using System.Net.Http.Headers;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Anthropic;

public interface IClaudeUsageClient
{
    Task<string> ReadUsageAsync(ClaudeCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>
/// Queries Claude's account usage window using the local Claude Code OAuth
/// access token. The token is attached only as a bearer header on a single
/// read-only GET; it is never persisted. Auth failures surface as
/// <see cref="ConnectionHealth.AuthenticationRequired"/> so the UI can prompt the
/// user to refresh Claude Code, rather than fabricating zero usage.
/// </summary>
public sealed class ClaudeUsageClient(HttpClient httpClient) : IClaudeUsageClient
{
    // Anthropic exposes the Claude Code usage window at this OAuth-authenticated
    // endpoint. The beta header matches the value Claude Code sends for
    // OAuth-scoped calls.
    private static readonly Uri UsageEndpoint = new("https://api.anthropic.com/api/oauth/usage");
    private const string OAuthBetaHeader = "oauth-2025-04-20";

    public async Task<string> ReadUsageAsync(
        ClaudeCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);
        request.Headers.UserAgent.ParseAdd("QuotaDock/0.2");

        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ClaudeUsageException(
                ConnectionHealth.AuthenticationRequired,
                "Claude sign-in is required. Open Claude Code and sign in, then retry.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new ClaudeUsageException(
                ConnectionHealth.RateLimited,
                "Claude asked QuotaDock to slow down.",
                retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero ? retryAfter : null);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ClaudeUsageException(
                ConnectionHealth.Unavailable,
                $"Claude usage service returned HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ClaudeUsageException(
    ConnectionHealth health,
    string userMessage,
    TimeSpan? retryAfter = null) : Exception(userMessage)
{
    public ConnectionHealth Health { get; } = health;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
