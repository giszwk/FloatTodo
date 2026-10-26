using System;
using System.IO;

namespace FloatTodo.Services;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatTodo");
    public static string Database => Path.Combine(DataDir, "todos.db");
    public static string Settings => Path.Combine(DataDir, "settings.json");

    public static void EnsureDirectory() => Directory.CreateDirectory(DataDir);
}
