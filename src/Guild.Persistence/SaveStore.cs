using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Guild.Persistence;

public sealed record HistoryEntry(string EventId, string ActorId, string RankBlock, string Kind,
    string BeforeJson, string AfterJson, string Text);
public sealed record StoredHistory(long Sequence, string EventId, string ActorId, string Text);
public sealed record ChangeSet(long ExpectedRevision, string CommandId, string CommandHash,
    string ResultMessage, IReadOnlyDictionary<string, string> Documents, IReadOnlyList<HistoryEntry> History);
public sealed record CommitResult(bool Applied, long Revision, string Message);
public enum CommitStage { AfterDocuments, BeforeCommit, AfterCommit }

/// <summary>One local writer; state, consumption, history and idempotency acknowledgement share one transaction.</summary>
public sealed class SaveStore : IDisposable
{
    public const int SchemaVersion = 1;
    private readonly SqliteConnection connection;
    private readonly Action<CommitStage>? fault;
    private readonly object gate = new();
    public string Path { get; }
    public string? MigrationBackupPath { get; private set; }

    public SaveStore(string path, Action<CommitStage>? fault = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        this.fault = fault;
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        try
        {
            var version = Convert.ToInt32(Scalar("PRAGMA user_version;"), CultureInfo.InvariantCulture);
            if (version > SchemaVersion) throw new NotSupportedException($"保存形式 {version} はこのアプリでは読めません。データは変更していません。");
            if (version < SchemaVersion)
            {
                if (Convert.ToInt32(Scalar("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';")) > 0)
                {
                    MigrationBackupPath = Path + ".before-v1-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + ".backup";
                    using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = MigrationBackupPath, Pooling = false }.ToString());
                    destination.Open();
                    connection.BackupDatabase(destination);
                }
                using var tx = connection.BeginTransaction();
                ExecuteSql("CREATE TABLE IF NOT EXISTS documents (key TEXT PRIMARY KEY, value TEXT NOT NULL);", tx);
                ExecuteSql("CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);", tx);
                ExecuteSql("INSERT OR IGNORE INTO metadata(key,value) VALUES('revision','0');", tx);
                ExecuteSql("CREATE TABLE IF NOT EXISTS commands (command_id TEXT PRIMARY KEY, command_hash TEXT NOT NULL, message TEXT NOT NULL, revision INTEGER NOT NULL);", tx);
                ExecuteSql("CREATE TABLE IF NOT EXISTS history (sequence INTEGER PRIMARY KEY AUTOINCREMENT,event_id TEXT NOT NULL,actor_id TEXT NOT NULL,rank_block TEXT NOT NULL,kind TEXT NOT NULL,before_json TEXT NOT NULL,after_json TEXT NOT NULL,text TEXT NOT NULL,created_utc TEXT NOT NULL);", tx);
                ExecuteSql("CREATE INDEX IF NOT EXISTS history_actor ON history(actor_id,rank_block,sequence);", tx);
                ExecuteSql("PRAGMA user_version=1;", tx);
                tx.Commit();
            }
            ExecuteSql("PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        }
        catch { connection.Dispose(); throw; }
    }

    public long Revision { get { lock (gate) return long.Parse((string)Scalar("SELECT value FROM metadata WHERE key='revision';")!, CultureInfo.InvariantCulture); } }

    /// <summary>Protect logical JSON document migrations even when SQLite's table schema is unchanged.</summary>
    public string BackupBeforeDocumentMigration()
    {
        lock (gate)
        {
            var backup = Path + ".before-documents-v2-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N") + ".backup";
            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString());
            destination.Open();
            connection.BackupDatabase(destination);
            MigrationBackupPath = backup;
            return backup;
        }
    }

    public string? ReadDocument(string key)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM documents WHERE key=$key;";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
    }

    public CommitResult? FindCommand(string id, string hash)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT command_hash,message,revision FROM commands WHERE command_id=$id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            if (reader.GetString(0) != hash) throw new InvalidOperationException("同じ操作IDを異なる内容には使用できません。");
            return new CommitResult(false, reader.GetInt64(2), reader.GetString(1));
        }
    }

    public CommitResult Commit(ChangeSet change)
    {
        lock (gate)
        {
            var previous = FindCommand(change.CommandId, change.CommandHash);
            if (previous is not null) return previous;
            using var transaction = connection.BeginTransaction();
            var actualRevision = long.Parse((string)Scalar("SELECT value FROM metadata WHERE key='revision';", transaction)!, CultureInfo.InvariantCulture);
            if (actualRevision != change.ExpectedRevision) throw new InvalidOperationException("保存状態が更新されました。再読込して操作してください。");
            var revision = checked(actualRevision + 1);
            foreach (var document in change.Documents)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO documents(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
                command.Parameters.AddWithValue("$key", document.Key);
                command.Parameters.AddWithValue("$value", document.Value);
                command.ExecuteNonQuery();
            }
            fault?.Invoke(CommitStage.AfterDocuments);
            foreach (var entry in change.History)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO history(event_id,actor_id,rank_block,kind,before_json,after_json,text,created_utc) VALUES($event,$actor,$rank,$kind,$before,$after,$text,$time);";
                command.Parameters.AddWithValue("$event", entry.EventId);
                command.Parameters.AddWithValue("$actor", entry.ActorId);
                command.Parameters.AddWithValue("$rank", entry.RankBlock);
                command.Parameters.AddWithValue("$kind", entry.Kind);
                command.Parameters.AddWithValue("$before", entry.BeforeJson);
                command.Parameters.AddWithValue("$after", entry.AfterJson);
                command.Parameters.AddWithValue("$text", entry.Text);
                command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE metadata SET value=$revision WHERE key='revision'; INSERT INTO commands(command_id,command_hash,message,revision) VALUES($id,$hash,$message,$revision);";
                command.Parameters.AddWithValue("$revision", revision.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$id", change.CommandId);
                command.Parameters.AddWithValue("$hash", change.CommandHash);
                command.Parameters.AddWithValue("$message", change.ResultMessage);
                command.ExecuteNonQuery();
            }
            fault?.Invoke(CommitStage.BeforeCommit);
            transaction.Commit();
            fault?.Invoke(CommitStage.AfterCommit);
            return new CommitResult(true, revision, change.ResultMessage);
        }
    }

    public IReadOnlyList<StoredHistory> ReadHistory(long afterSequence = 0, int limit = 100)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT sequence,event_id,actor_id,text FROM history WHERE sequence>$after ORDER BY sequence LIMIT $limit;";
            command.Parameters.AddWithValue("$after", afterSequence);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var records = new List<StoredHistory>();
            while (reader.Read()) records.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return records;
        }
    }

    public string IntegrityCheck() { lock (gate) return (string)Scalar("PRAGMA integrity_check;")!; }
    private object? Scalar(string sql, SqliteTransaction? transaction = null) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return command.ExecuteScalar(); }
    private void ExecuteSql(string sql, SqliteTransaction? transaction = null) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }
    public void Dispose() => connection.Dispose();
}
