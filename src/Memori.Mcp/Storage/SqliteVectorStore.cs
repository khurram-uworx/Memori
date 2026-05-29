using Microsoft.Data.Sqlite;
using Microsoft.Extensions.VectorData;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Numerics.Tensors;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Memori.Mcp.Storage;

file static class SqliteVectorStoreSchema
{
    public static PropertyInfo GetKeyProperty<TRecord>()
    {
        var type = typeof(TRecord);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<VectorStoreKeyAttribute>() is not null)
                return prop;
        }

        throw new InvalidOperationException(
            $"Type '{type.FullName}' does not have a property marked with [VectorStoreKey].");
    }

    public static PropertyInfo? GetVectorProperty<TRecord>()
    {
        var type = typeof(TRecord);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<VectorStoreVectorAttribute>() is not null)
                return prop;
        }

        return null;
    }

    public static IReadOnlyList<PropertyInfo> GetTextSearchableProperties<TRecord>()
    {
        var type = typeof(TRecord);
        var result = new List<PropertyInfo>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<VectorStoreDataAttribute>();
            if (attr is not null && attr.IsFullTextIndexed && prop.PropertyType == typeof(string))
                result.Add(prop);
        }

        if (result.Count == 0)
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (prop.PropertyType == typeof(string))
                    result.Add(prop);

        return result;
    }

    public static IReadOnlyList<PropertyInfo> GetDataProperties<TRecord>()
    {
        var type = typeof(TRecord);
        var result = new List<PropertyInfo>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (prop.GetCustomAttribute<VectorStoreDataAttribute>() is not null)
                result.Add(prop);

        return result;
    }
}

file sealed record ColumnDef(
    string ColumnName,
    PropertyInfo Property,
    Type PropertyType,
    bool IsNullable,
    bool IsFullTextIndexed,
    string SqliteType,
    Func<object?, object?> ToDb,
    Func<object?, object?> FromDb);

