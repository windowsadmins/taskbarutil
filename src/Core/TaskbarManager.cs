using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarUtil.Core;

public class TaskbarManager
{
    private static bool _verbose = false;
    
    // Registry paths for taskbar pins
    private const string TaskbarPinsRegPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    private const string QuickLaunchRegPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("shell32.dll")]
    private static extern int SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    
    [DllImport("ole32.dll")]
    private static extern int CoInitialize(IntPtr pvReserved);
    
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;
    private const uint WM_COMMAND = 0x0111;

    public static void SetVerbose(bool verbose)
    {
        _verbose = verbose;
    }

    private static void LogVerbose(string message)
    {
        if (_verbose)
        {
            Console.WriteLine($"[VERBOSE] {message}");
        }
    }

    public static void RefreshTaskbar()
    {
        LogVerbose("Refreshing taskbar...");
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public static void RestartExplorer()
    {
        LogVerbose("Restarting Windows Explorer...");
        try
        {
            // Kill explorer
            var explorerProcesses = Process.GetProcessesByName("explorer");
            foreach (var process in explorerProcesses)
            {
                process.Kill();
                process.WaitForExit();
            }

            // Start explorer again
            Thread.Sleep(1000);
            Process.Start("explorer.exe");
            LogVerbose("Explorer restarted successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error restarting explorer: {ex.Message}");
        }
    }

    public static List<TaskbarItem> GetPinnedItems()
    {
        LogVerbose("Getting pinned taskbar items...");
        var items = new List<TaskbarItem>();
        
        try
        {
            // Try multiple methods to get pinned items
            items.AddRange(GetPinnedItemsFromRegistry());
            items.AddRange(GetPinnedItemsFromToolbarData());
            
            LogVerbose($"Found {items.Count} pinned items");
        }
        catch (Exception ex)
        {
            LogVerbose($"Error getting pinned items: {ex.Message}");
        }

        return items.DistinctBy(x => x.Path).ToList();
    }

    private static List<TaskbarItem> GetPinnedItemsFromRegistry()
    {
        var items = new List<TaskbarItem>();
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbarPinsRegPath);
            if (key != null)
            {
                var favorites = key.GetValue("Favorites") as byte[];
                if (favorites != null)
                {
                    items.AddRange(ParseTaskbarData(favorites));
                }
            }
        }
        catch (Exception ex)
        {
            LogVerbose($"Error reading from registry: {ex.Message}");
        }

        return items;
    }

    private static List<TaskbarItem> GetPinnedItemsFromToolbarData()
    {
        var items = new List<TaskbarItem>();
        
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var taskbarPath = Path.Combine(appDataPath, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            
            if (Directory.Exists(taskbarPath))
            {
                var shortcuts = Directory.GetFiles(taskbarPath, "*.lnk");
                foreach (var shortcut in shortcuts)
                {
                    var item = CreateTaskbarItemFromShortcut(shortcut);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogVerbose($"Error reading taskbar shortcuts: {ex.Message}");
        }

        return items;
    }

    private static TaskbarItem? CreateTaskbarItemFromShortcut(string shortcutPath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(shortcutPath);
            var targetPath = GetShortcutTarget(shortcutPath);
            
            return new TaskbarItem
            {
                Name = fileName,
                Path = targetPath ?? shortcutPath,
                Type = GetItemType(targetPath ?? shortcutPath),
                Position = -1 // Will be determined later
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetShortcutTarget(string shortcutPath)
    {
        try
        {
            CoInitialize(IntPtr.Zero);
            
            // Use IWshShell to get shortcut target
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            var shortcut = shell.CreateShortcut(shortcutPath);
            var target = shortcut.TargetPath as string;
            
            CoUninitialize();
            return target;
        }
        catch
        {
            return null;
        }
    }

    private static List<TaskbarItem> ParseTaskbarData(byte[] data)
    {
        var items = new List<TaskbarItem>();
        
        // This is a simplified parser - the actual format is complex
        // We'll implement a basic version that can extract some information
        
        try
        {
            // The taskbar data format is undocumented and complex
            // For now, we'll return an empty list and rely on the shortcuts method
            LogVerbose("Parsing taskbar registry data (limited implementation)");
        }
        catch (Exception ex)
        {
            LogVerbose($"Error parsing taskbar data: {ex.Message}");
        }

        return items;
    }

    public static bool PinToTaskbar(string itemPath, TaskbarItemType type = TaskbarItemType.Application)
    {
        LogVerbose($"Pinning item to taskbar: {itemPath}");
        
        try
        {
            // Resolve the path
            var resolvedPath = ResolvePath(itemPath);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                Console.WriteLine($"Could not resolve path: {itemPath}");
                return false;
            }

            // Check if already pinned
            var existingItems = GetPinnedItems();
            if (existingItems.Any(x => string.Equals(x.Path, resolvedPath, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"Item is already pinned: {resolvedPath}");
                return true;
            }

            // Create shortcut in taskbar folder
            var taskbarPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"
            );

            if (!Directory.Exists(taskbarPath))
            {
                Directory.CreateDirectory(taskbarPath);
            }

            var shortcutName = Path.GetFileNameWithoutExtension(resolvedPath) + ".lnk";
            var shortcutPath = Path.Combine(taskbarPath, shortcutName);

            CreateShortcut(shortcutPath, resolvedPath);
            
            LogVerbose($"Created shortcut: {shortcutPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pinning item: {ex.Message}");
            return false;
        }
    }

    public static bool UnpinFromTaskbar(string itemIdentifier)
    {
        LogVerbose($"Unpinning item from taskbar: {itemIdentifier}");
        
        try
        {
            var pinnedItems = GetPinnedItems();
            var itemToRemove = FindItem(pinnedItems, itemIdentifier);
            
            if (itemToRemove == null)
            {
                Console.WriteLine($"Item not found: {itemIdentifier}");
                return false;
            }

            // Remove the shortcut
            var taskbarPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"
            );

            var shortcuts = Directory.GetFiles(taskbarPath, "*.lnk");
            foreach (var shortcut in shortcuts)
            {
                var target = GetShortcutTarget(shortcut);
                if (string.Equals(target, itemToRemove.Path, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(shortcut).Equals(itemToRemove.Name, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(shortcut);
                    LogVerbose($"Deleted shortcut: {shortcut}");
                    return true;
                }
            }

            Console.WriteLine($"Could not find shortcut for: {itemIdentifier}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error unpinning item: {ex.Message}");
            return false;
        }
    }

    public static TaskbarItem? FindItem(List<TaskbarItem> items, string identifier)
    {
        // Try to find by name, path, or partial match
        return items.FirstOrDefault(x =>
            string.Equals(x.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Path, identifier, StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains(identifier, StringComparison.OrdinalIgnoreCase) ||
            x.Path.Contains(identifier, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static string? ResolvePath(string path)
    {
        try
        {
            // Handle environment variables
            path = Environment.ExpandEnvironmentVariables(path);
            
            // Handle relative paths
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(path);
            }

            // Check if file exists
            if (File.Exists(path) || Directory.Exists(path))
            {
                return path;
            }

            // Try to find in common locations
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                
                var possiblePaths = new[]
                {
                    Path.Combine(programFiles, path),
                    Path.Combine(programFilesX86, path),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), path),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path)
                };

                foreach (var possiblePath in possiblePaths)
                {
                    if (File.Exists(possiblePath))
                    {
                        return possiblePath;
                    }
                }
            }

            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        try
        {
            CoInitialize(IntPtr.Zero);
            
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            var shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Description = $"Pinned by TaskbarUtil";
            shortcut.Save();
            
            CoUninitialize();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create shortcut: {ex.Message}", ex);
        }
    }

    private static TaskbarItemType GetItemType(string path)
    {
        if (string.IsNullOrEmpty(path))
            return TaskbarItemType.Unknown;

        if (Directory.Exists(path))
            return TaskbarItemType.Folder;

        if (path.Contains("://"))
            return TaskbarItemType.Url;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".exe" => TaskbarItemType.Application,
            ".lnk" => TaskbarItemType.Shortcut,
            _ => TaskbarItemType.File
        };
    }

    public static void PrintItems(List<TaskbarItem> items)
    {
        if (items.Count == 0)
        {
            Console.WriteLine("No items found in taskbar.");
            return;
        }

        Console.WriteLine("Taskbar Items:");
        Console.WriteLine("Name\tPath\tType");
        Console.WriteLine(new string('-', 80));

        foreach (var item in items)
        {
            Console.WriteLine($"{item.Name}\t{item.Path}\t{item.Type}");
        }
    }
}
