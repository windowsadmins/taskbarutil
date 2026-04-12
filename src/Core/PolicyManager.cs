using Microsoft.Win32;

namespace TaskbarUtil.Core;

public enum ApplyMethod
{
    RegistryPolicy,
    DirectFileCopy
}

public record ApplyResult(ApplyMethod Method, string TargetPath, bool Success, string? Message = null);

public static class PolicyManager
{
    const string PolicyKeyPath = @"Software\Policies\Microsoft\Windows\Explorer";

    static readonly string ShellDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Shell");

    static readonly string ShellLayoutPath =
        Path.Combine(ShellDirectory, "LayoutModification.xml");

    public static ApplyResult Apply(string xmlFilePath, bool verbose = false)
    {
        // Try registry policy first
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PolicyKeyPath);
            key.SetValue("StartLayoutFile", xmlFilePath, RegistryValueKind.ExpandString);
            key.SetValue("LockedStartLayout", 1, RegistryValueKind.DWord);

            if (verbose)
            {
                Console.Error.WriteLine($"  [policy] Set HKCU\\{PolicyKeyPath}\\StartLayoutFile = {xmlFilePath}");
                Console.Error.WriteLine($"  [policy] Set HKCU\\{PolicyKeyPath}\\LockedStartLayout = 1");
            }

            return new ApplyResult(ApplyMethod.RegistryPolicy, PolicyKeyPath, true);
        }
        catch (UnauthorizedAccessException)
        {
            if (verbose)
                Console.Error.WriteLine("  [policy] Registry Policies path is locked (GPO-managed). Falling back to direct file copy.");
        }

        // Fallback: copy XML directly to Shell directory
        try
        {
            var xml = File.ReadAllText(xmlFilePath);
            Directory.CreateDirectory(ShellDirectory);
            File.WriteAllText(ShellLayoutPath, xml);

            if (verbose)
                Console.Error.WriteLine($"  [policy] Wrote layout to {ShellLayoutPath}");

            return new ApplyResult(ApplyMethod.DirectFileCopy, ShellLayoutPath, true,
                "Used direct file copy (registry Policies path is GPO-locked).");
        }
        catch (Exception ex)
        {
            return new ApplyResult(ApplyMethod.DirectFileCopy, ShellLayoutPath, false,
                $"Failed to write layout file: {ex.Message}");
        }
    }

    public static void Reset(bool verbose = false)
    {
        // Try to clean up registry keys
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolicyKeyPath, writable: true);
            if (key != null)
            {
                TryDeleteValue(key, "StartLayoutFile", verbose);
                TryDeleteValue(key, "LockedStartLayout", verbose);

                if (key.ValueCount == 0 && key.SubKeyCount == 0)
                {
                    key.Close();
                    Registry.CurrentUser.DeleteSubKey(PolicyKeyPath, throwOnMissingSubKey: false);
                    if (verbose)
                        Console.Error.WriteLine($"  [policy] Deleted empty key HKCU\\{PolicyKeyPath}");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (verbose)
                Console.Error.WriteLine("  [policy] Could not access registry Policies path (GPO-managed). Skipping.");
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [policy] Registry cleanup warning: {ex.Message}");
        }

        // Remove the direct file copy if present
        try
        {
            if (File.Exists(ShellLayoutPath))
            {
                File.Delete(ShellLayoutPath);
                if (verbose)
                    Console.Error.WriteLine($"  [policy] Deleted {ShellLayoutPath}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not delete {ShellLayoutPath}: {ex.Message}");
        }
    }

    public static bool IsApplied()
    {
        // Check registry policy
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolicyKeyPath);
            if (key != null)
            {
                var val = key.GetValue("LockedStartLayout");
                if (val is int i && i == 1)
                    return true;
            }
        }
        catch { }

        // Check direct file
        return File.Exists(ShellLayoutPath);
    }

    public static string? GetAppliedLayoutPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolicyKeyPath);
            return key?.GetValue("StartLayoutFile") as string;
        }
        catch
        {
            return null;
        }
    }

    static void TryDeleteValue(RegistryKey key, string name, bool verbose)
    {
        try
        {
            if (key.GetValue(name) != null)
            {
                key.DeleteValue(name);
                if (verbose)
                    Console.Error.WriteLine($"  [policy] Deleted HKCU\\{PolicyKeyPath}\\{name}");
            }
        }
        catch { }
    }
}