file sealed class SqliteVectorStoreCollection<TKey, TRecord> : VectorStoreCollection<TKey, TRecord>
    where TKey : notnull
    where TRecord : class
{
    readonly string name;
    readonly string connectionString;
    readonly string recordsTable;
    readonly string ftsTable;
    volatile bool deleted;

    static readonly PropertyInfo keyProperty = SqliteVectorStoreSchema.GetKeyProperty<TRecord>();
    static readonly PropertyInfo? vectorProperty = SqliteVectorStoreSchema.GetVectorProperty<TRecord>();
    static readonly IReadOnlyList<PropertyInfo> dataProperties = SqliteVectorStoreSchema.GetDataProperties<TRecord>();
    static readonly IReadOnlyList<PropertyInfo> textProperties = SqliteVectorStoreSchema.GetTextSearchableProperties<TRecord>();

    static readonly IReadOnlyList<ColumnDef> columns;
    static readonly string recordsTableSql;
    static readonly string ftsTableSql;

    static SqliteVectorStoreCollection()
    {
        columns = BuildColumns();

        var dataCols = new List<string>
        {
            "rowid INTEGER PRIMARY KEY AUTOINCREMENT",
            "__key TEXT NOT NULL UNIQUE"
        };

        if (vectorProperty is not null)
            dataCols.Add("__vector BLOB");

        foreach (var col in columns)
        {
            if (col.ColumnName == "__key" || col.ColumnName == "__vector")
                continue;

            var nullable = col.IsNullable ? string.Empty : " NOT NULL";
            dataCols.Add($"\"{col.ColumnName}\" {col.SqliteType}{nullable}");
        }

        recordsTableSql = $"CREATE TABLE IF NOT EXISTS \"{{0}}\" (\n  {string.Join(",\n  ", dataCols)}\n);";

        if (textProperties.Count > 0)
        {
            var ftsCols = textProperties.Select(p => $"\"{p.Name}\"").ToList();
            ftsTableSql = $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{{0}}\" USING fts5(\n  {string.Join(",\n  ", ftsCols)},\n  tokenize='porter unicode61'\n);";
        }
        else
        {
            ftsTableSql = string.Empty;
        }
    }

    internal static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name cannot be empty.", nameof(name));

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException(
                    $"Collection name '{name}' contains invalid character '{c}'. Only alphanumeric and underscore are allowed.");
        }

        return name;
    }

    public SqliteVectorStoreCollection(string name, string connectionString)
    {
        this.name = name ?? throw new ArgumentNullException(nameof(name));
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        var s = SanitizeName(name);
        recordsTable = s + "_records";
        ftsTable = s + "_fts";
    }

    static IReadOnlyList<ColumnDef> BuildColumns()
    {
        var result = new List<ColumnDef>
        {
            new("__key", keyProperty, typeof(TKey), false, false, "TEXT",
                v => v?.ToString() ?? string.Empty,
                v => v is string s ? (TKey)(object)s : (TKey)v!)
        };

        if (vectorProperty is not null)
        {
            result.Add(new("__vector", vectorProperty, typeof(ReadOnlyMemory<float>), true, false, "BLOB",
                v => SerializeVector((ReadOnlyMemory<float>)v!),
                v => v is byte[] bytes ? DeserializeVector(bytes) : ReadOnlyMemory<float>.Empty));
        }

        foreach (var prop in dataProperties)
        {
            var attr = prop.GetCustomAttribute<VectorStoreDataAttribute>();
            var isFullText = attr is not null && attr.IsFullTextIndexed && prop.PropertyType == typeof(string);
            var sqliteType = GetSqliteType(prop.PropertyType);
            var isNullable = !prop.PropertyType.IsValueType
                             || (prop.PropertyType.IsGenericType
                                 && prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>));

            result.Add(new(prop.Name, prop, prop.PropertyType, isNullable, isFullText, sqliteType,
                ToDbConverter(prop),
                FromDbConverter(prop)));
        }

        return result.AsReadOnly();
    }

    static string GetSqliteType(Type type)
    {
        if (type == typeof(string)) return "TEXT";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return "INTEGER";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "REAL";
        if (type == typeof(bool)) return "INTEGER";
        if (type == typeof(DateTimeOffset) || type == typeof(DateTime)) return "TEXT";
        if (type == typeof(Guid)) return "TEXT";
        if (type == typeof(ReadOnlyMemory<float>)) return "BLOB";

        return "TEXT";
    }

    static Func<object?, object?> ToDbConverter(PropertyInfo prop)
    {
        var t = prop.PropertyType;

        if (t == typeof(string)) return v => v is string s ? (object)s : DBNull.Value;
        if (t == typeof(int)) return v => v is not null ? (object)Convert.ToInt32(v) : DBNull.Value;
        if (t == typeof(long)) return v => v is not null ? (object)Convert.ToInt64(v) : DBNull.Value;
        if (t == typeof(double)) return v => v is not null ? (object)Convert.ToDouble(v) : DBNull.Value;
        if (t == typeof(float)) return v => v is not null ? (object)(double)Convert.ToSingle(v) : DBNull.Value;
        if (t == typeof(bool)) return v => v is not null ? (object)((bool)v ? 1 : 0) : DBNull.Value;
        if (t == typeof(DateTimeOffset)) return v => v is not null ? (object)((DateTimeOffset)v).ToString("O") : DBNull.Value;
        if (t == typeof(DateTime)) return v => v is not null ? (object)((DateTime)v).ToString("O") : DBNull.Value;
        if (t == typeof(Guid)) return v => v is not null ? (object)((Guid)v).ToString() : DBNull.Value;

        return v => v is null ? DBNull.Value : JsonSerializer.Serialize(v);
    }

    static Func<object?, object?> FromDbConverter(PropertyInfo prop)
    {
        var t = prop.PropertyType;

        if (t == typeof(string)) return v => v is DBNull ? null : v?.ToString();
        if (t == typeof(int)) return v => v is DBNull ? 0 : Convert.ToInt32(v);
        if (t == typeof(long)) return v => v is DBNull ? 0L : Convert.ToInt64(v);
        if (t == typeof(double)) return v => v is DBNull ? 0.0 : Convert.ToDouble(v);
        if (t == typeof(float)) return v => v is DBNull ? 0.0f : Convert.ToSingle(v);
        if (t == typeof(bool)) return v => v is DBNull ? false : Convert.ToInt32(v) != 0;
        if (t == typeof(DateTimeOffset)) return v => v is DBNull ? DateTimeOffset.MinValue : DateTimeOffset.Parse(v?.ToString()!);
        if (t == typeof(DateTime)) return v => v is DBNull ? DateTime.MinValue : DateTime.Parse(v?.ToString()!);
        if (t == typeof(Guid)) return v => v is DBNull ? Guid.Empty : Guid.Parse(v?.ToString()!);

        if (Nullable.GetUnderlyingType(t) is Type ut)
        {
            if (ut == typeof(int)) return v => v is DBNull ? null : (int?)Convert.ToInt32(v);
            if (ut == typeof(long)) return v => v is DBNull ? null : (long?)Convert.ToInt64(v);
            if (ut == typeof(double)) return v => v is DBNull ? null : (double?)Convert.ToDouble(v);
            if (ut == typeof(float)) return v => v is DBNull ? null : (float?)Convert.ToSingle(v);
            if (ut == typeof(bool)) return v => v is DBNull ? null : (bool?)(Convert.ToInt32(v) != 0);
            if (ut == typeof(DateTimeOffset)) return v => v is DBNull ? null : (DateTimeOffset?)DateTimeOffset.Parse(v?.ToString()!);
            if (ut == typeof(DateTime)) return v => v is DBNull ? null : (DateTime?)DateTime.Parse(v?.ToString()!);
            if (ut == typeof(Guid)) return v => v is DBNull ? null : (Guid?)Guid.Parse(v?.ToString()!);
        }

        return v =>
        {
            if (v is DBNull || v is null)
                return t.IsValueType ? Activator.CreateInstance(t) : null;

            if (v is string json)
                return JsonSerializer.Deserialize(json, t);

            return v;
        };
    }

    static byte[] SerializeVector(ReadOnlyMemory<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector.Span).CopyTo(bytes);
        return bytes;
    }

    static ReadOnlyMemory<float> DeserializeVector(byte[] bytes)
    {
        var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
        return floats.AsMemory();
    }

    static string EscapeFtsToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return token;

        return "\"" + token.Replace("\"", "\"\"") + "\"";
    }

    /// <inheritdoc />
    public override string Name => name;

    void checkNotDeleted()
    {
        if (deleted)
            throw new VectorStoreException($"Collection '{name}' has been deleted.")
            {
                CollectionName = name,
                OperationName = "write"
            };
    }

    static TKey extractKey(TRecord record)
    {
        var value = keyProperty.GetValue(record);
        return (TKey)value!;
    }

    SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    void BindRecordParameters(SqliteParameterCollection parameters, TRecord record)
    {
        foreach (var col in columns)
        {
            var value = col.Property.GetValue(record);
            parameters.AddWithValue("@" + col.ColumnName, col.ToDb(value));
        }
    }

    TRecord ReadRecord(SqliteDataReader reader)
    {
        var record = Activator.CreateInstance<TRecord>();

        foreach (var col in columns)
        {
            int ordinal;
            try
            {
                ordinal = reader.GetOrdinal(col.ColumnName);
            }
            catch (IndexOutOfRangeException)
            {
                continue;
            }

            if (reader.IsDBNull(ordinal))
            {
                if (!col.IsNullable && col.PropertyType.IsValueType)
                    col.Property.SetValue(record, col.FromDb(Activator.CreateInstance(col.PropertyType)));
                else
                    col.Property.SetValue(record, col.FromDb(null));

                continue;
            }

            col.Property.SetValue(record, col.FromDb(reader.GetValue(ordinal)));
        }

        return record;
    }

    static long GetLastInsertRowId(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid();";
        return (long)cmd.ExecuteScalar()!;
    }

    // --- Collection metadata methods ---

    /// <inheritdoc />
    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            cmd.Parameters.AddWithValue("@name", recordsTable);
            return Task.FromResult((long)cmd.ExecuteScalar()! > 0);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        var sql = string.Format(recordsTableSql, recordsTable);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (textProperties.Count > 0 && !string.IsNullOrEmpty(ftsTableSql))
        {
            var ftsSql = string.Format(ftsTableSql, ftsTable);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = ftsSql;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS \"{ftsTable}\"; DROP TABLE IF EXISTS \"{recordsTable}\";";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        deleted = true;
    }

    // --- CRUD methods ---

    /// <inheritdoc />
    public override async Task UpsertAsync(TRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);
        checkNotDeleted();

        var key = extractKey(record);
        var keyStr = key.ToString() ?? string.Empty;

        using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        long rowid;

        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = $"SELECT rowid FROM \"{recordsTable}\" WHERE \"__key\" = @key;";
            checkCmd.Parameters.AddWithValue("@key", keyStr);
            var existing = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (existing is long existingRowid)
            {
                rowid = existingRowid;

                var setClauses = new List<string>();
                using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;

                foreach (var col in columns)
                {
                    var value = col.Property.GetValue(record);
                    updateCmd.Parameters.AddWithValue("@" + col.ColumnName, col.ToDb(value));
                    if (col.ColumnName != "__key")
                        setClauses.Add($"\"{col.ColumnName}\" = @{col.ColumnName}");
                }

                updateCmd.CommandText = $"UPDATE \"{recordsTable}\" SET {string.Join(", ", setClauses)} WHERE \"__key\" = @__key;";
                await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                var colNames = string.Join(", ", columns.Select(c => $"\"{c.ColumnName}\""));
                var paramNames = string.Join(", ", columns.Select(c => "@" + c.ColumnName));
                insertCmd.CommandText = $"INSERT INTO \"{recordsTable}\" ({colNames}) VALUES ({paramNames});";
                BindRecordParameters(insertCmd.Parameters, record);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                rowid = GetLastInsertRowId(connection);
            }
        }

        // Sync FTS table
        if (textProperties.Count > 0)
        {
            using (var deleteFtsCmd = connection.CreateCommand())
            {
                deleteFtsCmd.Transaction = transaction;
                deleteFtsCmd.CommandText = $"DELETE FROM \"{ftsTable}\" WHERE \"rowid\" = @rowid;";
                deleteFtsCmd.Parameters.AddWithValue("@rowid", rowid);
                await deleteFtsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using var insertFtsCmd = connection.CreateCommand();
            insertFtsCmd.Transaction = transaction;
            var ftsCols = string.Join(", ", textProperties.Select(p => $"\"{p.Name}\"").Prepend("rowid"));
            var ftsParams = string.Join(", ", textProperties.Select(p => "@" + p.Name).Prepend("@rowid"));
            insertFtsCmd.CommandText = $"INSERT INTO \"{ftsTable}\" ({ftsCols}) VALUES ({ftsParams});";
            insertFtsCmd.Parameters.AddWithValue("@rowid", rowid);

            foreach (var textProp in textProperties)
            {
                var value = textProp.GetValue(record) as string ?? string.Empty;
                insertFtsCmd.Parameters.AddWithValue("@" + textProp.Name, value);
            }

            await insertFtsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    /// <inheritdoc />
    public override async Task UpsertAsync(IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(records);
        checkNotDeleted();

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task<TRecord?> GetAsync(TKey key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{recordsTable}\" WHERE \"__key\" = @key;";
            cmd.Parameters.AddWithValue("@key", key.ToString() ?? string.Empty);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return ReadRecord(reader);

            return null;
        }
        catch (SqliteException) when (!deleted)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        IEnumerable<TKey> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await GetAsync(key, options, cancellationToken).ConfigureAwait(false);
            if (record is not null)
                yield return record;
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<TRecord> GetAsync(
        Expression<Func<TRecord, bool>> filter,
        int top,
        FilteredRecordRetrievalOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);

        if (top <= 0)
            yield break;

        var filterFunc = filter.Compile();
        var skip = options?.Skip ?? 0;

        List<TRecord> results;
        try
        {
            using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{recordsTable}\";";

            results = [];
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var record = ReadRecord(reader);
                if (!filterFunc(record))
                    continue;

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                results.Add(record);

                if (results.Count >= top)
                    break;
            }
        }
        catch (SqliteException) when (!deleted)
        {
            yield break;
        }

        foreach (var record in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(TKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keyStr = key.ToString() ?? string.Empty;

        try
        {
            using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            long? rowid = null;

            if (textProperties.Count > 0)
            {
                using var getIdCmd = connection.CreateCommand();
                getIdCmd.Transaction = transaction;
                getIdCmd.CommandText = $"SELECT rowid FROM \"{recordsTable}\" WHERE \"__key\" = @key;";
                getIdCmd.Parameters.AddWithValue("@key", keyStr);
                var result = await getIdCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is long rid)
                    rowid = rid;
            }

            using var deleteCmd = connection.CreateCommand();
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = $"DELETE FROM \"{recordsTable}\" WHERE \"__key\" = @key;";
            deleteCmd.Parameters.AddWithValue("@key", keyStr);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (rowid.HasValue && textProperties.Count > 0)
            {
                using var deleteFtsCmd = connection.CreateCommand();
                deleteFtsCmd.Transaction = transaction;
                deleteFtsCmd.CommandText = $"DELETE FROM \"{ftsTable}\" WHERE \"rowid\" = @rowid;";
                deleteFtsCmd.Parameters.AddWithValue("@rowid", rowid.Value);
                await deleteFtsCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch (SqliteException) when (!deleted)
        {
        }
    }

    /// <inheritdoc />
    public override async Task DeleteAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<VectorSearchResult<TRecord>> SearchAsync<TInput>(
        TInput searchValue,
        int top,
        VectorSearchOptions<TRecord>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (top <= 0)
            yield break;

        var filterFunc = options?.Filter?.Compile();
        var scoreThreshold = options?.ScoreThreshold;
        var skip = options?.Skip ?? 0;

        if (searchValue is ReadOnlyMemory<float> queryVector)
        {
            // Vector similarity search
            if (vectorProperty is null)
                yield break;

            var scored = new List<(TRecord Record, double Score)>();

            try
            {
                using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT * FROM \"{recordsTable}\";";

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var record = ReadRecord(reader);

                    if (filterFunc is not null && !filterFunc(record))
                        continue;

                    var vectorValue = vectorProperty.GetValue(record);
                    if (vectorValue is not ReadOnlyMemory<float> recordVector)
                        continue;

                    if (recordVector.Length == 0 || queryVector.Length == 0)
                        continue;

                    double score;
                    try
                    {
                        score = TensorPrimitives.CosineSimilarity(queryVector.Span, recordVector.Span);
                    }
                    catch
                    {
                        continue;
                    }

                    if (double.IsNaN(score) || (scoreThreshold.HasValue && score < scoreThreshold.Value))
                        continue;

                    scored.Add((record, score));
                }
            }
            catch (SqliteException) when (!deleted)
            {
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            var returned = 0;
            foreach (var (record, score) in scored)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                yield return new VectorSearchResult<TRecord>(record, score);
                returned++;

                if (returned >= top)
                    yield break;
            }
        }
        else if (searchValue is string textQuery)
        {
            // FTS5 full-text search
            var ftsQuery = BuildFtsQuery(textQuery);
            if (string.IsNullOrEmpty(ftsQuery) || textProperties.Count == 0)
                yield break;

            var scored = new List<(TRecord Record, double Score)>();

            try
            {
                using var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

                // Verify FTS table exists
                using var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
                checkCmd.Parameters.AddWithValue("@name", ftsTable);
                var scalar = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var exists = scalar is long count && count > 0;

                if (!exists)
                    yield break;

                // Query using FTS5 MATCH and join back to records
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"""
                    SELECT r.*, f.rank
                    FROM (
                        SELECT rowid, rank FROM "{ftsTable}" WHERE "{ftsTable}" MATCH @query
                    ) f
                    INNER JOIN "{recordsTable}" r ON r.rowid = f.rowid
                    ORDER BY f.rank;
                    """;
                cmd.Parameters.AddWithValue("@query", ftsQuery);

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var record = ReadRecord(reader);

                    if (filterFunc is not null && !filterFunc(record))
                        continue;

                    double rank;
                    try
                    {
                        rank = reader.GetDouble(reader.GetOrdinal("rank"));
                    }
                    catch
                    {
                        rank = 0;
                    }

                    // Negate BM25 so higher = better
                    var score = -rank;

                    if (scoreThreshold.HasValue && score < scoreThreshold.Value)
                        continue;

                    scored.Add((record, score));
                }
            }
            catch (SqliteException) when (!deleted)
            {
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            var returned = 0;
            foreach (var (record, score) in scored)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                yield return new VectorSearchResult<TRecord>(record, score);
                returned++;

                if (returned >= top)
                    yield break;
            }
        }
        else
        {
            throw new VectorStoreException(
                $"Unsupported search input type '{typeof(TInput).FullName}'. " +
                "Supported types are string (text search) and ReadOnlyMemory<float> (vector search).");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    static string BuildFtsQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var terms = new List<string>();
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0)
            {
                var token = text[start..i];
                if (token.Length > 0)
                    terms.Add(EscapeFtsToken(token));
                start = -1;
            }
        }

        return terms.Count switch
        {
            0 => string.Empty,
            1 => terms[0],
            _ => string.Join(" AND ", terms)
        };
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(SqliteVectorStoreCollection<TKey, TRecord>))
            return this;

        if (serviceType == typeof(VectorStoreCollection<TKey, TRecord>))
            return this;

        if (serviceType == typeof(IVectorSearchable<TRecord>))
            return this;

        return null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            deleted = true;
    }
}

