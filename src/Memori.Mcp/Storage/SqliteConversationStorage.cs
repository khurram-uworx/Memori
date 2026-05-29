using Memori.Abstractions;
using Memori.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Memori.Mcp.Storage;

/// <summary>
/// SQLite-backed implementation of <see cref="IConversationStorage"/>.
/// Thread-safe, cancellation-aware, and durable across process restarts.
/// </summary>
public sealed class SqliteConversationStorage : IConversationStorage, IDisposable
{
    readonly string connectionString;
    readonly SemaphoreSlim initLock = new(1, 1);
    volatile bool initialized;

    /// <summary>
    /// Creates a new <see cref="SqliteConversationStorage"/> instance.
    /// </summary>
    public SqliteConversationStorage(SqliteStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var path = options.DatabasePath;
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("DatabasePath cannot be empty.", nameof(options));

        if (options.AutoCreateDatabase)
        {
            var fullPath = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        connectionString = $"Data Source={path}";
    }

    async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        await initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
                return;

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            command.CommandText = """
                CREATE TABLE IF NOT EXISTS entities (
                    id TEXT PRIMARY KEY,
                    scope TEXT
                );

                CREATE TABLE IF NOT EXISTS processes (
                    id TEXT PRIMARY KEY,
                    scope TEXT
                );

                CREATE TABLE IF NOT EXISTS sessions (
                    id TEXT PRIMARY KEY,
                    entity_id TEXT,
                    process_id TEXT,
                    scope TEXT
                );

                CREATE TABLE IF NOT EXISTS conversations (
                    id TEXT PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    summary TEXT
                );

                CREATE TABLE IF NOT EXISTS conversation_messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    conversation_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    type TEXT NOT NULL DEFAULT 'text',
                    created_at TEXT NOT NULL,
                    metadata TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_messages_conversation
                    ON conversation_messages(conversation_id, id);

                CREATE INDEX IF NOT EXISTS idx_conversations_session
                    ON conversations(session_id, updated_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            initialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    static string requireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty.", paramName);

        return value;
    }

    static string newId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <inheritdoc />
    public async ValueTask<string> GetOrCreateEntityAsync(
        string externalId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(externalId, nameof(externalId));

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO entities (id, scope) VALUES (@id, @scope)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@scope", (object?)scope ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return id;
    }

    /// <inheritdoc />
    public async ValueTask<string> GetOrCreateProcessAsync(
        string externalId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(externalId, nameof(externalId));

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO processes (id, scope) VALUES (@id, @scope)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@scope", (object?)scope ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return id;
    }

    /// <inheritdoc />
    public async ValueTask<string> GetOrCreateSessionAsync(
        string sessionId,
        string? entityId,
        string? processId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(sessionId, nameof(sessionId));

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sessions (id, entity_id, process_id, scope)
            VALUES (@id, @entityId, @processId, @scope)
            ON CONFLICT(id) DO UPDATE SET
                entity_id = COALESCE(entity_id, excluded.entity_id),
                process_id = COALESCE(process_id, excluded.process_id);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@entityId", (object?)entityId ?? DBNull.Value);
        command.Parameters.AddWithValue("@processId", (object?)processId ?? DBNull.Value);
        command.Parameters.AddWithValue("@scope", (object?)scope ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        transaction.Commit();
        return id;
    }

    /// <inheritdoc />
    public async ValueTask<Conversation> GetOrCreateConversationAsync(
        string sessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(sessionId, nameof(sessionId));

        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        // Ensure session exists (idempotent)
        using (var ensureSession = connection.CreateCommand())
        {
            ensureSession.Transaction = transaction;
            ensureSession.CommandText = """
                INSERT INTO sessions (id, entity_id, process_id, scope)
                VALUES (@id, NULL, NULL, NULL)
                ON CONFLICT(id) DO NOTHING;
                """;
            ensureSession.Parameters.AddWithValue("@id", id);
            await ensureSession.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Look for the most recent conversation within timeout
        var now = DateTimeOffset.UtcNow;

        using (var findCommand = connection.CreateCommand())
        {
            findCommand.Transaction = transaction;
            findCommand.CommandText = """
                SELECT id, session_id, created_at, updated_at, summary
                FROM conversations
                WHERE session_id = @sessionId
                ORDER BY updated_at DESC
                LIMIT 1;
                """;
            findCommand.Parameters.AddWithValue("@sessionId", id);

            using var reader = await findCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var updatedAt = DateTimeOffset.Parse(reader.GetString(3));
                if (now - updatedAt <= timeout)
                {
                    var conversation = new Conversation(
                        reader.GetString(0),
                        reader.GetString(1),
                        DateTimeOffset.Parse(reader.GetString(2)),
                        updatedAt,
                        reader.IsDBNull(4) ? null : reader.GetString(4));

                    transaction.Commit();
                    return conversation;
                }
            }
        }

        // Create a new conversation
        var conversationId = newId("conversation");
        var newConversation = new Conversation(
            conversationId,
            id,
            createdAt: now,
            updatedAt: now);

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO conversations (id, session_id, created_at, updated_at, summary)
                VALUES (@id, @sessionId, @createdAt, @updatedAt, NULL);
                """;
            insertCommand.Parameters.AddWithValue("@id", conversationId);
            insertCommand.Parameters.AddWithValue("@sessionId", id);
            insertCommand.Parameters.AddWithValue("@createdAt", now.ToString("O"));
            insertCommand.Parameters.AddWithValue("@updatedAt", now.ToString("O"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
        return newConversation;
    }

    /// <inheritdoc />
    public async ValueTask AppendMessagesAsync(
        string conversationId,
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messages);
        var convId = requireNonEmpty(conversationId, nameof(conversationId));

        if (messages.Count == 0)
            return;

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO conversation_messages (conversation_id, role, content, type, created_at, metadata)
                VALUES (@conversationId, @role, @content, @type, @createdAt, @metadata);
                """;
            command.Parameters.AddWithValue("@conversationId", convId);
            command.Parameters.AddWithValue("@role", message.Role);
            command.Parameters.AddWithValue("@content", message.Content);
            command.Parameters.AddWithValue("@type", message.Type);
            command.Parameters.AddWithValue("@createdAt", message.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("@metadata",
                SerializeMetadata(message.Metadata));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Update conversation timestamp
        using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = """
                UPDATE conversations SET updated_at = @updatedAt
                WHERE id = @conversationId;
                """;
            updateCommand.Parameters.AddWithValue("@updatedAt", now.ToString("O"));
            updateCommand.Parameters.AddWithValue("@conversationId", convId);
            var rows = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rows == 0)
                throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");
        }

        transaction.Commit();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var convId = requireNonEmpty(conversationId, nameof(conversationId));

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT role, content, type, created_at, metadata
            FROM conversation_messages
            WHERE conversation_id = @conversationId
            ORDER BY id ASC;
            """;
        command.Parameters.AddWithValue("@conversationId", convId);

        var messages = new List<ConversationMessage>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var role = reader.GetString(0);
            var content = reader.GetString(1);
            var type = reader.GetString(2);
            var createdAt = DateTimeOffset.Parse(reader.GetString(3));
            var metadata = reader.IsDBNull(4)
                ? null
                : DeserializeMetadata(reader.GetString(4));

            messages.Add(new ConversationMessage(role, content, type, createdAt, metadata));
        }

        return messages.AsReadOnly();
    }

    /// <inheritdoc />
    public async ValueTask UpdateConversationSummaryAsync(
        string conversationId,
        string summary,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = requireNonEmpty(conversationId, nameof(conversationId));
        ArgumentNullException.ThrowIfNull(summary);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE conversations
            SET summary = @summary, updated_at = @updatedAt
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@summary", summary);
        command.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (rows == 0)
            throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");

        transaction.Commit();
    }

    static string SerializeMetadata(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return "{}";

        return JsonSerializer.Serialize(metadata);
    }

    static IReadOnlyDictionary<string, object?>? DeserializeMetadata(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "{}")
            return null;

        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        return dict;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        initLock.Dispose();
    }
}
