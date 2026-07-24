using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Infrastructure.Persistence;

public sealed class SqliteSnapshotStore : ISnapshotStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string connectionString;

    public SqliteSnapshotStore(string databasePath)
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

            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                connection_id TEXT NOT NULL,
                captured_at TEXT NOT NULL,
                payload_json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_snapshots_connection_captured
                ON snapshots(connection_id, captured_at DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(
        UsageSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var payload = JsonSerializer.Serialize(snapshot, SerializerOptions);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO snapshots(connection_id, captured_at, payload_json)
            VALUES ($connectionId, $capturedAt, $payload);
            """;
        command.Parameters.AddWithValue("$connectionId", snapshot.ConnectionId);
        command.Parameters.AddWithValue("$capturedAt", snapshot.CapturedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageSnapshot?> LoadLatestAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload_json
            FROM snapshots
            WHERE connection_id = $connectionId
            ORDER BY captured_at DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$connectionId", connectionId);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return payload is null
            ? null
            : JsonSerializer.Deserialize<UsageSnapshot>(payload, SerializerOptions);
    }

    public async Task<IReadOnlyList<UsageSnapshot>> LoadLatestForAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.payload_json
            FROM snapshots AS s
            INNER JOIN (
                SELECT connection_id, MAX(id) AS latest_id
                FROM snapshots
                GROUP BY connection_id
            ) AS latest ON latest.latest_id = s.id
            ORDER BY s.connection_id;
            """;

        var snapshots = new List<UsageSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var snapshot = JsonSerializer.Deserialize<UsageSnapshot>(reader.GetString(0), SerializerOptions);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    public async Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snapshots WHERE captured_at < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToUniversalTime().ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteForConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snapshots WHERE connection_id = $connectionId;";
        command.Parameters.AddWithValue("$connectionId", connectionId);
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