/// <summary>
/// SQLite-backed implementation of <see cref="VectorStore"/>.
/// Creates <see cref="SqliteVectorStoreCollection{TKey,TRecord}"/> instances that
/// persist data in SQLite tables with FTS5 full-text search support.
/// </summary>
public sealed class SqliteVectorStore : VectorStore
{
    readonly ConcurrentDictionary<string, object> collections = new(StringComparer.Ordinal);
    readonly object gate = new();
    readonly string connectionString;

    /// <summary>
    /// Creates a new <see cref="SqliteVectorStore"/> instance.
    /// </summary>
    public SqliteVectorStore(SqliteStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var path = options.DatabasePath;
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("DatabasePath cannot be empty.", nameof(options));

        connectionString = $"Data Source={path}";
    }

    static string sanitizeCollectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Collection name cannot be empty.", nameof(name));

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException(
                    $"Collection name '{name}' contains invalid character '{c}'. Only alphanumeric and underscore are allowed.");
        }

        return name;
    }

    /// <inheritdoc />
    [RequiresDynamicCode("This API requires dynamic code generation for schema discovery.")]
    [RequiresUnreferencedCode("This API uses reflection to discover VectorStore attributes on TRecord.")]
    public override VectorStoreCollection<TKey, TRecord> GetCollection<TKey, TRecord>(
        string name,
        VectorStoreCollectionDefinition? definition = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (gate)
        {
            if (collections.TryGetValue(name, out var collection))
            {
                if (collection is SqliteVectorStoreCollection<TKey, TRecord> typed)
                    return typed;

                throw new VectorStoreException(
                    $"Collection '{name}' already exists with a different record type.");
            }

            var newCollection = new SqliteVectorStoreCollection<TKey, TRecord>(name, connectionString);
            collections[name] = newCollection;
            return newCollection;
        }
    }

    /// <inheritdoc />
    public override VectorStoreCollection<object, Dictionary<string, object?>> GetDynamicCollection(
        string name,
        VectorStoreCollectionDefinition definition)
    {
        throw new NotSupportedException(
            "Dynamic collections are not supported. Use the typed GetCollection<TKey, TRecord>() instead.");
    }

    /// <inheritdoc />
    public override async Task<bool> CollectionExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(name);

        lock (gate)
        {
            if (collections.ContainsKey(name))
                return true;
        }

        var safe = sanitizeCollectionName(name);
        var recordsTable = safe + "_records";

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            cmd.Parameters.AddWithValue("@name", recordsTable);
            return (long?)await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override async Task EnsureCollectionDeletedAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(name);

        object? existing;
        lock (gate)
        {
            collections.TryRemove(name, out existing);
        }

        if (existing is IDisposable disposable)
            disposable.Dispose();

        var safe = sanitizeCollectionName(name);
        var recordsTable = safe + "_records";
        var ftsTable = safe + "_fts";

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DROP TABLE IF EXISTS \"{ftsTable}\"; DROP TABLE IF EXISTS \"{recordsTable}\";";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<string> ListCollectionNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT substr(name, 1, length(name) - 8) FROM sqlite_master WHERE type='table' AND name LIKE '%_records' AND name NOT LIKE 'sqlite_%';";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cn = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (!string.IsNullOrEmpty(cn))
                yield return cn;
        }
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(SqliteVectorStore) || serviceType == typeof(VectorStore))
            return this;

        return null;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (gate)
            {
                foreach (var value in collections.Values)
                    if (value is IDisposable disposable)
                        disposable.Dispose();

                collections.Clear();
            }
        }
    }
}
