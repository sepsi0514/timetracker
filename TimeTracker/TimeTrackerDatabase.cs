using Microsoft.Data.Sqlite;

namespace TimeTracker;

public sealed class TimeTrackerDatabase
{
    private readonly string _connectionString;

    public TimeTrackerDatabase(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS WorkItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE
            );
            CREATE TABLE IF NOT EXISTS WorkSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WorkItemId INTEGER NOT NULL REFERENCES WorkItems(Id),
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                DurationMinutes INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkSessions_OneActive
                ON WorkSessions((1)) WHERE EndedAt IS NULL;
            """;
        command.ExecuteNonQuery();
    }

    public List<WorkItem> GetWorkItems()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Id, w.Name, COALESCE(SUM(s.DurationMinutes), 0)
            FROM WorkItems AS w
            LEFT JOIN WorkSessions AS s ON s.WorkItemId = w.Id
            GROUP BY w.Id, w.Name
            ORDER BY w.Name COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<WorkItem>();
        while (reader.Read())
        {
            items.Add(new WorkItem(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return items;
    }

    public void AddWorkItem(string name)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO WorkItems (Name) VALUES ($name);";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.ExecuteNonQuery();
    }

    public void DeleteWorkItem(long workItemId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var activeSessionCommand = connection.CreateCommand())
        {
            activeSessionCommand.Transaction = transaction;
            activeSessionCommand.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM WorkSessions
                    WHERE WorkItemId = $workItemId AND EndedAt IS NULL
                );
                """;
            activeSessionCommand.Parameters.AddWithValue("$workItemId", workItemId);
            if (Convert.ToInt64(activeSessionCommand.ExecuteScalar()) != 0)
            {
                throw new InvalidOperationException("Stop the active session before deleting this item.");
            }
        }

        using (var deleteSessionsCommand = connection.CreateCommand())
        {
            deleteSessionsCommand.Transaction = transaction;
            deleteSessionsCommand.CommandText = "DELETE FROM WorkSessions WHERE WorkItemId = $workItemId;";
            deleteSessionsCommand.Parameters.AddWithValue("$workItemId", workItemId);
            deleteSessionsCommand.ExecuteNonQuery();
        }

        using (var deleteItemCommand = connection.CreateCommand())
        {
            deleteItemCommand.Transaction = transaction;
            deleteItemCommand.CommandText = "DELETE FROM WorkItems WHERE Id = $workItemId;";
            deleteItemCommand.Parameters.AddWithValue("$workItemId", workItemId);
            deleteItemCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public ActiveSession? GetActiveSession()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, w.Id, w.Name, s.StartedAt
            FROM WorkSessions AS s
            JOIN WorkItems AS w ON w.Id = s.WorkItemId
            WHERE s.EndedAt IS NULL
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ActiveSession(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)))
            : null;
    }

    public ActiveSession StartSession(WorkItem item)
    {
        var startedAt = DateTimeOffset.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO WorkSessions (WorkItemId, StartedAt)
            VALUES ($workItemId, $startedAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$workItemId", item.Id);
        command.Parameters.AddWithValue("$startedAt", startedAt.ToString("O"));
        var sessionId = (long)command.ExecuteScalar()!;
        return new ActiveSession(sessionId, item.Id, item.Name, startedAt);
    }

    public long StopSession(ActiveSession session)
    {
        var endedAt = DateTimeOffset.UtcNow;
        var durationMinutes = Math.Max(1, (long)Math.Ceiling((endedAt - session.StartedAt).TotalMinutes));

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkSessions
            SET EndedAt = $endedAt, DurationMinutes = $durationMinutes
            WHERE Id = $id AND EndedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$endedAt", endedAt.ToString("O"));
        command.Parameters.AddWithValue("$durationMinutes", durationMinutes);
        command.Parameters.AddWithValue("$id", session.Id);

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("This session has already been stopped.");
        }

        return durationMinutes;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }
}

public sealed record WorkItem(long Id, string Name, long TotalMinutes)
{
    public string DisplayMinutes => $"{TotalMinutes} min";
}

public sealed record ActiveSession(long Id, long WorkItemId, string WorkItemName, DateTimeOffset StartedAt);