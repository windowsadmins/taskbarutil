# TaskbarUtil postinstall script
# Version: {{VERSION}}

$installPath = 'C:\Program Files\sbin'

Write-Host "TaskbarUtil {{VERSION}} - Post-installation" -ForegroundColor Cyan

# Add to system PATH
$currentPath = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
if ($currentPath -notlike "*$installPath*") {
    $newPath = "$currentPath;$installPath"
    [Environment]::SetEnvironmentVariable('PATH', $newPath, 'Machine')
    Write-Host "Added $installPath to system PATH" -ForegroundColor Green
} else {
    Write-Host "PATH already configured" -ForegroundColor Cyan
}

Write-Host "TaskbarUtil installation complete!" -ForegroundColor Green
Write-Host "Usage: taskbarutil --help" -ForegroundColor Cyan
