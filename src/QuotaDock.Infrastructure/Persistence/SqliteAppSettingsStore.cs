using System.Text.Json;
using Microsoft.Data.Sqlite;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Configuration;

namespace QuotaDock.Infrastructure.Persistence;

public sealed class SqliteAppSettingsStore : IAppSettingsStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public SqliteAppSettingsStore(string databasePath)
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

            CREATE TABLE IF NOT EXISTS app_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                payload_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM app_settings WHERE id = 1;";
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (payload is null)
        {
            return AppSettings.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(payload, SerializerOptions) ?? AppSettings.Default;
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var payload = JsonSerializer.Serialize(settings, SerializerOptions);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings(id, payload_json, updated_at)
            VALUES (1, $payload, $updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                payload_json = excluded.payload_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
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
