# TaskbarUtil Examples

This directory contains example scripts demonstrating various uses of TaskbarUtil.

## Basic Operations

### Pin common applications
```powershell
# Pin Notepad
taskbarutil add "C:\Windows\System32\notepad.exe"

# Pin Calculator
taskbarutil add "calculator:"

# Pin File Explorer
taskbarutil add "explorer.exe"

# Pin Command Prompt
taskbarutil add "cmd.exe"

# Pin PowerShell
taskbarutil add "powershell.exe"
```

### Pin applications with custom labels
```powershell
# Pin Visual Studio Code with custom label
taskbarutil add "C:\Users\%USERNAME%\AppData\Local\Programs\Microsoft VS Code\Code.exe" --label "VS Code"

# Pin Chrome with custom label
taskbarutil add "C:\Program Files\Google\Chrome\Application\chrome.exe" --label "Chrome Browser"
```

### Remove items
```powershell
# Remove specific item
taskbarutil remove "Notepad"

# Remove all items
taskbarutil remove all
```

### List and find operations
```powershell
# List all pinned items
taskbarutil list

# Find a specific item
taskbarutil find "Chrome"
```

## Advanced Examples

### Bulk operations
```powershell
# Pin multiple development tools
taskbarutil add "notepad.exe"
taskbarutil add "cmd.exe" 
taskbarutil add "powershell.exe"
taskbarutil add "C:\Program Files\Git\git-bash.exe" --label "Git Bash"
```

### Environment-specific setup
```powershell
# Developer workstation setup
$apps = @(
    @{Path="C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe"; Label="Visual Studio"},
    @{Path="C:\Users\$env:USERNAME\AppData\Local\Programs\Microsoft VS Code\Code.exe"; Label="VS Code"},
    @{Path="C:\Program Files\Git\git-bash.exe"; Label="Git Bash"},
    @{Path="C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"; Label="PowerShell"}
)

foreach ($app in $apps) {
    if (Test-Path $app.Path) {
        taskbarutil add $app.Path --label $app.Label
        Write-Host "Pinned: $($app.Label)"
    } else {
        Write-Warning "Not found: $($app.Path)"
    }
}
```

### Cleanup scripts
```powershell
# Remove all current pins and set up clean environment
taskbarutil remove all

# Pin essential applications only
taskbarutil add "explorer.exe"
taskbarutil add "msedge.exe"
taskbarutil add "notepad.exe"
```
