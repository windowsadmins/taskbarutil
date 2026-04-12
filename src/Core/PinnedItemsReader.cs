using System.Runtime.InteropServices;

namespace TaskbarUtil.Core;

public static class PinnedItemsReader
{
    public record PinnedItem(string DisplayName, string? TargetPath, string LinkFilePath);

    public static List<PinnedItem> GetCurrentPins()
    {
        var items = new List<PinnedItem>();
        var dir = EnvironmentInfo.PinnedItemsDirectory;

        if (!Directory.Exists(dir))
            return items;

        foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk"))
        {
            var name = Path.GetFileNameWithoutExtension(lnk);
            var target = ResolveShortcutTarget(lnk);
            items.Add(new PinnedItem(name, target, lnk));
        }

        return items.OrderBy(i => i.DisplayName).ToList();
    }

    static string? ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }
}
