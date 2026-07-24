using System.Net;
using QuotaDock.Connectors.Anthropic;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class AnthropicOrganizationConnectorTests
{
    [Fact]
    public async Task FetchAsync_AggregatesMessageUsageAndConvertsCostCentsToUsd()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/usage_report/messages", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""
                    {
                      "data":[{"results":[{
                        "uncached_input_tokens":1000,
                        "cache_read_input_tokens":250,
                        "cache_creation":{"ephemeral_5m_input_tokens":50,"ephemeral_1h_input_tokens":25},
                        "output_tokens":400,
                        "workspace_id":"ws_1",
                        "model":"claude-sonnet"
                      }]}],
                      "has_more":false,
                      "next_page":null
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/cost_report", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""
                    {"data":[{"results":[{"amount":"125","currency":"USD","workspace_id":"ws_1","description":"Claude Sonnet"}]}],"has_more":false,"next_page":null}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vault = new MemorySecretVault();
        await vault.SaveAsync("anthropic-secret", "sk-ant-admin-test-value");
        var connector = new AnthropicOrganizationConnector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") },
            vault,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero)));

        var result = await connector.FetchAsync(new ConnectorConnection(
            "anthropic-org", ProviderKind.Anthropic, "Work", DataSourceKind.OfficialApi,
            "anthropic-secret", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1325m, result.Snapshot!.Metrics.Single(m => m.Id == "anthropic-input-tokens").Current);
        Assert.Equal(400m, result.Snapshot.Metrics.Single(m => m.Id == "anthropic-output-tokens").Current);
        Assert.Equal(1.25m, result.Snapshot.Metrics.Single(m => m.Id == "anthropic-spend").Current);
        var workspaceTokens = result.Snapshot.Metrics.Single(metric =>
            metric.Scope == MetricScope.Project && metric.Kind == MetricKind.Tokens);
        Assert.Equal(1725m, workspaceTokens.Current);
        Assert.Equal("ws_1", workspaceTokens.Dimensions["workspace_id"]);
        Assert.Equal("claude-sonnet", workspaceTokens.Dimensions["model"]);
        var workspaceSpend = result.Snapshot.Metrics.Single(metric =>
            metric.Scope == MetricScope.Project && metric.Kind == MetricKind.Currency);
        Assert.Equal(1.25m, workspaceSpend.Current);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("sk-ant-admin-test-value", request.Headers.GetValues("x-api-key").Single());
            Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        });
        Assert.Contains("group_by%5B%5D=workspace_id", handler.Requests[0].RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_MapsUnauthorizedToAuthenticationRequired()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var vault = new MemorySecretVault();
        await vault.SaveAsync("anthropic-secret", "sk-ant-admin-test-value");
        var connector = new AnthropicOrganizationConnector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") },
            vault,
            TimeProvider.System);

        var result = await connector.FetchAsync(new ConnectorConnection(
            "anthropic-org", ProviderKind.Anthropic, "Work", DataSourceKind.OfficialApi,
            "anthropic-secret", null), CancellationToken.None);

        Assert.Equal(ConnectionHealth.AuthenticationRequired, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }
}
