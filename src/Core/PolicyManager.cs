using Microsoft.Win32;

namespace TaskbarUtil.Core;

public enum ApplyMethod
{
    RegistryPolicy,
    DirectFileCopy
}

public record ApplyResult(ApplyMethod Method, string TargetPath, bool Success, string? Message = null);

public record UserProfile(string Sid, string ProfilePath, string? UserName);

public static class PolicyManager
{
    const string PolicyKeyPath = @"Software\Policies\Microsoft\Windows\Explorer";
    const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    const string TaskbandKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    static readonly string SharedXmlDir = @"C:\ProgramData\TaskbarUtil";
    static readonly string SharedXmlPath = Path.Combine(SharedXmlDir, "LayoutModification.xml");

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

    /// <summary>
    /// Apply the taskbar layout to all user profiles on the machine.
    /// Copies XML to a shared ProgramData location, then sets policy keys
    /// in each user's HKU hive and clears their caches.
    /// Requires elevation (admin/SYSTEM).
    /// </summary>
    public static int ApplyAllHomes(string xmlFilePath, bool verbose = false)
    {
        // Copy XML to shared location so all users reference the same file
        Directory.CreateDirectory(SharedXmlDir);
        File.Copy(xmlFilePath, SharedXmlPath, overwrite: true);
        if (verbose)
            Console.Error.WriteLine($"  [allhomes] Deployed XML to {SharedXmlPath}");

        var profiles = GetUserProfiles(verbose);
        int applied = 0;

        foreach (var profile in profiles)
        {
            // Check if user's hive is loaded in HKU (only logged-in users)
            using var hiveCheck = Registry.Users.OpenSubKey(profile.Sid);
            if (hiveCheck == null)
            {
                if (verbose)
                    Console.Error.WriteLine($"  [allhomes] Skipped {profile.UserName ?? profile.Sid} (not logged in, hive not loaded)");
                continue;
            }

            try
            {
                using var hku = Registry.Users.CreateSubKey($@"{profile.Sid}\{PolicyKeyPath}");
                hku.SetValue("StartLayoutFile", SharedXmlPath, RegistryValueKind.ExpandString);
                hku.SetValue("LockedStartLayout", 1, RegistryValueKind.DWord);

                // Clear start2.bin cache
                var start2 = Path.Combine(profile.ProfilePath,
                    @"AppData\Local\Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState\start2.bin");
                if (File.Exists(start2))
                {
                    try { File.Delete(start2); } catch { }
                }

                // Clear Taskband registry (pin order cache)
                try
                {
                    Registry.Users.DeleteSubKeyTree($@"{profile.Sid}\{TaskbandKeyPath}", throwOnMissingSubKey: false);
                }
                catch { }

                applied++;
                if (verbose)
                    Console.Error.WriteLine($"  [allhomes] Applied to {profile.UserName ?? profile.Sid}");
            }
            catch (Exception ex)
            {
                if (verbose)
                    Console.Error.WriteLine($"  [allhomes] Failed for {profile.UserName ?? profile.Sid}: {ex.Message}");
            }
        }

        // Also deploy to Default profile for new users
        try
        {
            var defaultShell = EnvironmentInfo.DefaultProfileShellPath;
            Directory.CreateDirectory(defaultShell);
            File.Copy(xmlFilePath, Path.Combine(defaultShell, "LayoutModification.xml"), overwrite: true);
            if (verbose)
                Console.Error.WriteLine($"  [allhomes] Deployed to Default profile for new users");
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [allhomes] Could not deploy to Default profile: {ex.Message}");
        }

        return applied;
    }

    public static List<UserProfile> GetUserProfiles(bool verbose = false)
    {
        var profiles = new List<UserProfile>();

        try
        {
            using var profileList = Registry.LocalMachine.OpenSubKey(ProfileListPath);
            if (profileList == null) return profiles;

            foreach (var sidName in profileList.GetSubKeyNames())
            {
                // Skip system SIDs (S-1-5-18, S-1-5-19, S-1-5-20)
                if (!sidName.StartsWith("S-1-") || sidName is "S-1-5-18" or "S-1-5-19" or "S-1-5-20")
                    continue;

                using var subKey = profileList.OpenSubKey(sidName);
                var profilePath = subKey?.GetValue("ProfileImagePath") as string;
                if (profilePath == null || !Directory.Exists(profilePath))
                    continue;

                // Try to resolve username from SID
                string? userName = null;
                try
                {
                    var sid = new System.Security.Principal.SecurityIdentifier(sidName);
                    userName = sid.Translate(typeof(System.Security.Principal.NTAccount))?.Value;
                }
                catch { }

                profiles.Add(new UserProfile(sidName, profilePath, userName));
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [allhomes] Error enumerating profiles: {ex.Message}");
        }

        return profiles;
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
