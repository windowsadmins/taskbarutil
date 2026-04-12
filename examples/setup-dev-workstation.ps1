# Developer Workstation Taskbar Setup
# Builds a taskbar layout with common developer tools and applies it

param(
    [switch]$DryRun,
    [switch]$Verbose
)

$flags = @()
if ($DryRun) { $flags += "--dry-run" }
if ($Verbose) { $flags += "--verbose" }

Write-Host "Setting up developer workstation taskbar..." -ForegroundColor Green

# Reset any existing layout config
if (-not $DryRun) {
    & taskbarutil reset --no-restart 2>$null
}

# Build the layout
$apps = @(
    "File Explorer",
    "Google Chrome",
    "Windows Terminal",
    "Visual Studio Code",
    "Microsoft Edge",
    "Notepad"
)

foreach ($app in $apps) {
    Write-Host "  Adding: $app" -ForegroundColor Cyan
    & taskbarutil add $app @flags
}

# Show the result
Write-Host ""
Write-Host "Layout config:" -ForegroundColor Green
& taskbarutil show

# Apply
if (-not $DryRun) {
    Write-Host ""
    Write-Host "Applying layout..." -ForegroundColor Yellow
    & taskbarutil apply @flags
}

Write-Host ""
Write-Host "Done. Run 'taskbarutil list' to see current pinned items." -ForegroundColor Green
