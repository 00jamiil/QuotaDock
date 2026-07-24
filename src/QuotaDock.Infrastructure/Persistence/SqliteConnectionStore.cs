using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Infrastructure.Persistence;

public sealed class SqliteConnectionStore : IConnectionStore, IAsyncDisposable
{
    private readonly string connectionString;

    public SqliteConnectionStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS connections (
                id TEXT PRIMARY KEY,
                provider TEXT NOT NULL,
                account_label TEXT NOT NULL,
                source TEXT NOT NULL,
                secret_reference TEXT NULL,
                settings_json TEXT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var settingsJson = connection.Settings is null
            ? null
            : JsonSerializer.Serialize(connection.Settings);

        await using var database = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = database.CreateCommand();
        command.CommandText = """
            INSERT INTO connections(
                id, provider, account_label, source, secret_reference, settings_json, updated_at)
            VALUES (
                $id, $provider, $accountLabel, $source, $secretReference, $settingsJson, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                provider = excluded.provider,
                account_label = excluded.account_label,
                source = excluded.source,
                secret_reference = excluded.secret_reference,
                settings_json = excluded.settings_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", connection.Id);
        command.Parameters.AddWithValue("$provider", connection.Provider.ToString());
        command.Parameters.AddWithValue("$accountLabel", connection.AccountLabel);
        command.Parameters.AddWithValue("$source", connection.Source.ToString());
        command.Parameters.AddWithValue("$secretReference", (object?)connection.SecretReference ?? DBNull.Value);
        command.Parameters.AddWithValue("$settingsJson", (object?)settingsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConnectorConnection>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, provider, account_label, source, secret_reference, settings_json
            FROM connections
            ORDER BY provider, account_label, id;
            """;

        var connections = new List<ConnectorConnection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Enum.TryParse<ProviderKind>(reader.GetString(1), out var provider) ||
                !Enum.TryParse<DataSourceKind>(reader.GetString(3), out var source))
            {
                continue;
            }

            IReadOnlyDictionary<string, string>? settings = null;
            if (!reader.IsDBNull(5))
            {
                settings = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5));
            }

            connections.Add(new ConnectorConnection(
                reader.GetString(0),
                provider,
                reader.GetString(2),
                source,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                settings));
        }

        return connections;
    }

    public async Task DeleteAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM connections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", connectionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM connections;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        return ValueTask.CompletedTask;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
