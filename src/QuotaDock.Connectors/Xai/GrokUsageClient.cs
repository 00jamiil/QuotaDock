using System.Net;
using System.Net.Http.Headers;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Xai;

public interface IGrokUsageClient
{
    Task<string> ReadUsageAsync(GrokCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>
/// Queries xAI account usage using the local Grok Build OAuth access token.
/// The token is attached only as a bearer header on a single read-only GET;
/// it is never persisted. Auth failures surface as
/// <see cref="ConnectionHealth.AuthenticationRequired"/> so the UI can prompt
/// a re-login rather than fabricating zero usage.
/// </summary>
/// <remarks>
/// xAI does not currently publish a stable, documented endpoint for remaining
/// subscription credit balance. The endpoint below is a best-effort assumption
/// against the public API base; the parser fails closed, so if the live shape
/// differs QuotaDock reports "format changed" instead of inventing usage.
/// Verify and adjust <see cref="UsageEndpoint"/> against the live Grok API.
/// </remarks>
public sealed class GrokUsageClient(HttpClient httpClient) : IGrokUsageClient
{
    private static readonly Uri UsageEndpoint = new("https://api.x.ai/v1/usage");

    public async Task<string> ReadUsageAsync(
        GrokCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.UserAgent.ParseAdd("QuotaDock/0.2");

        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new GrokUsageException(
                ConnectionHealth.AuthenticationRequired,
                "Grok sign-in is required. Run the Grok CLI login, then retry.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new GrokUsageException(
                ConnectionHealth.RateLimited,
                "xAI asked QuotaDock to slow down.",
                retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero ? retryAfter : null);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GrokUsageException(
                ConnectionHealth.Unavailable,
                $"xAI usage service returned HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class GrokUsageException(
    ConnectionHealth health,
    string userMessage,
    TimeSpan? retryAfter = null) : Exception(userMessage)
{
    public ConnectionHealth Health { get; } = health;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
