# Developer Workstation Setup Script
# This script sets up a typical developer workstation taskbar

param(
    [switch]$CleanFirst,
    [switch]$Verbose
)

if ($Verbose) {
    $VerboseFlag = "--verbose"
} else {
    $VerboseFlag = ""
}

Write-Host "Setting up developer workstation taskbar..." -ForegroundColor Green

# Clean existing pins if requested
if ($CleanFirst) {
    Write-Host "Removing all existing taskbar pins..." -ForegroundColor Yellow
    & taskbarutil remove all $VerboseFlag
}

# Define applications to pin
$Applications = @(
    @{
        Name = "File Explorer"
        Path = "explorer.exe"
        Label = "Explorer"
    },
    @{
        Name = "Microsoft Edge"
        Path = "msedge.exe"
        Label = "Edge"
    },
    @{
        Name = "Windows Terminal"
        Path = "wt.exe"
        Label = "Terminal"
    },
    @{
        Name = "Visual Studio Code"
        Path = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe"
        Label = "VS Code"
    },
    @{
        Name = "Visual Studio"
        Path = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe"
        Label = "Visual Studio"
    },
    @{
        Name = "Git Bash"
        Path = "${env:ProgramFiles}\Git\git-bash.exe"
        Label = "Git Bash"
    },
    @{
        Name = "Postman"
        Path = "$env:LOCALAPPDATA\Postman\Postman.exe"
        Label = "Postman"
    },
    @{
        Name = "Notepad++"
        Path = "${env:ProgramFiles}\Notepad++\notepad++.exe"
        Label = "Notepad++"
    }
)

# Pin applications
foreach ($app in $Applications) {
    if (Test-Path $app.Path -ErrorAction SilentlyContinue) {
        Write-Host "Pinning: $($app.Name)" -ForegroundColor Cyan
        & taskbarutil add $app.Path --label $app.Label $VerboseFlag
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ Successfully pinned $($app.Name)" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Failed to pin $($app.Name)" -ForegroundColor Red
        }
    } else {
        Write-Host "  - Skipping $($app.Name) (not found at $($app.Path))" -ForegroundColor Gray
    }
}

Write-Host "`nDeveloper workstation setup complete!" -ForegroundColor Green
Write-Host "Run 'taskbarutil list' to see all pinned applications." -ForegroundColor Cyan
