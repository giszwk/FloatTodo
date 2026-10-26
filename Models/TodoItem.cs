using System;
using System.Globalization;

namespace FloatTodo.Models;

public enum RepeatKind { None, Daily, Weekly, Monthly }

public sealed class TodoItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public bool IsCompleted { get; init; }
    public required string CreatedUtc { get; init; }
    public string? CompletedUtc { get; init; }
    public string? DueLocal { get; init; }
    public bool ReminderEnabled { get; init; }
    public string? SeriesId { get; init; }

    public string ScheduleText
    {
        get
        {
            if (DueLocal is null) return string.Empty;
            if (!DateTime.TryParseExact(DueLocal, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var due)) return string.Empty;
            return $"{(SeriesId is null ? "🕒" : "🔁")} {due:MM-dd HH:mm}" +
                   (ReminderEnabled ? " · 提醒" : "");
        }
    }
}

public sealed class RecurringSchedule
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Pattern { get; init; }
    public required string StartLocal { get; init; }
    public required string NextLocal { get; init; }
    public bool ReminderEnabled { get; init; }
    public bool Enabled { get; init; }
    public string Description => $"{Pattern switch { "Daily" => "每天", "Weekly" => "每周", "Monthly" => "每月", _ => Pattern }} · " +
                                 (ReminderEnabled ? "到点提醒" : "只生成待办");
}

public sealed class DueReminder
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string DueLocal { get; init; }
}
