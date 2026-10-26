using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FloatTodo.Models;
using Microsoft.Data.Sqlite;

namespace FloatTodo.Services;

public sealed class TaskRepository
{
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
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS Tasks (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Title TEXT NOT NULL,
                    IsCompleted INTEGER NOT NULL DEFAULT 0,
                    CreatedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IX_Tasks_Status ON Tasks(IsCompleted);
                """;
            command.ExecuteNonQuery();
        }

        // v0.1 的数据库没有 SortOrder。升级时按旧的时间倒序生成初始顺序，
        // 不删表、不丢任务，且迁移的两步操作放在同一个事务里。
        bool hasSortOrder = false;
        using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(Tasks);";
            using var reader = inspect.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "SortOrder", StringComparison.OrdinalIgnoreCase))
                {
                    hasSortOrder = true;
                    break;
                }
            }
        }

        if (!hasSortOrder)
        {
            using var transaction = connection.BeginTransaction();
            using (var migrate = connection.CreateCommand())
            {
                migrate.Transaction = transaction;
                migrate.CommandText = """
                    ALTER TABLE Tasks ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;
                    WITH Ranked AS (
                        SELECT Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY IsCompleted
                                   ORDER BY COALESCE(CompletedUtc, CreatedUtc) DESC, Id ASC
                               ) - 1 AS Position
                        FROM Tasks
                    )
                    UPDATE Tasks
                    SET SortOrder = (SELECT Position FROM Ranked WHERE Ranked.Id = Tasks.Id);
                    """;
                migrate.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        using (var index = connection.CreateCommand())
        {
            index.CommandText = "CREATE INDEX IF NOT EXISTS IX_Tasks_Order ON Tasks(IsCompleted, SortOrder);";
            index.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public List<TodoItem> GetAll()
    {
        var items = new List<TodoItem>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, IsCompleted, CreatedUtc, CompletedUtc
            FROM Tasks
            ORDER BY IsCompleted ASC, SortOrder ASC,
                     COALESCE(CompletedUtc, CreatedUtc) DESC, Id ASC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new TodoItem
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                IsCompleted = reader.GetInt64(2) != 0,
                CreatedUtc = reader.GetString(3),
                CompletedUtc = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }
        return items;
    }

    public int GetPendingCount()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tasks WHERE IsCompleted = 0;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Add(string title)
    {
        title = NormalizeTitle(title);
        using var connection = Open();
        using var command = connection.CreateCommand();
        // 新任务放到待办列表顶部，不影响用户手动排列的旧任务。
        command.CommandText = """
            INSERT INTO Tasks (Id, Title, IsCompleted, CreatedUtc, SortOrder)
            VALUES ($id, $title, 0, $utc,
                    COALESCE((SELECT MIN(SortOrder) - 1 FROM Tasks WHERE IsCompleted = 0), 0));
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void Rename(string id, string title)
    {
        title = NormalizeTitle(title);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Tasks SET Title = $title WHERE Id = $id;";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetCompleted(string id, bool completed)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // 改变状态的任务自动放到目标列表顶部，两侧的原有排序保持不变。
        command.CommandText = """
            UPDATE Tasks
            SET IsCompleted = $done,
                CompletedUtc = $completedUtc,
                SortOrder = COALESCE((
                    SELECT MIN(SortOrder) - 1 FROM Tasks
                    WHERE IsCompleted = $done AND Id <> $id
                ), 0)
            WHERE Id = $id AND IsCompleted <> $done;
            """;
        command.Parameters.AddWithValue("$done", completed ? 1 : 0);
        command.Parameters.AddWithValue("$completedUtc",
            completed ? DateTimeOffset.UtcNow.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 将某任务插到同组目标任务之前/之后。只在同一状态列表内移动，
    /// 一个事务中重排整组的 SortOrder，避免出现重复或半完成的排序。
    /// </summary>
    public bool ReorderWithinGroup(string draggedId, string targetId, bool insertAfter)
    {
        if (draggedId == targetId) return false;

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var rows = new List<(string Id, bool Completed)>();
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT Id, IsCompleted FROM Tasks
                ORDER BY IsCompleted ASC, SortOrder ASC,
                         COALESCE(CompletedUtc, CreatedUtc) DESC, Id ASC;
                """;
            using var reader = query.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetInt64(1) != 0));
        }

        int from = rows.FindIndex(row => row.Id == draggedId);
        int target = rows.FindIndex(row => row.Id == targetId);
        if (from < 0 || target < 0)
            throw new InvalidOperationException("任务已发生变化，请重新拖动。");
        if (rows[from].Completed != rows[target].Completed)
            throw new InvalidOperationException("待办和已完成事项需分别排序。");

        var ids = rows.Where(row => row.Completed == rows[from].Completed)
                      .Select(row => row.Id).ToList();
        int oldPosition = ids.IndexOf(draggedId);
        ids.RemoveAt(oldPosition);
        int newPosition = ids.IndexOf(targetId) + (insertAfter ? 1 : 0);
        ids.Insert(newPosition, draggedId);
        if (oldPosition == newPosition) return false;

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Tasks SET SortOrder = $order WHERE Id = $id;";
            var orderParameter = update.Parameters.Add("$order", SqliteType.Integer);
            var idParameter = update.Parameters.Add("$id", SqliteType.Text);
            for (int i = 0; i < ids.Count; i++)
            {
                orderParameter.Value = i;
                idParameter.Value = ids[i];
                update.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        return true;
    }

    public void Delete(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Tasks WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title.Trim();
        if (normalized.Length is < 1 or > 200)
            throw new ArgumentException("任务内容应为 1 至 200 个字符。", nameof(title));
        return normalized;
    }
}
