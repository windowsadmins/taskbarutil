using ManagedUtilities;

namespace TaskbarUtil.Core;

/// <summary>
/// The process-wide file log. Resolved on first use, so read-only commands that
/// never log (list, find, show) do not create the log directory.
/// </summary>
public static class Log
{
    private static readonly Lazy<FileLog> Instance =
        new(() => FileLog.ForTool("taskbarutil", "TaskbarUtil"));

    public static FileLog File => Instance.Value;

    public static void Debug(string message) => File.Debug(message);

    public static void Info(string message) => File.Info(message);

    public static void Warn(string message) => File.Warn(message);

    public static void Error(string message) => File.Error(message);

    public static void Error(string message, Exception exception) => File.Error(message, exception);
}
