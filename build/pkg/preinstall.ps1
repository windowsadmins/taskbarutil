# TaskbarUtil preinstall script
# Version: {{VERSION}}

Write-Host "TaskbarUtil {{VERSION}} - Pre-installation" -ForegroundColor Cyan

# Check Windows 11
$build = [System.Environment]::OSVersion.Version.Build
if ($build -lt 22000) {
    Write-Host "Warning: TaskbarUtil requires Windows 11 (build 22000+). Current: $build" -ForegroundColor Yellow
}

# Remove any existing taskbarutil policy if upgrading
$policyPath = 'HKCU:\Software\Policies\Microsoft\Windows\Explorer'
if (Test-Path $policyPath) {
    $layoutFile = Get-ItemPropertyValue -Path $policyPath -Name 'StartLayoutFile' -ErrorAction SilentlyContinue
    if ($layoutFile) {
        Write-Host "Existing taskbar policy found: $layoutFile" -ForegroundColor Cyan
    }
}

Write-Host "Pre-installation checks complete" -ForegroundColor Green
