using Explorer.Indexing.Index;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Explorer.Indexing.Persistence;

/// <summary>
/// 인메모리 인덱스의 SQLite 스냅샷 (빠른 콜드 스타트용 — 진실의 원천은 인메모리).
/// 저장은 전체 재작성 단일 트랜잭션, 단일 writer 전제. WAL 모드로 읽기와 경합하지 않는다.
/// </summary>
public sealed class SqliteIndexSnapshot
{
    private const int SchemaVersion = 1;

    private readonly string _dbPath;
    private readonly ILogger<SqliteIndexSnapshot> _logger;

    public SqliteIndexSnapshot(string dbPath, ILogger<SqliteIndexSnapshot> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentNullException.ThrowIfNull(logger);
        _dbPath = dbPath;
        _logger = logger;
    }

    /// <summary>인덱스 전체를 스냅샷으로 저장한다. 실패해도 예외를 던지지 않는다(다음 주기에 재시도).</summary>
    public bool TrySave(FileIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        try
        {
            var directory = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = Open();
            EnsureSchema(connection);

            using var transaction = connection.BeginTransaction();
            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM nodes;";
                clear.ExecuteNonQuery();
            }

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO nodes(id, parent_id, name, size, mtime, is_dir) VALUES ($id, $parent, $name, $size, $mtime, $isdir);";
            var pId = insert.Parameters.Add("$id", SqliteType.Integer);
            var pParent = insert.Parameters.Add("$parent", SqliteType.Integer);
            var pName = insert.Parameters.Add("$name", SqliteType.Text);
            var pSize = insert.Parameters.Add("$size", SqliteType.Integer);
            var pMtime = insert.Parameters.Add("$mtime", SqliteType.Integer);
            var pIsDir = insert.Parameters.Add("$isdir", SqliteType.Integer);

            var count = 0;
            index.ExportNodes((id, parentId, name, size, mtime, isDir) =>
            {
                pId.Value = id;
                pParent.Value = parentId;
                pName.Value = name;
                pSize.Value = size;
                pMtime.Value = mtime;
                pIsDir.Value = isDir ? 1 : 0;
                insert.ExecuteNonQuery();
                count++;
            });

            transaction.Commit();
            _logger.LogDebug("인덱스 스냅샷 저장: {Count}개 노드 → {Path}", count, _dbPath);
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(ex, "인덱스 스냅샷 저장 실패: {Path}", _dbPath);
            return false;
        }
    }

    /// <summary>스냅샷에서 새 인덱스를 복원한다. 없거나 손상이면 null (호출자는 빈 인덱스로 시작).</summary>
    public FileIndex? TryLoad()
    {
        if (!File.Exists(_dbPath))
        {
            return null;
        }

        try
        {
            using var connection = Open();
            if (ReadSchemaVersion(connection) != SchemaVersion)
            {
                _logger.LogInformation("인덱스 스냅샷 스키마 불일치 — 무시하고 재구축: {Path}", _dbPath);
                return null;
            }

            var index = new FileIndex();
            using var select = connection.CreateCommand();
            select.CommandText = "SELECT id, parent_id, name, size, mtime, is_dir FROM nodes ORDER BY id;";
            using var reader = select.ExecuteReader();
            var count = 0;
            while (reader.Read())
            {
                index.ImportNode(
                    originalId: reader.GetInt32(0),
                    parentId: reader.GetInt32(1),
                    name: reader.GetString(2),
                    size: reader.GetInt64(3),
                    modifiedTicks: reader.GetInt64(4),
                    isDirectory: reader.GetInt32(5) != 0);
                count++;
            }

            _logger.LogDebug("인덱스 스냅샷 로드: {Count}개 노드 ← {Path}", count, _dbPath);
            return index;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "인덱스 스냅샷 로드 실패 — 재구축한다: {Path}", _dbPath);
            return null;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS nodes(
                id INTEGER PRIMARY KEY,
                parent_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                size INTEGER NOT NULL,
                mtime INTEGER NOT NULL,
                is_dir INTEGER NOT NULL);
            INSERT OR REPLACE INTO meta(key, value) VALUES('schema_version', '{SchemaVersion}');
            """;
        command.ExecuteNonQuery();
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version';";
            return command.ExecuteScalar() is string value && int.TryParse(value, out var version) ? version : -1;
        }
        catch (SqliteException)
        {
            return -1;
        }
    }
}
