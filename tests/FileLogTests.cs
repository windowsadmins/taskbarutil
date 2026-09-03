using System.Text.RegularExpressions;
using ManagedUtilities;
using System.Text.Json;
using Xunit;

namespace TaskbarUtil.Tests;

/// <summary>
/// Pins down the two things scripts and log readers depend on: the exact line
/// format, and the size-rolled generation scheme.
/// </summary>
public class FileLogTests
{
    static readonly Regex LinePattern = new(
        @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] (DEBUG|INFO |WARN |ERROR) .*$");

    static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "taskbarutil-filelog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FormatLine_UsesBracketedLocalTimestampAndFiveCharLevel()
    {
        var stamp = new DateTime(2026, 9, 1, 14, 5, 9);

        Assert.Equal("[2026-09-01 14:05:09] DEBUG probing", FileLog.FormatLine(stamp, FileLogLevel.Debug, "probing"));
        Assert.Equal("[2026-09-01 14:05:09] INFO  pinned 'Edge'", FileLog.FormatLine(stamp, FileLogLevel.Info, "pinned 'Edge'"));
        Assert.Equal("[2026-09-01 14:05:09] WARN  slow", FileLog.FormatLine(stamp, FileLogLevel.Warn, "slow"));
        Assert.Equal("[2026-09-01 14:05:09] ERROR failed", FileLog.FormatLine(stamp, FileLogLevel.Error, "failed"));
    }

    [Fact]
    public void FormatLine_FoldsEmbeddedLineBreaksSoOneEntryStaysOneLine()
    {
        var stamp = new DateTime(2026, 9, 1, 0, 0, 0);

        var line = FileLog.FormatLine(stamp, FileLogLevel.Error, "first\r\nsecond\nthird");

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal("[2026-09-01 00:00:00] ERROR first | second third", line);
    }

    [Fact]
    public void Write_AppendsOneFormattedLinePerEntry()
    {
        var log = new FileLog(Path.Combine(NewTempDir(), "taskbarutil.log"));

        log.Info("first");
        log.Error("second");
        log.Warn("third");

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.Matches(LinePattern, l));
        Assert.EndsWith("INFO  first", lines[0]);
        Assert.EndsWith("ERROR second", lines[1]);
        Assert.EndsWith("WARN  third", lines[2]);
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        var path = Path.Combine(NewTempDir(), "nested", "deeper", "taskbarutil.log");
        var log = new FileLog(path);

        log.Info("hello");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Write_DropsEntriesBelowMinimumLevel()
    {
        var log = new FileLog(Path.Combine(NewTempDir(), "taskbarutil.log"))
        {
            MinimumLevel = FileLogLevel.Warn
        };

        log.Debug("hidden");
        log.Info("hidden");
        log.Warn("shown");
        log.Error("shown");

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.EndsWith("shown", l));
    }

    [Fact]
    public void Write_RollsAtMaxBytesKeepingFiveGenerationsNewestFirst()
    {
        // Each entry is 37 characters plus the line terminator, so a 200-byte cap
        // holds five entries per file. Sixty entries force eleven rolls; only the
        // five newest generations may survive.
        var log = new FileLog(Path.Combine(NewTempDir(), "taskbarutil.log"), maxBytes: 200, generations: 5);

        for (var i = 0; i < 60; i++)
            log.Info($"entry {i:D3}");

        Assert.True(File.Exists(log.FilePath));
        for (var generation = 1; generation <= 5; generation++)
            Assert.True(File.Exists(log.GenerationPath(generation)), $"generation .{generation} is missing");
        Assert.False(File.Exists(log.GenerationPath(6)), "a sixth generation was kept");

        static int LastEntry(string path) => int.Parse(File.ReadLines(path).Last().Split(' ').Last());
        static int FirstEntry(string path) => int.Parse(File.ReadLines(path).First().Split(' ').Last());

        // The active file continues where .1 left off; .1 is newer than .2, and so on.
        Assert.True(FirstEntry(log.FilePath) > LastEntry(log.GenerationPath(1)));
        for (var generation = 1; generation < 5; generation++)
            Assert.True(
                LastEntry(log.GenerationPath(generation)) > LastEntry(log.GenerationPath(generation + 1)),
                $"generation .{generation} is not newer than .{generation + 1}");

        // Nothing exceeds the cap, and the very newest entry is in the active file.
        Assert.True(new FileInfo(log.FilePath).Length <= 200);
        for (var generation = 1; generation <= 5; generation++)
            Assert.True(new FileInfo(log.GenerationPath(generation)).Length <= 200);
        Assert.Equal(59, LastEntry(log.FilePath));
    }

    [Fact]
    public void Write_ToAnUnwritablePathNeverThrows()
    {
        // A file where the log expects a directory makes CreateDirectory fail.
        var blocker = Path.Combine(NewTempDir(), "not-a-directory");
        File.WriteAllText(blocker, "occupied");
        var log = new FileLog(Path.Combine(blocker, "taskbarutil.log"));

        var exception = Record.Exception(() => log.Error("this must be swallowed"));

        Assert.Null(exception);
    }

    [Fact]
    public void EveryEntryIsAlsoWrittenToTheEventStream()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskbarutil-events-" + Guid.NewGuid().ToString("n"));
        try
        {
            var day = DateTime.Now.ToString(FileLog.DayFormat);
            var log = FileLog.ForTool("taskbarutil", Path.Combine(root, "TaskbarUtil"));
            // ForTool resolves the real shared or per-user location, so assert the shape
            // rather than the root: the entries land in a day directory beside events.jsonl.
            Assert.Equal(day, Path.GetFileName(Path.GetDirectoryName(log.FilePath)));
            Assert.Equal("taskbarutil.log", Path.GetFileName(log.FilePath));

            log.Info("pinned 'Edge'");
            var events = Path.Combine(log.DirectoryPath, FileLog.EventsFileName);
            Assert.True(File.Exists(events));
            var last = File.ReadAllLines(events)[^1];
            using var document = JsonDocument.Parse(last);
            Assert.Equal("INFO", document.RootElement.GetProperty("level").GetString());
            Assert.Equal("taskbarutil", document.RootElement.GetProperty("tool").GetString());
            Assert.Equal("pinned 'Edge'", document.RootElement.GetProperty("message").GetString());
            Assert.Equal(Environment.ProcessId.ToString(), document.RootElement.GetProperty("pid").GetString());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ResolvePathIsDayNested()
    {
        var stamp = new DateTime(2026, 9, 3, 4, 11, 7);
        var path = FileLog.ResolvePath("taskbarutil", "TaskbarUtil", stamp);
        Assert.Equal("taskbarutil.log", Path.GetFileName(path));
        Assert.Equal("2026-09-03", Path.GetFileName(Path.GetDirectoryName(path)));
    }
}
