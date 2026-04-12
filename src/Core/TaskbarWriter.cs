using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarUtil.Core;

/// <summary>
/// Directly manipulates the User Pinned\TaskBar shortcuts and Taskband registry
/// to force an immediate taskbar update without requiring sign-out.
/// </summary>
public static class TaskbarWriter
{
    const string TaskbandKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    static string BackupDirectory =>
        Path.Combine(EnvironmentInfo.ConfigDirectory, "backup");

    static string BackupShortcutsDir =>
        Path.Combine(BackupDirectory, "shortcuts");

    static string BackupFavoritesPath =>
        Path.Combine(BackupDirectory, "Favorites.bin");

    static string BackupFavoritesResolvePath =>
        Path.Combine(BackupDirectory, "FavoritesResolve.bin");

    public static bool Apply(TaskbarLayout layout, bool verbose = false)
    {
        var pinnedDir = EnvironmentInfo.PinnedItemsDirectory;

        // 1. Backup current state (shortcuts + Favorites blob)
        BackupCurrentState(pinnedDir, verbose);

        // 2. Clear existing shortcuts in User Pinned folder
        if (Directory.Exists(pinnedDir))
        {
            foreach (var lnk in Directory.GetFiles(pinnedDir, "*.lnk"))
            {
                try
                {
                    File.Delete(lnk);
                    if (verbose)
                        Console.Error.WriteLine($"  [taskbar] Deleted {Path.GetFileName(lnk)}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Warning: Could not delete {lnk}: {ex.Message}");
                }
            }
        }
        else
        {
            Directory.CreateDirectory(pinnedDir);
        }

        // 3. Create new shortcuts for each pin
        foreach (var pin in layout.Pins)
        {
            CreateShortcutForPin(pin, pinnedDir, verbose);
        }

        // 4. Delete Favorites blob to force explorer to rebuild from shortcuts
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbandKeyPath, writable: true);
            if (key != null)
            {
                key.DeleteValue("Favorites", throwOnMissingValue: false);
                key.DeleteValue("FavoritesResolve", throwOnMissingValue: false);
                if (verbose)
                    Console.Error.WriteLine("  [taskbar] Cleared Favorites registry (will rebuild from shortcuts)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Could not clear Taskband registry: {ex.Message}");
            return false;
        }

