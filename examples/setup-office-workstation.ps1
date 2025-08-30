# Office Workstation Setup Script
# This script sets up a typical office workstation taskbar

param(
    [switch]$CleanFirst,
    [switch]$Verbose
)

if ($Verbose) {
    $VerboseFlag = "--verbose"
} else {
    $VerboseFlag = ""
}

Write-Host "Setting up office workstation taskbar..." -ForegroundColor Green

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
        Name = "Microsoft Outlook"
        Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\OUTLOOK.EXE"
        Label = "Outlook"
    },
    @{
        Name = "Microsoft Word"
        Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\WINWORD.EXE"
        Label = "Word"
    },
    @{
        Name = "Microsoft Excel"
        Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\EXCEL.EXE"
        Label = "Excel"
    },
    @{
        Name = "Microsoft PowerPoint"
        Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\POWERPNT.EXE"
        Label = "PowerPoint"
    },
    @{
        Name = "Microsoft Teams"
        Path = "$env:LOCALAPPDATA\Microsoft\Teams\current\Teams.exe"
        Label = "Teams"
    },
    @{
        Name = "Adobe Acrobat Reader"
        Path = "${env:ProgramFiles}\Adobe\Acrobat DC\Acrobat\Acrobat.exe"
        Label = "Acrobat"
    },
    @{
        Name = "Calculator"
        Path = "calc.exe"
        Label = "Calculator"
    },
    @{
        Name = "Notepad"
        Path = "notepad.exe"
        Label = "Notepad"
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

Write-Host "`nOffice workstation setup complete!" -ForegroundColor Green
Write-Host "Run 'taskbarutil list' to see all pinned applications." -ForegroundColor Cyan
