using System.Numerics;
using System.Text.Json;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;
using Guild.Persistence;
using Microsoft.Data.Sqlite;

namespace Guild.Domain.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "guild-persistence-" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(directory, "save.sqlite3");

    public PersistenceTests() => Directory.CreateDirectory(directory);

    private static ChangeSet Change(long revision, string id, string hash = "hash", string balance = "90", string eventId = "event")
        => new(revision, id, hash, "確定しました。", new Dictionary<string, string>
        {
            ["balance"] = balance,
            ["materials"] = "0",
            ["pending"] = "0"
        }, [new(eventId, "actor", "0", "Fusion", "before", "after", "素材とイベントを消費して合成しました。")]);

    private void Seed()
    {
        using var store = new SaveStore(DatabasePath);
        store.Commit(new(0, "seed", "seed-hash", "初期状態", new Dictionary<string, string>
        {
            ["balance"] = "100", ["materials"] = "2", ["pending"] = "1"
        }, [new("seed-event", "actor", "0", "Seed", "{}", "{}", "初期状態")]));
    }

    [Theory]
    [InlineData(CommitStage.AfterDocuments)]
    [InlineData(CommitStage.BeforeCommit)]
    public void ExceptionBeforeCommitRollsBackDocumentsConsumptionHistoryAndAcknowledgement(CommitStage failureStage)
    {
        Seed();
        using (var store = new SaveStore(DatabasePath, stage => { if (stage == failureStage) throw new IOException("simulated interruption"); }))
        {
            Assert.Throws<IOException>(() => store.Commit(Change(1, "spend")));
            Assert.Equal(1, store.Revision);
            Assert.Equal("100", store.ReadDocument("balance"));
            Assert.Equal("2", store.ReadDocument("materials"));
            Assert.Equal("1", store.ReadDocument("pending"));
            Assert.Single(store.ReadHistory());
            Assert.Null(store.FindCommand("spend", "hash"));
        }
        using var reopened = new SaveStore(DatabasePath);
        Assert.Equal(1, reopened.Revision);
        Assert.Equal("100", reopened.ReadDocument("balance"));
        Assert.Single(reopened.ReadHistory());
        Assert.Equal("ok", reopened.IntegrityCheck());
    }

    [Fact]
    public void ExceptionAfterCommitLeavesDurableResultAndRetryDoesNotApplyTwice()
    {
        Seed();
        var change = Change(1, "spend");
        using (var store = new SaveStore(DatabasePath, stage => { if (stage == CommitStage.AfterCommit) throw new IOException("response was lost"); }))
        {
            Assert.Throws<IOException>(() => store.Commit(change));
            Assert.Equal(2, store.Revision);
            Assert.Equal("90", store.ReadDocument("balance"));
            Assert.Equal("0", store.ReadDocument("materials"));
            Assert.Equal("0", store.ReadDocument("pending"));
            Assert.Equal(2, store.ReadHistory().Count);
            var retry = store.Commit(change);
            Assert.False(retry.Applied);
            Assert.Equal(2, retry.Revision);
        }
        using var reopened = new SaveStore(DatabasePath);
        Assert.False(reopened.Commit(change).Applied);
        Assert.Equal(2, reopened.ReadHistory().Count);
        Assert.Equal("90", reopened.ReadDocument("balance"));
        Assert.Equal("ok", reopened.IntegrityCheck());
    }

    [Fact]
    public void RepeatedCommandReturnsOriginalAcknowledgementAndDifferentHashIsRejected()
    {
        using var store = new SaveStore(DatabasePath);
        var original = Change(0, "same-command");
        Assert.True(store.Commit(original).Applied);
        // Retrying a known command is valid even when its expected revision is now stale.
        var repeated = store.Commit(original with { Documents = new Dictionary<string, string> { ["balance"] = "-100" } });
        Assert.False(repeated.Applied);
        Assert.Equal(1, repeated.Revision);
        Assert.Equal("90", store.ReadDocument("balance"));
        Assert.Single(store.ReadHistory());
        Assert.Throws<InvalidOperationException>(() => store.Commit(original with { CommandHash = "different-payload-hash" }));
        Assert.Throws<InvalidOperationException>(() => store.FindCommand("same-command", "different-payload-hash"));
        Assert.Equal(1, store.Revision);
    }

    [Fact]
    public void StaleWriterCannotOverwriteACommittedRevision()
    {
        using var first = new SaveStore(DatabasePath);
        using var second = new SaveStore(DatabasePath);
        var firstRevision = first.Revision;
        var secondRevision = second.Revision;
        first.Commit(Change(firstRevision, "first", balance: "80", eventId: "first-event"));
        Assert.Throws<InvalidOperationException>(() => second.Commit(Change(secondRevision, "second", balance: "70", eventId: "second-event")));
        Assert.Equal("80", second.ReadDocument("balance"));
        Assert.Null(second.FindCommand("second", "hash"));
        Assert.Single(second.ReadHistory());
        Assert.True(second.Commit(Change(second.Revision, "second", balance: "70", eventId: "second-event")).Applied);
        Assert.Equal("70", first.ReadDocument("balance"));
        Assert.Equal(2, first.Revision);
    }

    [Fact]
    public void HugeIntegerAndExactFractionDocumentsSurviveClosingAndReopening()
    {
        var huge = BigInteger.Pow(10, 150) + 7;
        var expected = new NumericDocument(huge, new Rational(-huge, 31));
        var json = JsonSerializer.Serialize(expected, DomainJson.Options);
        using (var store = new SaveStore(DatabasePath))
            store.Commit(new(0, "huge", "huge-hash", "保存", new Dictionary<string, string> { ["profile"] = json }, []));
        using var reopened = new SaveStore(DatabasePath);
        var restored = JsonSerializer.Deserialize<NumericDocument>(reopened.ReadDocument("profile")!, DomainJson.Options);
        Assert.Equal(expected, restored);
        Assert.Contains(huge.ToString(), reopened.ReadDocument("profile"));
        Assert.Equal("ok", reopened.IntegrityCheck());
    }

    [Fact]
    public void FutureSchemaIsRejectedWithoutChangingDatabaseOrCreatingCurrentTables()
    {
        using (var connection = OpenRaw(DatabasePath))
            Execute(connection, "CREATE TABLE future_data(value TEXT); INSERT INTO future_data(value) VALUES('preserve-me'); PRAGMA user_version=999;");
        var before = File.ReadAllBytes(DatabasePath);
        Assert.Throws<NotSupportedException>(() => new SaveStore(DatabasePath));
        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
        using var check = OpenRaw(DatabasePath);
        Assert.Equal(999L, Scalar(check, "PRAGMA user_version;"));
        Assert.Equal("preserve-me", Scalar(check, "SELECT value FROM future_data;"));
        Assert.Equal(0L, Scalar(check, "SELECT count(*) FROM sqlite_master WHERE name='documents';"));
    }

    [Fact]
    public void ExistingVersionZeroDatabaseIsBackedUpBeforeAdditiveMigration()
    {
        using (var connection = OpenRaw(DatabasePath))
            Execute(connection, "CREATE TABLE legacy_data(value TEXT); INSERT INTO legacy_data(value) VALUES('legacy-state'); PRAGMA user_version=0;");
        string backupPath;
        using (var store = new SaveStore(DatabasePath))
        {
            backupPath = Assert.IsType<string>(store.MigrationBackupPath);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(0, store.Revision);
            Assert.Equal("ok", store.IntegrityCheck());
        }
        using var original = OpenRaw(DatabasePath);
        using var backup = OpenRaw(backupPath);
        Assert.Equal(1L, Scalar(original, "PRAGMA user_version;"));
        Assert.Equal(0L, Scalar(backup, "PRAGMA user_version;"));
        Assert.Equal("legacy-state", Scalar(original, "SELECT value FROM legacy_data;"));
        Assert.Equal("legacy-state", Scalar(backup, "SELECT value FROM legacy_data;"));
        Assert.Equal(0L, Scalar(backup, "SELECT count(*) FROM sqlite_master WHERE name='commands';"));
    }

    private static SqliteConnection OpenRaw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar();
    }
    public sealed record NumericDocument(BigInteger Rank, Rational Modifier);
    public void Dispose() => Directory.Delete(directory, recursive: true);
}
