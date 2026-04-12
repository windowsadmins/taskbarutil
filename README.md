# TaskbarUtil

A command-line utility for managing Windows 11 taskbar pins via local policy. Inspired by macOS [dockutil](https://github.com/kcrawford/dockutil).

## How It Works

Windows 11 does not support direct programmatic pinning/unpinning of taskbar items. Microsoft removed the Shell COM verbs and the registry format is undocumented. The only supported mechanism is **policy-based configuration** via `LayoutModification.xml`.

TaskbarUtil builds and manages a `LayoutModification.xml` config, then deploys it by setting local policy registry keys under `HKCU\Software\Policies\Microsoft\Windows\Explorer`. No Active Directory or Intune server required -- the policy keys work on standalone machines.

### Live Apply (no logout required)

When you run `taskbarutil apply`, the tool applies changes immediately by:

1. Setting the policy registry keys (`StartLayoutFile` + `LockedStartLayout`)
2. Killing `StartMenuExperienceHost` and `ShellExperienceHost` (releases cache locks)
3. Deleting `start2.bin` (pin data cache)
4. Deleting the `Taskband` registry key (pin order cache)
5. Restarting `explorer.exe` (rebuilds taskbar from the policy XML)

The taskbar disappears briefly (~5-15 seconds) while explorer reinitializes, then returns with the configured pins in the correct order. Requires elevation to write policy registry keys.

## Usage

```
taskbarutil [command] [options]

Commands:
  list          List items currently pinned to the taskbar
  find <query>  Search for installed apps by name
  add <app>     Add an app to the taskbar layout config
  remove <app>  Remove an app from the taskbar layout config
  show          Display the current layout config (XML)
  apply         Apply the layout config via local policy
  reset         Remove policy and restore default taskbar

Options:
  -v, --verbose   Enable verbose output
  --dry-run       Show what would be done without making changes
  --version       Show version information
  -h, --help      Show help
```

### Examples

```powershell
# See what's currently pinned
taskbarutil list

# Search for an app
taskbarutil find chrome

# Build a taskbar layout
taskbarutil add "Google Chrome"
taskbarutil add "Windows Terminal"
taskbarutil add "File Explorer"
taskbarutil add "Microsoft Edge"

# Preview the config
taskbarutil show

# Apply it
taskbarutil apply

# Reset to default
taskbarutil reset
```

### Add Options

```powershell
# Add at a specific position (1-based)
taskbarutil add "Notepad" --position 2

# Add a UWP app by AppUserModelID
taskbarutil add "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" --uwp

# Add a desktop app by DesktopApplicationID
taskbarutil add "Microsoft.Windows.Explorer" --app-id

# Dry run
taskbarutil add "Chrome" --dry-run
```

## Requirements

- Windows 11
- .NET 9.0 Runtime (or use the self-contained build)

## Building

```powershell
# Build
dotnet build

# Run tests
dotnet test

# Build self-contained executable
.\build.ps1 -Architecture x64
```

## How the Policy Works

TaskbarUtil stores its config at `%LocalAppData%\TaskbarUtil\LayoutModification.xml` and sets these registry values when you run `apply`:

| Key | Value | Purpose |
|-----|-------|---------|
| `HKCU\...\Explorer\StartLayoutFile` | Path to XML | Points to the layout config |
| `HKCU\...\Explorer\LockedStartLayout` | `1` | Activates the layout policy |

The XML must include `Version="1"` on the `LayoutModificationTemplate` root element -- without it, Windows silently ignores the entire file.

Running `reset` removes these values, clears the caches, and restarts explorer to restore the default taskbar.

## Supported Apps

TaskbarUtil has a built-in registry of common apps (Chrome, Edge, Terminal, Explorer, Notepad, Teams, VS Code, etc.) that can be added by friendly name. For other apps, it searches the Start Menu shortcuts and installed AppxPackages.

Three types of pins are supported in the XML:

- **Desktop apps via .lnk path**: `DesktopApplicationLinkPath` pointing to a Start Menu shortcut
- **Desktop apps via ID**: `DesktopApplicationID` for system apps like File Explorer
- **UWP/MSIX apps**: `AppUserModelID` for Store/modern apps

## License

Apache-2.0
