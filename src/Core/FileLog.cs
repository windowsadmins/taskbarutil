using System;
using System.IO;
using System.Text;

namespace ManagedUtilities;

/// <summary>
/// Severity of a <see cref="FileLog"/> entry. The upper-cased name is what lands in the file.
/// </summary>
public enum FileLogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Append-only, size-rolled text log for command-line and tray utilities that are
/// driven by scripts and packages rather than by a person watching a console.
///
/// This file is shared verbatim between several small utilities (copied, not
/// referenced), so it has no dependencies beyond the base class library and no
/// knowledge of the tool that hosts it.
///
/// Location: %ProgramData%\ManagedUtilities\logs\&lt;tool&gt;.log, resolved from
/// <see cref="Environment.SpecialFolder.CommonApplicationData"/> and created on demand.
/// When that directory cannot be written (a non-elevated user) the log falls back to
/// %LOCALAPPDATA%\&lt;Tool&gt;\logs\&lt;tool&gt;.log.
///
/// Format: one entry per line, "[yyyy-MM-dd HH:mm:ss] LEVEL message" in local time,
/// with the level padded to five characters (DEBUG, INFO, WARN, ERROR).
///
/// Rolling: once the file would exceed <see cref="MaxBytes"/> it is renamed to
/// &lt;tool&gt;.log.1 and the older generations shift up to &lt;tool&gt;.log.5; the
/// oldest is deleted. Newest is always .1.
///
/// Every public member is exception-safe. A log that cannot be written must never
/// change the behaviour or exit code of the tool that owns it.
/// </summary>
public sealed class FileLog
{
    public const long DefaultMaxBytes = 5L * 1024 * 1024;
    public const int DefaultGenerations = 5;
    public const string SharedDirectoryName = "ManagedUtilities";
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new object();

    /// <summary>
    /// Creates a log that appends to <paramref name="filePath"/>. Prefer
    /// <see cref="ForTool"/> in production code; this constructor exists so tests
    /// and callers with their own path policy can point the log anywhere.
    /// </summary>
    public FileLog(string filePath, long maxBytes = DefaultMaxBytes, int generations = DefaultGenerations)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A log file path is required.", nameof(filePath));

        FilePath = Path.GetFullPath(filePath);
        MaxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
        Generations = generations > 0 ? generations : DefaultGenerations;
    }

    /// <summary>Full path of the active log file.</summary>
    public string FilePath { get; }

    /// <summary>Directory that holds the log file and its rolled generations.</summary>
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;

    /// <summary>Size at which the active file is rolled.</summary>
    public long MaxBytes { get; }

    /// <summary>Number of rolled generations kept (.1 through .N).</summary>
    public int Generations { get; }

    /// <summary>Entries below this level are dropped. Defaults to everything.</summary>
    public FileLogLevel MinimumLevel { get; set; } = FileLogLevel.Debug;

    /// <summary>
    /// Resolves the conventional log for a tool: the shared ManagedUtilities
    /// directory when writable, otherwise the per-user fallback. Never throws.
    /// </summary>
    /// <param name="toolName">Lower-case file stem, e.g. "taskbarutil" gives taskbarutil.log.</param>
    /// <param name="displayName">Folder name used under %LOCALAPPDATA% for the fallback; defaults to <paramref name="toolName"/>.</param>
    public static FileLog ForTool(string toolName, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            throw new ArgumentException("A tool name is required.", nameof(toolName));

        var directory = ResolveDirectory(toolName, displayName ?? toolName);
        return new FileLog(Path.Combine(directory, toolName + ".log"));
    }

    /// <summary>
    /// Picks the directory the log should live in. The shared location wins when the
    /// tool can create it and append to its log file there; otherwise the per-user
    /// location is returned without checking it, so a later write failure is simply
    /// swallowed rather than surfaced.
    /// </summary>
    public static string ResolveDirectory(string toolName, string displayName)
    {
        var shared = SharedDirectory();
        if (shared != null && CanAppend(shared, toolName + ".log"))
            return shared;

        var local = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
            local = Path.GetTempPath();

        return Path.Combine(local, displayName, "logs");
    }

    /// <summary>The shared log directory, or null when the platform has no CommonApplicationData.</summary>
    public static string? SharedDirectory()
    {
        var common = SafeFolder(Environment.SpecialFolder.CommonApplicationData);
        return string.IsNullOrEmpty(common) ? null : Path.Combine(common, SharedDirectoryName, "logs");
    }

    public bool IsEnabled(FileLogLevel level) => level >= MinimumLevel;

    public void Debug(string message) => Write(FileLogLevel.Debug, message);

    public void Info(string message) => Write(FileLogLevel.Info, message);

    public void Warn(string message) => Write(FileLogLevel.Warn, message);

    public void Error(string message) => Write(FileLogLevel.Error, message);

    /// <summary>
    /// Writes an ERROR entry carrying the exception type and message, followed by a
    /// DEBUG entry with the full exception text for anyone who needs the stack.
    /// </summary>
    public void Error(string message, Exception exception)
    {
        if (exception == null)
        {
            Error(message);
            return;
        }

        Error($"{message}: {exception.GetType().Name}: {exception.Message}");
        Debug(exception.ToString());
    }

    /// <summary>Appends one entry. Swallows every failure.</summary>
    public void Write(FileLogLevel level, string message)
    {
        if (!IsEnabled(level))
            return;

        try
        {
            var bytes = Utf8NoBom.GetBytes(FormatLine(DateTime.Now, level, message) + Environment.NewLine);

            lock (_gate)
            {
                var directory = DirectoryPath;
                if (directory.Length > 0)
                    Directory.CreateDirectory(directory);

                RollIfNeeded(bytes.Length);

                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            // A log that cannot be written is not the tool's problem.
        }
    }

    /// <summary>
    /// Builds the exact line written for an entry, without the line terminator.
    /// Embedded line breaks in the message are folded so one entry stays one line.
    /// </summary>
    public static string FormatLine(DateTime timestamp, FileLogLevel level, string message)
    {
        var text = (message ?? string.Empty)
            .Replace("\r\n", " | ")
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        return $"[{timestamp.ToString(TimestampFormat)}] {LevelName(level),-5} {text}";
    }

    public static string LevelName(FileLogLevel level) => level switch
    {
        FileLogLevel.Debug => "DEBUG",
        FileLogLevel.Info => "INFO",
        FileLogLevel.Warn => "WARN",
        FileLogLevel.Error => "ERROR",
        _ => level.ToString().ToUpperInvariant()
    };

    /// <summary>Path of a rolled generation: 1 is the newest.</summary>
    public string GenerationPath(int generation) => FilePath + "." + generation;

    private void RollIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length == 0 || current.Length + incomingBytes <= MaxBytes)
            return;

        var oldest = GenerationPath(Generations);
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (var generation = Generations - 1; generation >= 1; generation--)
        {
            var from = GenerationPath(generation);
            if (File.Exists(from))
                File.Move(from, GenerationPath(generation + 1), overwrite: true);
        }

        File.Move(FilePath, GenerationPath(1), overwrite: true);
    }

    private static bool CanAppend(string directory, string fileName)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var probe = new FileStream(
                Path.Combine(directory, fileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch
        {
            return string.Empty;
        }
    }
}
