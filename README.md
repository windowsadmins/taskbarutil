# TaskbarUtil

A command line utility for managing Windows taskbar pins, inspired by macOS [dockutil](https://github.com/kcrawford/dockutil).

## Features

- **Add** - Pin applications, files, folders, and URLs to the taskbar
- **Remove** - Unpin items from the taskbar
- **List** - Display all pinned items
- **Find** - Search for specific pinned items
- **Move** - Reorder taskbar items (limited support due to Windows restrictions)
- **Code Signing Support** - Build script supports digital signatures for enterprise environments
- **Automated Build System** - PowerShell build script with CI/CD support
- **Windows 11 Support** - Dedicated Windows 11 taskbar management

## Installation

### Option 1: Download Binary (Recommended)
Download the latest executable from the [releases page](https://github.com/windowsadmins/taskbarutil/releases).

### Option 2: Build from Source
```powershell
git clone https://github.com/windowsadmins/taskbarutil.git
cd taskbarutil
.\build.ps1 -Clean
```

### Option 3: Enterprise Build with Code Signing
For enterprise environments with code signing certificates:
```powershell
# Configure your certificate first, then build with signing
.\build.ps1 -Sign -Clean
```

## Usage

```
taskbarutil [command] [options]

Commands:
  add      Add an item to the taskbar
  remove   Remove an item from the taskbar
  list     List all items in the taskbar
  find     Find an item in the taskbar
  move     Move an item in the taskbar
  
Options:
  --version, -V     Display version information
  --verbose, -v     Enable verbose output
  --no-restart     Do not refresh the taskbar after changes
  --help, -h       Show help information
```

### Add Command

```
taskbarutil add <path> [options]

Arguments:
  path              Path to item to add (application, file, folder, or URL)

Options:
  --label, -l       Custom label for the taskbar item
  --replacing, -r   Replace an existing item with this label
  --position, -p    Position: beginning, end, or index number
  --after, -a       Place after this item
  --before, -b      Place before this item
```

### Remove Command

```
taskbarutil remove <item>

Arguments:
  item              Item to remove (name, path, or 'all')
```

### Examples

Pin Notepad to the taskbar:
```powershell
taskbarutil add "C:\Windows\System32\notepad.exe"
```

Pin with custom label:
```powershell
taskbarutil add "C:\Program Files\Application\app.exe" --label "My App"
```

Remove an item:
```powershell
taskbarutil remove "Notepad"
```

List all pinned items:
```powershell
taskbarutil list
```

Find a specific item:
```powershell
taskbarutil find "Chrome"
```

Remove all items:
```powershell
taskbarutil remove all
```

## Limitations

- **Move operations**: Windows doesn't provide a reliable API for programmatically reordering taskbar items. Users may need to manually drag items to reorder them.
- **Windows-only**: This tool only works on Windows systems.
- **Administrator rights**: Some operations may require administrator privileges depending on the target application location.

## System Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for details.

## Acknowledgments

Inspired by Kyle Crawford's [dockutil](https://github.com/kcrawford/dockutil) for macOS.