        return true;
    }

    public static bool Restore(bool verbose = false)
    {
        var pinnedDir = EnvironmentInfo.PinnedItemsDirectory;

        // Restore shortcuts from backup
        if (Directory.Exists(BackupShortcutsDir))
        {
            // Clear current shortcuts
            if (Directory.Exists(pinnedDir))
            {
                foreach (var lnk in Directory.GetFiles(pinnedDir, "*.lnk"))
                {
                    try { File.Delete(lnk); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(pinnedDir);
            }

            foreach (var lnk in Directory.GetFiles(BackupShortcutsDir, "*.lnk"))
            {
                try
                {
                    File.Copy(lnk, Path.Combine(pinnedDir, Path.GetFileName(lnk)), overwrite: true);
                    if (verbose)
                        Console.Error.WriteLine($"  [taskbar] Restored {Path.GetFileName(lnk)}");
                }
                catch { }
            }
        }

        // Restore Favorites blob from backup
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbandKeyPath, writable: true);
            if (key != null)
            {
                if (File.Exists(BackupFavoritesPath))
                {
                    var data = File.ReadAllBytes(BackupFavoritesPath);
                    key.SetValue("Favorites", data, RegistryValueKind.Binary);
                    if (verbose)
                        Console.Error.WriteLine($"  [taskbar] Restored Favorites blob ({data.Length} bytes)");
                }
                else
                {
                    key.DeleteValue("Favorites", throwOnMissingValue: false);
                }

                if (File.Exists(BackupFavoritesResolvePath))
                {
                    var data = File.ReadAllBytes(BackupFavoritesResolvePath);
                    key.SetValue("FavoritesResolve", data, RegistryValueKind.Binary);
                }
                else
                {
                    key.DeleteValue("FavoritesResolve", throwOnMissingValue: false);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Could not restore Taskband: {ex.Message}");
            return false;
        }

        // Clean up backup
        try
        {
            if (Directory.Exists(BackupDirectory))
                Directory.Delete(BackupDirectory, recursive: true);
        }
        catch { }

        return true;
    }

    static void BackupCurrentState(string pinnedDir, bool verbose)
    {
        EnvironmentInfo.EnsureConfigDirectory();
        Directory.CreateDirectory(BackupShortcutsDir);

        // Backup shortcuts
        if (Directory.Exists(pinnedDir))
        {
            foreach (var lnk in Directory.GetFiles(pinnedDir, "*.lnk"))
            {
                try
                {
                    File.Copy(lnk, Path.Combine(BackupShortcutsDir, Path.GetFileName(lnk)), overwrite: true);
                }
                catch { }
            }

            if (verbose)
                Console.Error.WriteLine($"  [taskbar] Backed up {Directory.GetFiles(BackupShortcutsDir, "*.lnk").Length} shortcut(s)");
        }

        // Backup Favorites blob
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbandKeyPath);
            var favorites = key?.GetValue("Favorites") as byte[];
            if (favorites != null)
            {
                File.WriteAllBytes(BackupFavoritesPath, favorites);
                if (verbose)
                    Console.Error.WriteLine($"  [taskbar] Backed up Favorites blob ({favorites.Length} bytes)");
            }

            var resolve = key?.GetValue("FavoritesResolve") as byte[];
            if (resolve != null)
                File.WriteAllBytes(BackupFavoritesResolvePath, resolve);
        }
        catch { }
    }

    static void CreateShortcutForPin(TaskbarPin pin, string targetDir, bool verbose)
    {
        var lnkPath = Path.Combine(targetDir, pin.DisplayName + ".lnk");

        try
        {
            if (pin.Type == Models.PinType.UWA && pin.AppUserModelID != null)
            {
                // UWP/MSIX apps: create shortcut via explorer.exe shell:AppsFolder\AUMID
                CreateUwpShortcut(lnkPath, pin.AppUserModelID, pin.DisplayName);
                if (verbose)
                    Console.Error.WriteLine($"  [taskbar] Created UWP shortcut: {pin.DisplayName}.lnk -> shell:AppsFolder\\{pin.AppUserModelID}");
            }
            else
            {
                var targetPath = ResolveTargetPath(pin);
                if (targetPath == null)
                {
                    if (verbose)
                        Console.Error.WriteLine($"  [taskbar] Skipping {pin.DisplayName} (unresolvable)");
                    return;
                }

                CreateDesktopShortcut(lnkPath, targetPath, pin.DisplayName);
                if (verbose)
                    Console.Error.WriteLine($"  [taskbar] Created shortcut: {pin.DisplayName}.lnk -> {targetPath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Could not create shortcut for {pin.DisplayName}: {ex.Message}");
        }
    }

    static void CreateDesktopShortcut(string lnkPath, string targetPath, string description)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
        shortcut.Description = description;
        shortcut.Save();
        Marshal.ReleaseComObject(shortcut);
        Marshal.ReleaseComObject(shell);
    }

    static void CreateUwpShortcut(string lnkPath, string aumid, string description)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = @"C:\Windows\explorer.exe";
        shortcut.Arguments = $"shell:AppsFolder\\{aumid}";
        shortcut.Description = description;
        shortcut.Save();
        Marshal.ReleaseComObject(shortcut);
        Marshal.ReleaseComObject(shell);
    }

    static string? ResolveTargetPath(TaskbarPin pin)
    {
        if (pin.DesktopApplicationLinkPath != null)
        {
            var expanded = Environment.ExpandEnvironmentVariables(pin.DesktopApplicationLinkPath);
            if (File.Exists(expanded))
                return ResolveShortcutTarget(expanded);
        }

        if (pin.DesktopApplicationID != null)
        {
            return pin.DesktopApplicationID switch
            {
                "Microsoft.Windows.Explorer" => @"C:\Windows\explorer.exe",
                "MSEdge" => FindEdgeExe(),
                _ => null
            };
        }

        return null;
    }

    static string? FindEdgeExe()
    {
        var paths = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };
        return paths.FirstOrDefault(File.Exists);
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
