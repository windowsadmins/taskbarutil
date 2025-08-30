using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TaskbarUtil.Core;

/// <summary>
/// Windows 11 specific taskbar manager using modern APIs and PowerShell integration
/// </summary>
public class Windows11TaskbarManager
{
    private const string TASKBAR_LAYOUT_REGISTRY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    private const string FAVORITES_REGISTRY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Taskband\Favorites";
    private const string SHORTCUTS_FOLDER = @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar";
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellExecute(IntPtr hwnd, string lpOperation, string lpFile, string lpParameters, string lpDirectory, int nShowCmd);
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);
    
    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);
    
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    private static readonly Guid FOLDERID_ApplicationShortcuts = new("A3918781-E5F2-4890-B3D9-A7E54332328C");
    private const uint KF_FLAG_CREATE = 0x00008000;
    private const uint KF_FLAG_DONT_VERIFY = 0x00004000;
    private const int SW_HIDE = 0;
    private const uint WM_COMMAND = 0x0111;
    private const uint TB_REFRESH = 0x041D;

    /// <summary>
    /// Pins an application to the taskbar using Windows 11 methods
    /// </summary>
    public async Task<bool> PinToTaskbarAsync(string applicationPath)
    {
        if (!File.Exists(applicationPath))
        {
            throw new FileNotFoundException($"Application not found: {applicationPath}");
        }

        var success = false;

        // Method 1: Try PowerShell Start-Layout approach (Windows 11 22H2+)
        success = await TryPowerShellLayoutMethod(applicationPath);
        if (success) return true;

        // Method 2: Try direct taskbar shortcut creation
        success = await TryTaskbarShortcutMethod(applicationPath);
        if (success) return true;

        // Method 3: Try registry manipulation
        success = await TryRegistryMethod(applicationPath);
        if (success) return true;

        // Method 4: Try COM interface approach
        success = await TryComInterfaceMethod(applicationPath);
        if (success) return true;

        return false;
    }

    /// <summary>
    /// Unpins an application from the taskbar
    /// </summary>
    public async Task<bool> UnpinFromTaskbarAsync(string applicationPath)
    {
        var success = false;

        // Method 1: Remove from taskbar shortcuts
        success = await RemoveTaskbarShortcut(applicationPath);
        if (success) return true;

        // Method 2: Registry cleanup
        success = await CleanupRegistry(applicationPath);
        if (success) return true;

        // Method 3: PowerShell removal
        success = await TryPowerShellRemoval(applicationPath);
        if (success) return true;

        return false;
    }

    /// <summary>
    /// Lists all pinned applications
    /// </summary>
    public async Task<List<string>> ListPinnedApplicationsAsync()
    {
        var pinned = new List<string>();

        // Get from taskbar shortcuts folder
        var taskbarFolder = GetTaskbarShortcutsFolder();
        if (Directory.Exists(taskbarFolder))
        {
            foreach (var shortcut in Directory.GetFiles(taskbarFolder, "*.lnk"))
            {
                var target = GetShortcutTarget(shortcut);
                if (!string.IsNullOrEmpty(target))
                {
                    pinned.Add(target);
                }
            }
        }

        // Get from registry
        var registryPinned = await GetRegistryPinnedItems();
        pinned.AddRange(registryPinned);

        return pinned.Distinct().ToList();
    }

    #region Private Methods

    private async Task<bool> TryPowerShellLayoutMethod(string applicationPath)
    {
        try
        {
            // Create a temporary layout XML for Windows 11
            var layoutXml = CreateTaskbarLayoutXml(applicationPath);
            var tempLayoutFile = Path.Combine(Path.GetTempPath(), $"taskbar_layout_{Guid.NewGuid()}.xml");
            
            await File.WriteAllTextAsync(tempLayoutFile, layoutXml);

            var psScript = $@"
                try {{
                    Import-StartLayout -LayoutPath '{tempLayoutFile}' -MountPath C:\
                    Write-Output 'SUCCESS'
                }} catch {{
                    # Fallback: Try direct registry approach
                    $appName = Split-Path '{applicationPath}' -Leaf
                    $regPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Taskband'
                    New-ItemProperty -Path $regPath -Name $appName -Value '{applicationPath}' -Force | Out-Null
                    Write-Output 'SUCCESS'
                }}
            ";

            var result = await RunPowerShellScript(psScript);
            
            // Cleanup
            if (File.Exists(tempLayoutFile))
            {
                File.Delete(tempLayoutFile);
            }

            return result.Contains("SUCCESS");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PowerShell layout method failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryTaskbarShortcutMethod(string applicationPath)
    {
        try
        {
            var taskbarFolder = GetTaskbarShortcutsFolder();
            if (!Directory.Exists(taskbarFolder))
            {
                Directory.CreateDirectory(taskbarFolder);
            }

            var shortcutName = Path.GetFileNameWithoutExtension(applicationPath) + ".lnk";
            var shortcutPath = Path.Combine(taskbarFolder, shortcutName);

            // Create shortcut using PowerShell
            var psScript = $@"
                $WshShell = New-Object -comObject WScript.Shell
                $Shortcut = $WshShell.CreateShortcut('{shortcutPath}')
                $Shortcut.TargetPath = '{applicationPath}'
                $Shortcut.WorkingDirectory = '{Path.GetDirectoryName(applicationPath)}'
                $Shortcut.Save()
                Write-Output 'SUCCESS'
            ";

            var result = await RunPowerShellScript(psScript);
            
            if (result.Contains("SUCCESS"))
            {
                // Refresh taskbar
                RefreshTaskbar();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Taskbar shortcut method failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryRegistryMethod(string applicationPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(FAVORITES_REGISTRY);
            if (key != null)
            {
                var appName = Path.GetFileNameWithoutExtension(applicationPath);
                key.SetValue(appName, applicationPath);
                
                // Also add to taskband entries
                using var taskbandKey = Registry.CurrentUser.CreateSubKey(TASKBAR_LAYOUT_REGISTRY);
                taskbandKey?.SetValue($"Pinned_{appName}", applicationPath);
                
                RefreshTaskbar();
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Registry method failed: {ex.Message}");
        }
        
        return false;
    }

    private async Task<bool> TryComInterfaceMethod(string applicationPath)
    {
        try
        {
            // Use verb approach with modern shell
            var psScript = $@"
                $shell = New-Object -ComObject Shell.Application
                $folder = $shell.Namespace((Split-Path '{applicationPath}'))
                $item = $folder.ParseName((Split-Path '{applicationPath}' -Leaf))
                
                # Try different pin verbs for Windows 11
                $verbs = $item.Verbs() | Where-Object {{ $_.Name -match 'pin|taskbar' }}
                foreach ($verb in $verbs) {{
                    try {{
                        $verb.DoIt()
                        Write-Output 'SUCCESS'
                        break
                    }} catch {{
                        continue
                    }}
                }}
                
                # Fallback: Use SendTo taskbar
                $sendToPath = [Environment]::GetFolderPath('SendTo')
                $taskbarPath = Join-Path $sendToPath 'Taskbar (create shortcut).lnk'
                if (Test-Path $taskbarPath) {{
                    Copy-Item '{applicationPath}' $taskbarPath
                    Write-Output 'SUCCESS'
                }}
            ";

            var result = await RunPowerShellScript(psScript);
            return result.Contains("SUCCESS");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"COM interface method failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RemoveTaskbarShortcut(string applicationPath)
    {
        try
        {
            var taskbarFolder = GetTaskbarShortcutsFolder();
            var shortcutName = Path.GetFileNameWithoutExtension(applicationPath) + ".lnk";
            var shortcutPath = Path.Combine(taskbarFolder, shortcutName);

            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                RefreshTaskbar();
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Remove taskbar shortcut failed: {ex.Message}");
        }
        
        return false;
    }

    private async Task<bool> CleanupRegistry(string applicationPath)
    {
        try
        {
            var appName = Path.GetFileNameWithoutExtension(applicationPath);
            
            using var favKey = Registry.CurrentUser.OpenSubKey(FAVORITES_REGISTRY, true);
            favKey?.DeleteValue(appName, false);
            
            using var taskbandKey = Registry.CurrentUser.OpenSubKey(TASKBAR_LAYOUT_REGISTRY, true);
            taskbandKey?.DeleteValue($"Pinned_{appName}", false);
            
            RefreshTaskbar();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Registry cleanup failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryPowerShellRemoval(string applicationPath)
    {
        try
        {
            var psScript = $@"
                # Try to unpin using shell verbs
                $shell = New-Object -ComObject Shell.Application
                $folder = $shell.Namespace((Split-Path '{applicationPath}'))
                $item = $folder.ParseName((Split-Path '{applicationPath}' -Leaf))
                
                $verbs = $item.Verbs() | Where-Object {{ $_.Name -match 'unpin|remove' }}
                foreach ($verb in $verbs) {{
                    try {{
                        $verb.DoIt()
                        Write-Output 'SUCCESS'
                        break
                    }} catch {{
                        continue
                    }}
                }}
            ";

            var result = await RunPowerShellScript(psScript);
            return result.Contains("SUCCESS");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PowerShell removal failed: {ex.Message}");
            return false;
        }
    }

    private async Task<List<string>> GetRegistryPinnedItems()
    {
        var items = new List<string>();
        
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FAVORITES_REGISTRY);
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (!string.IsNullOrEmpty(value) && File.Exists(value))
                    {
                        items.Add(value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Get registry pinned items failed: {ex.Message}");
        }
        
        return items;
    }

    private string GetTaskbarShortcutsFolder()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, SHORTCUTS_FOLDER);
    }

    private string GetShortcutTarget(string shortcutPath)
    {
        try
        {
            var shell = new object(); // Dynamic COM object
            var folder = shell.GetType().InvokeMember("Namespace", 
                System.Reflection.BindingFlags.InvokeMethod, null, shell, 
                new object[] { Path.GetDirectoryName(shortcutPath) });
            
            var item = folder.GetType().InvokeMember("ParseName", 
                System.Reflection.BindingFlags.InvokeMethod, null, folder, 
                new object[] { Path.GetFileName(shortcutPath) });
                
            var link = item.GetType().InvokeMember("GetLink", 
                System.Reflection.BindingFlags.InvokeMethod, null, item, null);
                
            return link.GetType().InvokeMember("Path", 
                System.Reflection.BindingFlags.GetProperty, null, link, null)?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private string CreateTaskbarLayoutXml(string applicationPath)
    {
        var appName = Path.GetFileNameWithoutExtension(applicationPath);
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<LayoutModificationTemplate 
    xmlns=""http://schemas.microsoft.com/Start/2014/LayoutModification""
    xmlns:defaultlayout=""http://schemas.microsoft.com/Start/2014/FullDefaultLayout""
    xmlns:start=""http://schemas.microsoft.com/Start/2014/StartLayout""
    xmlns:taskbar=""http://schemas.microsoft.com/Start/2014/TaskbarLayout""
    Version=""1"">
    <CustomTaskbarLayoutCollection>
        <defaultlayout:TaskbarLayout>
            <taskbar:TaskbarPinList>
                <taskbar:DesktopApp DesktopApplicationLinkPath=""{applicationPath}"" />
            </taskbar:TaskbarPinList>
        </defaultlayout:TaskbarLayout>
    </CustomTaskbarLayoutCollection>
</LayoutModificationTemplate>";
    }

    private async Task<string> RunPowerShellScript(string script)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return string.IsNullOrEmpty(error) ? output : error;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PowerShell execution failed: {ex.Message}");
            return "";
        }
    }

    private void RefreshTaskbar()
    {
        try
        {
            var taskbarWindow = FindWindow("Shell_TrayWnd", null);
            if (taskbarWindow != IntPtr.Zero)
            {
                PostMessage(taskbarWindow, WM_COMMAND, new IntPtr(TB_REFRESH), IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Taskbar refresh failed: {ex.Message}");
        }
    }

    #endregion
}
