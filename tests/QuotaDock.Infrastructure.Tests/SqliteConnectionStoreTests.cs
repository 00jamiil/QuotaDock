using QuotaDock.Core.Domain;
using QuotaDock.Infrastructure.Persistence;

namespace QuotaDock.Infrastructure.Tests;

public sealed class SqliteConnectionStoreTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"quotadock-connections-{Guid.NewGuid():N}.db");
    private SqliteConnectionStore store = null!;

    public async Task InitializeAsync()
    {
        store = new SqliteConnectionStore(databasePath);
        await store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await store.DisposeAsync();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsOnlyNonSecretConnectionData()
    {
        var connection = new ConnectorConnection(
            "openai-org",
            ProviderKind.OpenAI,
            "Work",
            DataSourceKind.OfficialApi,
            "vault-openai-org",
            new Dictionary<string, string> { ["softBudgetUsd"] = "50" });

        await store.SaveAsync(connection);

        var loaded = Assert.Single(await store.LoadAllAsync());
        Assert.Equal(connection.Id, loaded.Id);
        Assert.Equal(connection.Provider, loaded.Provider);
        Assert.Equal(connection.AccountLabel, loaded.AccountLabel);
        Assert.Equal(connection.Source, loaded.Source);
        Assert.Equal(connection.SecretReference, loaded.SecretReference);
        Assert.Equal("50", loaded.Settings!["softBudgetUsd"]);
        Assert.DoesNotContain("sk-admin-super-secret", await File.ReadAllTextAsync(databasePath));
    }

    [Fact]
    public async Task DeleteAsync_RemovesConnectionMetadata()
    {
        await store.SaveAsync(new ConnectorConnection(
            "anthropic-org", ProviderKind.Anthropic, "Work", DataSourceKind.OfficialApi,
            "vault-anthropic-org", null));

        await store.DeleteAsync("anthropic-org");

        Assert.Empty(await store.LoadAllAsync());
    }
}
