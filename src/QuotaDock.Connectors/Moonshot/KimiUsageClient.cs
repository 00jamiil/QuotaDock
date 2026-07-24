using System.Net;
using System.Net.Http.Headers;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Moonshot;

public interface IKimiUsageClient
{
    Task<string> ReadUsageAsync(KimiCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>
/// Queries Kimi account usage using the local Kimi Code OAuth access token.
/// The token is attached only as a bearer header on a single read-only GET; it
/// is never persisted. Auth failures surface as
/// <see cref="ConnectionHealth.AuthenticationRequired"/> so the UI can prompt
/// a re-login rather than fabricating zero usage.
/// </summary>
/// <remarks>
/// Kimi Code is a Claude Code fork, so its usage window endpoint mirrors
/// Claude's OAuth usage shape. The exact host is not publicly documented; the
/// endpoint below is a best-effort assumption and the parser fails closed, so a
/// differing live shape reports "format changed" instead of inventing usage.
/// Verify and adjust <see cref="UsageEndpoint"/> against the live Kimi API.
/// </remarks>
public sealed class KimiUsageClient(HttpClient httpClient) : IKimiUsageClient
{
    private static readonly Uri UsageEndpoint = new("https://api.kimi.com/api/oauth/usage");
    private const string OAuthBetaHeader = "oauth-2025-04-20";

    public async Task<string> ReadUsageAsync(
        KimiCredentials credentials,
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
            throw new KimiUsageException(
                ConnectionHealth.AuthenticationRequired,
                "Kimi sign-in is required. Run the Kimi CLI login, then retry.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new KimiUsageException(
                ConnectionHealth.RateLimited,
                "Kimi asked QuotaDock to slow down.",
                retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero ? retryAfter : null);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new KimiUsageException(
                ConnectionHealth.Unavailable,
                $"Kimi usage service returned HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class KimiUsageException(
    ConnectionHealth health,
    string userMessage,
    TimeSpan? retryAfter = null) : Exception(userMessage)
{
    public ConnectionHealth Health { get; } = health;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
