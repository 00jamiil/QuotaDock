using System.Net;
using QuotaDock.Core.Abstractions;

namespace QuotaDock.Connectors.Tests;

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class MemorySecretVault : ISecretVault
{
    private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);

    public ValueTask SaveAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        secrets[reference] = secret;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
    {
        secrets.TryGetValue(reference, out var value);
        return ValueTask.FromResult(value);
    }

    public ValueTask RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        secrets.Remove(reference);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

