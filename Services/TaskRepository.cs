using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloatTodo.Models;
using Microsoft.Data.Sqlite;

namespace FloatTodo.Services;

/// <summary>兼容 v0.1 / v0.1.1 的就地升级；所有日期计划以本机当地墙上时间存储。</summary>
public sealed class TaskRepository
{
    private const string LocalFormat = "yyyy-MM-ddTHH:mm:ss";
    private readonly string _connectionString;

    public TaskRepository()
    {
        AppPaths.EnsureDirectory();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.Database,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        using var connection = Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS Tasks (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Title TEXT NOT NULL,
                    IsCompleted INTEGER NOT NULL DEFAULT 0,
                    CreatedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    DueLocal TEXT NULL,
                    ReminderEnabled INTEGER NOT NULL DEFAULT 0,
                    SeriesId TEXT NULL,
                    AlertedUtc TEXT NULL,
                    SnoozedUntilLocal TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS RecurringSchedules (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Title TEXT NOT NULL,
                    Pattern TEXT NOT NULL,
                    StartLocal TEXT NOT NULL,
                    NextLocal TEXT NOT NULL,
                    ReminderEnabled INTEGER NOT NULL,
                    Enabled INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS IX_Tasks_Status ON Tasks(IsCompleted);
                """;
            create.ExecuteNonQuery();
        }
        // 先迁移旧排序列，再迁移提醒列；SQLite ADD COLUMN 不删除现有记录。
        if (!HasColumn(connection, "Tasks", "SortOrder"))
        {
            using var tx = connection.BeginTransaction();
            using var migrate = connection.CreateCommand();
            migrate.Transaction = tx;
            migrate.CommandText = """
                ALTER TABLE Tasks ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;
                WITH Ranked AS (
                  SELECT Id, ROW_NUMBER() OVER (
                    PARTITION BY IsCompleted
                    ORDER BY COALESCE(CompletedUtc, CreatedUtc) DESC, Id ASC
                  ) - 1 AS Position FROM Tasks
                )
                UPDATE Tasks SET SortOrder = (SELECT Position FROM Ranked WHERE Ranked.Id = Tasks.Id);
                """;
            migrate.ExecuteNonQuery();
            tx.Commit();
        }
        EnsureColumn(connection, "DueLocal", "TEXT NULL");
        EnsureColumn(connection, "ReminderEnabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SeriesId", "TEXT NULL");
        EnsureColumn(connection, "AlertedUtc", "TEXT NULL");
        EnsureColumn(connection, "SnoozedUntilLocal", "TEXT NULL");
        using var indexes = connection.CreateCommand();
        indexes.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_Tasks_Order ON Tasks(IsCompleted, SortOrder);
            CREATE INDEX IF NOT EXISTS IX_Tasks_Alert ON Tasks(IsCompleted, ReminderEnabled, DueLocal);
            CREATE INDEX IF NOT EXISTS IX_Schedules_Next ON RecurringSchedules(Enabled, NextLocal);
            """;
        indexes.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void EnsureColumn(SqliteConnection connection, string column, string definition)
    {
        if (HasColumn(connection, "Tasks", column)) return;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE Tasks ADD COLUMN {column} {definition};";
        cmd.ExecuteNonQuery();
    }

    private static string Local(DateTime value) => value.ToString(LocalFormat, CultureInfo.InvariantCulture);
    private static DateTime ParseLocal(string value) => DateTime.ParseExact(value, LocalFormat,
        CultureInfo.InvariantCulture, DateTimeStyles.None);

    public List<TodoItem> GetAll()
    {
        var items = new List<TodoItem>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, IsCompleted, CreatedUtc, CompletedUtc, DueLocal, ReminderEnabled, SeriesId
            FROM Tasks ORDER BY IsCompleted ASC, SortOrder ASC,
            COALESCE(CompletedUtc, CreatedUtc) DESC, Id ASC;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new TodoItem
            {
                Id = r.GetString(0), Title = r.GetString(1), IsCompleted = r.GetInt64(2) != 0,
                CreatedUtc = r.GetString(3), CompletedUtc = r.IsDBNull(4) ? null : r.GetString(4),
                DueLocal = r.IsDBNull(5) ? null : r.GetString(5),
                ReminderEnabled = r.GetInt64(6) != 0, SeriesId = r.IsDBNull(7) ? null : r.GetString(7)
            });
        return items;
    }

    public int GetPendingCount()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Tasks WHERE IsCompleted = 0;";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void InsertTask(SqliteConnection c, SqliteTransaction? tx, string title,
        string? due = null, bool reminder = false, string? seriesId = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO Tasks(Id, Title, IsCompleted, CreatedUtc, SortOrder, DueLocal, ReminderEnabled, SeriesId)
            VALUES($id, $title, 0, $utc,
                   COALESCE((SELECT MIN(SortOrder)-1 FROM Tasks WHERE IsCompleted=0),0),
                   $due, $reminder, $series);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$due", (object?)due ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reminder", reminder ? 1 : 0);
        cmd.Parameters.AddWithValue("$series", (object?)seriesId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Add(string title)
    {
        using var c = Open();
        InsertTask(c, null, NormalizeTitle(title));
    }

    public void CreatePlannedTask(string title, DateTime startLocal, RepeatKind repeat, bool reminder)
    {
        title = NormalizeTitle(title);
        if (startLocal < DateTime.Now.AddMinutes(-1))
            throw new ArgumentException("计划时间不能早于当前时间。");
        var first = Local(startLocal);
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (repeat == RepeatKind.None)
            InsertTask(c, tx, title, first, reminder);
        else
        {
            var seriesId = Guid.NewGuid().ToString("N");
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO RecurringSchedules(Id, Title, Pattern, StartLocal, NextLocal, ReminderEnabled, Enabled)
                VALUES($id, $title, $pattern, $start, $next, $remind, 1);
                """;
            cmd.Parameters.AddWithValue("$id", seriesId);
            cmd.Parameters.AddWithValue("$title", title);
            cmd.Parameters.AddWithValue("$pattern", repeat.ToString());
            cmd.Parameters.AddWithValue("$start", first);
            cmd.Parameters.AddWithValue("$next", Local(NextOccurrence(startLocal, startLocal, repeat)));
            cmd.Parameters.AddWithValue("$remind", reminder ? 1 : 0);
            cmd.ExecuteNonQuery();
            InsertTask(c, tx, title, first, reminder, seriesId);
        }
        tx.Commit();
    }

    public List<RecurringSchedule> GetSchedules()
    {
        var list = new List<RecurringSchedule>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, Pattern, StartLocal, NextLocal, ReminderEnabled, Enabled
            FROM RecurringSchedules WHERE Enabled=1 ORDER BY NextLocal ASC;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RecurringSchedule
            {
                Id = r.GetString(0), Title = r.GetString(1), Pattern = r.GetString(2),
                StartLocal = r.GetString(3), NextLocal = r.GetString(4),
                ReminderEnabled = r.GetInt64(5) != 0, Enabled = r.GetInt64(6) != 0
            });
        return list;
    }

    public void StopSchedule(string seriesId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE RecurringSchedules SET Enabled=0 WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", seriesId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>本机长时间关闭时只补建最近一次应生成的任务，不积压几百条。</summary>
    public bool AdvanceSchedules(DateTime nowLocal)
    {
        var schedules = GetSchedules().Where(s => ParseLocal(s.NextLocal) <= nowLocal).ToList();
        if (schedules.Count == 0) return false;
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var s in schedules)
        {
            if (!Enum.TryParse<RepeatKind>(s.Pattern, out var kind) || kind == RepeatKind.None)
                continue;
            var next = ParseLocal(s.NextLocal);
            var start = ParseLocal(s.StartLocal);
            DateTime latest = next;
            int safety = 0;
            while (next <= nowLocal && safety++ < 100000)
            {
                latest = next;
                next = NextOccurrence(next, start, kind);
            }
            if (next <= nowLocal) continue;
            // 按 (SeriesId, DueLocal) 幂等生成：即使重复调度也不会复制相同任务。
            using (var exists = c.CreateCommand())
            {
                exists.Transaction = tx;
                exists.CommandText = "SELECT COUNT(*) FROM Tasks WHERE SeriesId=$id AND DueLocal=$due;";
                exists.Parameters.AddWithValue("$id", s.Id);
                exists.Parameters.AddWithValue("$due", Local(latest));
                if (Convert.ToInt32(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                    InsertTask(c, tx, s.Title, Local(latest), s.ReminderEnabled, s.Id);
            }
            using var update = c.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE RecurringSchedules SET NextLocal=$next WHERE Id=$id AND Enabled=1;";
            update.Parameters.AddWithValue("$next", Local(next));
            update.Parameters.AddWithValue("$id", s.Id);
            update.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    private static DateTime NextOccurrence(DateTime previous, DateTime anchor, RepeatKind kind)
    {
        return kind switch
        {
            RepeatKind.Daily => previous.AddDays(1),
            RepeatKind.Weekly => previous.AddDays(7),
            RepeatKind.Monthly => NextMonthly(previous, anchor),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static DateTime NextMonthly(DateTime previous, DateTime anchor)
    {
        var month = new DateTime(previous.Year, previous.Month, 1).AddMonths(1);
        int day = Math.Min(anchor.Day, DateTime.DaysInMonth(month.Year, month.Month));
        return new DateTime(month.Year, month.Month, day, anchor.Hour, anchor.Minute, anchor.Second);
    }

    public List<DueReminder> GetDueReminders(DateTime nowLocal)
    {
        var list = new List<DueReminder>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Title, DueLocal FROM Tasks
            WHERE IsCompleted=0 AND ReminderEnabled=1 AND DueLocal IS NOT NULL
              AND ((SnoozedUntilLocal IS NOT NULL AND SnoozedUntilLocal <= $now)
                OR (SnoozedUntilLocal IS NULL AND AlertedUtc IS NULL AND DueLocal <= $now))
            ORDER BY DueLocal DESC LIMIT 200;
            """;
        cmd.Parameters.AddWithValue("$now", Local(nowLocal));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new DueReminder { Id = r.GetString(0), Title = r.GetString(1), DueLocal = r.GetString(2) });
        return list;
    }

    public void MarkAlerted(IEnumerable<string> ids)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Tasks SET AlertedUtc=$now, SnoozedUntilLocal=NULL WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        var idParam = cmd.Parameters.Add("$id", SqliteType.Text);
        foreach (var id in ids)
        {
            idParam.Value = id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Snooze(string id, DateTime untilLocal)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE Tasks SET SnoozedUntilLocal=$until WHERE Id=$id AND IsCompleted=0;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$until", Local(untilLocal));
        cmd.ExecuteNonQuery();
    }

    public void Rename(string id, string title)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Tasks SET Title=$title WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$title", NormalizeTitle(title));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetCompleted(string id, bool completed)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE Tasks SET IsCompleted=$done, CompletedUtc=$time,
            SnoozedUntilLocal=NULL,
            SortOrder=COALESCE((SELECT MIN(SortOrder)-1 FROM Tasks
                WHERE IsCompleted=$done AND Id<>$id),0)
            WHERE Id=$id AND IsCompleted<>$done;
            """;
        cmd.Parameters.AddWithValue("$done", completed ? 1 : 0);
        cmd.Parameters.AddWithValue("$time", completed ? DateTimeOffset.UtcNow.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>插入位置是原列表的边界 0..Count，移除拖动项后自动校正下标。</summary>
    public bool ReorderAtIndex(string draggedId, bool completed, int insertionIndex)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var ids = new List<string>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = """
                SELECT Id FROM Tasks WHERE IsCompleted=$done
                ORDER BY SortOrder ASC, COALESCE(CompletedUtc,CreatedUtc) DESC, Id ASC;
                """;
            q.Parameters.AddWithValue("$done", completed ? 1 : 0);
            using var reader = q.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetString(0));
        }
        var oldIndex = ids.IndexOf(draggedId);
        if (oldIndex < 0) return false;
        insertionIndex = Math.Clamp(insertionIndex, 0, ids.Count);
        var target = insertionIndex > oldIndex ? insertionIndex - 1 : insertionIndex;
        if (target == oldIndex) return false;
        ids.RemoveAt(oldIndex);
        ids.Insert(target, draggedId);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE Tasks SET SortOrder=$order WHERE Id=$id;";
        var order = cmd.Parameters.Add("$order", SqliteType.Integer);
        var id = cmd.Parameters.Add("$id", SqliteType.Text);
        for (int i = 0; i < ids.Count; i++)
        {
            order.Value = i; id.Value = ids[i]; cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    public void Delete(string id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM Tasks WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title.Trim();
        if (normalized.Length is < 1 or > 200)
            throw new ArgumentException("任务内容应为 1 至 200 个字符。", nameof(title));
        return normalized;
    }
}
