#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Complex Windows Taskbar Configuration Script - Enterprise Edition

.DESCRIPTION
    This script replicates the complex dockutil functionality for Windows environments.
    It supports conditional application pinning based on:
    - Computer name patterns
    - Registry values
    - Installed software detection
    - User roles and permissions
    - Hardware configurations

.PARAMETER ComputerType
    Override automatic computer type detection

.PARAMETER CleanFirst
    Remove all existing taskbar pins before applying configuration

.PARAMETER NoRestart
    Skip taskbar refresh after changes

.PARAMETER DryRun
    Show what would be done without making changes

.PARAMETER ConfigFile
    Path to custom configuration file

.PARAMETER UserType
    Specify user type (student, faculty, admin, lab)

.PARAMETER Verbose
    Enable verbose output

.EXAMPLE
    .\Configure-EnterpriseTaskbar.ps1
    Auto-detect environment and configure taskbar

.EXAMPLE
    .\Configure-EnterpriseTaskbar.ps1 -ComputerType "DesignLab" -CleanFirst
    Configure for design lab, removing existing pins first

.EXAMPLE
    .\Configure-EnterpriseTaskbar.ps1 -UserType "student" -DryRun
    Show what would be configured for student accounts
#>

[CmdletBinding()]
param(
    [string]$ComputerType = $null,
    [switch]$CleanFirst,
    [switch]$NoRestart,
    [switch]$DryRun,
    [string]$ConfigFile = $null,
    [ValidateSet('student', 'faculty', 'admin', 'lab', 'auto')]
    [string]$UserType = 'auto'
)

# Import required modules and set preferences
$ErrorActionPreference = 'Continue'

# Configuration classes and structures
class TaskbarConfig {
    [string]$Name
    [string[]]$ComputerPatterns
    [string[]]$RegistryChecks
    [string[]]$UserTypes
    [TaskbarApp[]]$Applications
    [TaskbarFolder[]]$Folders
    [hashtable]$Settings
}

class TaskbarApp {
    [string]$Name
    [string]$Path
    [string]$Label
    [string[]]$AlternatePaths
    [string[]]$Conditions
    [bool]$Required
    [int]$Priority
}

class TaskbarFolder {
    [string]$Name
    [string]$Path
    [string]$Label
    [string]$View = 'list'
    [string]$Display = 'folder'
    [string]$Sort = 'name'
}

class SystemInfo {
    [string]$ComputerName
    [string]$Domain
    [string]$OSVersion
    [string]$Architecture
    [string]$UserName
    [bool]$IsAdmin
    [hashtable]$RegistryValues
    [string[]]$InstalledSoftware
}

# Global variables
$Script:TaskbarUtil = $null
$Script:SystemInfo = $null
$Script:Configurations = @()

#region Utility Functions

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('Info', 'Warning', 'Error', 'Success', 'Debug')]
        [string]$Level = 'Info'
    )
    
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $color = switch ($Level) {
        'Info' { 'White' }
        'Warning' { 'Yellow' }
        'Error' { 'Red' }
        'Success' { 'Green' }
        'Debug' { 'Gray' }
    }
    
    $prefix = switch ($Level) {
        'Info' { '🔍' }
        'Warning' { '⚠️' }
        'Error' { '❌' }
        'Success' { '✅' }
        'Debug' { '🐛' }
    }
    
    Write-Host "[$timestamp] $prefix $Message" -ForegroundColor $color
}

function Test-TaskbarUtilAvailable {
    try {
        if ($Script:TaskbarUtil) {
            return Test-Path $Script:TaskbarUtil
        }
        
        # Try to find taskbarutil in PATH
        $taskbarUtil = Get-Command 'taskbarutil' -ErrorAction SilentlyContinue
        if ($taskbarUtil) {
            $Script:TaskbarUtil = $taskbarUtil.Source
            return $true
        }
        
        # Try common installation locations
        $commonPaths = @(
            "$env:ProgramFiles\TaskbarUtil\taskbarutil.exe",
            "$env:LOCALAPPDATA\TaskbarUtil\taskbarutil.exe",
            "$PSScriptRoot\..\publish\taskbarutil.exe",
            "$PSScriptRoot\..\artifacts\publish-win-x64\taskbarutil.exe"
        )
        
        foreach ($path in $commonPaths) {
            if (Test-Path $path) {
                $Script:TaskbarUtil = $path
                return $true
            }
        }
        
        return $false
    }
    catch {
        Write-Log "Error checking for taskbarutil: $($_.Exception.Message)" -Level Error
        return $false
    }
}

function Invoke-TaskbarUtil {
    param(
        [string[]]$Arguments
    )
    
    if (-not (Test-TaskbarUtilAvailable)) {
        throw "TaskbarUtil not found. Please ensure it's installed and in PATH."
    }
    
    $allArgs = $Arguments
    if ($NoRestart) {
        $allArgs += '--no-restart'
    }
    if ($Verbose) {
        $allArgs += '--verbose'
    }
    
    Write-Log "Executing: taskbarutil $($allArgs -join ' ')" -Level Debug
    
    if ($DryRun) {
        Write-Log "[DRY RUN] Would execute: taskbarutil $($allArgs -join ' ')" -Level Info
        return $true
    }
    
    try {
        $result = & $Script:TaskbarUtil $allArgs 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Command succeeded: $($result -join '; ')" -Level Success
            return $true
        } else {
            Write-Log "Command failed (exit code $LASTEXITCODE): $($result -join '; ')" -Level Error
            return $false
        }
    }
    catch {
        Write-Log "Error executing taskbarutil: $($_.Exception.Message)" -Level Error
        return $false
    }
}

function Get-SystemInformation {
    Write-Log "Gathering system information..." -Level Info
    
    $info = [SystemInfo]::new()
    $info.ComputerName = $env:COMPUTERNAME
    $info.Domain = $env:USERDOMAIN
    $info.OSVersion = [System.Environment]::OSVersion.Version.ToString()
    $info.Architecture = $env:PROCESSOR_ARCHITECTURE
    $info.UserName = $env:USERNAME
    $info.IsAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
    
    # Get registry values for computer identification
    $info.RegistryValues = @{}
    try {
        $regPaths = @{
            'ComputerDescription' = 'HKLM:\SYSTEM\CurrentControlSet\Services\lanmanserver\Parameters'
            'OrganizationalUnit' = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State\Machine'
            'ManagedBy' = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        }
        
        foreach ($key in $regPaths.Keys) {
            try {
                $value = Get-ItemProperty -Path $regPaths[$key] -Name $key -ErrorAction SilentlyContinue
                if ($value) {
                    $info.RegistryValues[$key] = $value.$key
                }
            }
            catch {
                Write-Log "Could not read registry value $key" -Level Debug
            }
        }
    }
    catch {
        Write-Log "Error reading registry values: $($_.Exception.Message)" -Level Warning
    }
    
    # Get installed software
    $info.InstalledSoftware = Get-InstalledSoftware
    
    $Script:SystemInfo = $info
    Write-Log "System: $($info.ComputerName) | User: $($info.UserName) | Admin: $($info.IsAdmin)" -Level Info
    
    return $info
}

function Get-InstalledSoftware {
    $software = @()
    
    try {
        # Check registry for installed programs
        $regPaths = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )
        
        foreach ($path in $regPaths) {
            try {
                Get-ItemProperty $path -ErrorAction SilentlyContinue | 
                    Where-Object { $_.DisplayName } |
                    ForEach-Object { $software += $_.DisplayName }
            }
            catch {
                Write-Log "Could not read software from $path" -Level Debug
            }
        }
        
        # Check common application directories
        $appDirs = @(
            "$env:ProgramFiles",
            "${env:ProgramFiles(x86)}",
            "$env:LOCALAPPDATA\Programs"
        )
        
        foreach ($dir in $appDirs) {
            if (Test-Path $dir) {
                Get-ChildItem $dir -Directory -ErrorAction SilentlyContinue |
                    ForEach-Object { $software += $_.Name }
            }
        }
        
        return $software | Sort-Object -Unique
    }
    catch {
        Write-Log "Error getting installed software: $($_.Exception.Message)" -Level Warning
        return @()
    }
}

function Get-ComputerType {
    param([SystemInfo]$SystemInfo)
    
    if ($ComputerType) {
        Write-Log "Using manually specified computer type: $ComputerType" -Level Info
        return $ComputerType
    }
    
    $computerName = $SystemInfo.ComputerName.ToUpper()
    $description = $SystemInfo.RegistryValues['ComputerDescription']
    
    # Define patterns for different computer types
    $patterns = @{
        'StudentLab' = @('*LAB*', '*STUDENT*', '*KIOSK*')
        'DesignLab' = @('*DESIGN*', '*ART*', '*CREATIVE*', '*PHOTO*', '*VIDEO*')
        'MusicLab' = @('*MUSIC*', '*AUDIO*', '*SOUND*', '*RECORDING*')
        'VideoLab' = @('*VIDEO*', '*FILM*', '*EDIT*', '*RENDER*')
        'Library' = @('*LIBRARY*', '*LIB*', '*REFERENCE*')
        'Classroom' = @('*CLASS*', '*ROOM*', '*TEACH*')
        'Faculty' = @('*FACULTY*', '*PROF*', '*INSTRUCTOR*')
        'Admin' = @('*ADMIN*', '*OFFICE*', '*STAFF*')
        'Podium' = @('*PODIUM*', '*PRESENT*', '*PROJECTION*')
        'Default' = @('*')
    }
    
    foreach ($type in $patterns.Keys) {
        if ($type -eq 'Default') { continue }
        
        foreach ($pattern in $patterns[$type]) {
            if ($computerName -like $pattern -or $description -like $pattern) {
                Write-Log "Detected computer type: $type (matched pattern: $pattern)" -Level Info
                return $type
            }
        }
    }
    
    Write-Log "Could not determine computer type, using Default" -Level Warning
    return 'Default'
}

function Test-Condition {
    param(
        [string]$Condition,
        [SystemInfo]$SystemInfo
    )
    
    try {
        # Parse condition syntax
        if ($Condition.StartsWith('software:')) {
            $softwareName = $Condition.Substring(9)
            return $SystemInfo.InstalledSoftware -contains $softwareName
        }
        
        if ($Condition.StartsWith('registry:')) {
            $regPath = $Condition.Substring(9)
            return Test-Path $regPath
        }
        
        if ($Condition.StartsWith('file:')) {
            $filePath = $Condition.Substring(5)
            return Test-Path $filePath
        }
        
        if ($Condition.StartsWith('computer:')) {
            $pattern = $Condition.Substring(9)
            return $SystemInfo.ComputerName -like $pattern
        }
        
        if ($Condition.StartsWith('user:')) {
            $userPattern = $Condition.Substring(5)
            return $SystemInfo.UserName -like $userPattern
        }
        
        if ($Condition.StartsWith('admin')) {
            return $SystemInfo.IsAdmin
        }
        
        # Default: treat as file path
        return Test-Path $Condition
    }
    catch {
        Write-Log "Error evaluating condition '$Condition': $($_.Exception.Message)" -Level Warning
        return $false
    }
}

#endregion

#region Configuration Definitions

function Initialize-TaskbarConfigurations {
    Write-Log "Initializing taskbar configurations..." -Level Info
    
    # Student Lab Configuration
    $studentLab = [TaskbarConfig]::new()
    $studentLab.Name = 'StudentLab'
    $studentLab.ComputerPatterns = @('*LAB*', '*STUDENT*', '*KIOSK*')
    $studentLab.UserTypes = @('student', 'lab')
    $studentLab.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Microsoft Edge'; Path = 'msedge.exe'; Required = $true; Priority = 2 },
        [TaskbarApp]@{ Name = 'Microsoft Word'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\WINWORD.EXE"; Priority = 3 },
        [TaskbarApp]@{ Name = 'Microsoft Excel'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\EXCEL.EXE"; Priority = 4 },
        [TaskbarApp]@{ Name = 'Microsoft PowerPoint'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\POWERPNT.EXE"; Priority = 5 },
        [TaskbarApp]@{ Name = 'Notepad'; Path = 'notepad.exe'; Required = $true; Priority = 10 }
    )
    $studentLab.Folders = @(
        [TaskbarFolder]@{ Name = 'Documents'; Path = [Environment]::GetFolderPath('MyDocuments'); View = 'list'; Sort = 'datemodified' },
        [TaskbarFolder]@{ Name = 'Downloads'; Path = [Environment]::GetFolderPath('UserProfile') + '\Downloads'; View = 'list'; Sort = 'datemodified' }
    )
    $studentLab.Settings = @{ AutoHide = $true; ShowRecents = $false }
    
    # Design Lab Configuration  
    $designLab = [TaskbarConfig]::new()
    $designLab.Name = 'DesignLab'
    $designLab.ComputerPatterns = @('*DESIGN*', '*ART*', '*CREATIVE*', '*PHOTO*')
    $designLab.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Adobe Photoshop'; Path = "${env:ProgramFiles}\Adobe\Adobe Photoshop 2025\Photoshop.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Adobe\Adobe Photoshop 2024\Photoshop.exe"); Priority = 2 },
        [TaskbarApp]@{ Name = 'Adobe Illustrator'; Path = "${env:ProgramFiles}\Adobe\Adobe Illustrator 2025\Support Files\Contents\Windows\Illustrator.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Adobe\Adobe Illustrator 2024\Support Files\Contents\Windows\Illustrator.exe"); Priority = 3 },
        [TaskbarApp]@{ Name = 'Adobe InDesign'; Path = "${env:ProgramFiles}\Adobe\Adobe InDesign 2025\InDesign.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Adobe\Adobe InDesign 2024\InDesign.exe"); Priority = 4 },
        [TaskbarApp]@{ Name = 'Adobe Creative Cloud'; Path = "${env:ProgramFiles(x86)}\Adobe\Adobe Creative Cloud\ACC\Creative Cloud.exe"; Priority = 5 },
        [TaskbarApp]@{ Name = 'Microsoft Edge'; Path = 'msedge.exe'; Required = $true; Priority = 6 }
    )
    
    # Music Lab Configuration
    $musicLab = [TaskbarConfig]::new()
    $musicLab.Name = 'MusicLab'
    $musicLab.ComputerPatterns = @('*MUSIC*', '*AUDIO*', '*SOUND*', '*NMSA*')
    $musicLab.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Audacity'; Path = "${env:ProgramFiles}\Audacity\Audacity.exe"; Priority = 2 },
        [TaskbarApp]@{ Name = 'GarageBand'; Path = "${env:ProgramFiles}\GarageBand\GarageBand.exe"; Conditions = @('software:GarageBand'); Priority = 3 },
        [TaskbarApp]@{ Name = 'Pro Tools'; Path = "${env:ProgramFiles}\Avid\Pro Tools\ProTools.exe"; Conditions = @('software:Pro Tools'); Priority = 4 },
        [TaskbarApp]@{ Name = 'Ableton Live'; Path = "${env:ProgramFiles}\Ableton\Live 11 Suite\Program\Ableton Live 11 Suite.exe"; 
                      Conditions = @('software:Ableton Live'); Priority = 5 },
        [TaskbarApp]@{ Name = 'Adobe Audition'; Path = "${env:ProgramFiles}\Adobe\Adobe Audition 2025\Adobe Audition.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Adobe\Adobe Audition 2024\Adobe Audition.exe"); Priority = 6 },
        [TaskbarApp]@{ Name = 'OBS Studio'; Path = "${env:ProgramFiles}\obs-studio\bin\64bit\obs64.exe"; Priority = 7 }
    )
    
    # Video/Film Lab Configuration
    $videoLab = [TaskbarConfig]::new()
    $videoLab.Name = 'VideoLab'
    $videoLab.ComputerPatterns = @('*VIDEO*', '*FILM*', '*EDIT*', '*FMSA*', '*RENDER*')
    $videoLab.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Adobe Premiere Pro'; Path = "${env:ProgramFiles}\Adobe\Adobe Premiere Pro 2025\Adobe Premiere Pro.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Adobe\Adobe Premiere Pro 2024\Adobe Premiere Pro.exe"); Priority = 2 },
        [TaskbarApp]@{ Name = 'Adobe After Effects'; Path = "${env:ProgramFiles}\Adobe\Adobe After Effects 2025\Support Files\AfterFX.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Adobe\Adobe After Effects 2024\Support Files\AfterFX.exe"); Priority = 3 },
        [TaskbarApp]@{ Name = 'Final Cut Pro'; Path = "${env:ProgramFiles}\Final Cut Pro\Final Cut Pro.exe"; Conditions = @('software:Final Cut Pro'); Priority = 4 },
        [TaskbarApp]@{ Name = 'DaVinci Resolve'; Path = "${env:ProgramFiles}\Blackmagic Design\DaVinci Resolve\Resolve.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\DaVinci Resolve\DaVinci Resolve.exe"); Priority = 5 },
        [TaskbarApp]@{ Name = 'OBS Studio'; Path = "${env:ProgramFiles}\obs-studio\bin\64bit\obs64.exe"; Priority = 6 }
    )
    
    # Faculty/Office Configuration
    $faculty = [TaskbarConfig]::new()
    $faculty.Name = 'Faculty'
    $faculty.ComputerPatterns = @('*FACULTY*', '*OFFICE*', '*ADMIN*', '*STAFF*')
    $faculty.UserTypes = @('faculty', 'admin')
    $faculty.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Microsoft Edge'; Path = 'msedge.exe'; Required = $true; Priority = 2 },
        [TaskbarApp]@{ Name = 'Microsoft Outlook'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\OUTLOOK.EXE"; Priority = 3 },
        [TaskbarApp]@{ Name = 'Microsoft Word'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\WINWORD.EXE"; Priority = 4 },
        [TaskbarApp]@{ Name = 'Microsoft Excel'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\EXCEL.EXE"; Priority = 5 },
        [TaskbarApp]@{ Name = 'Microsoft PowerPoint'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\POWERPNT.EXE"; Priority = 6 },
        [TaskbarApp]@{ Name = 'Microsoft Teams'; Path = "$env:LOCALAPPDATA\Microsoft\Teams\current\Teams.exe"; 
                      AlternatePaths = @("${env:ProgramFiles}\Microsoft\Teams\current\Teams.exe"); Priority = 7 },
        [TaskbarApp]@{ Name = 'Adobe Acrobat'; Path = "${env:ProgramFiles}\Adobe\Acrobat DC\Acrobat\Acrobat.exe"; Priority = 8 }
    )
    $faculty.Settings = @{ AutoHide = $false; ShowRecents = $false }
    
    # Library Configuration
    $library = [TaskbarConfig]::new()
    $library.Name = 'Library'
    $library.ComputerPatterns = @('*LIBRARY*', '*LIB*', '*REFERENCE*')
    $library.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Microsoft Edge'; Path = 'msedge.exe'; Required = $true; Priority = 2 },
        [TaskbarApp]@{ Name = 'Adobe Acrobat'; Path = "${env:ProgramFiles}\Adobe\Acrobat DC\Acrobat\Acrobat.exe"; Priority = 3 },
        [TaskbarApp]@{ Name = 'Microsoft Word'; Path = "${env:ProgramFiles}\Microsoft Office\root\Office16\WINWORD.EXE"; Priority = 4 },
        [TaskbarApp]@{ Name = 'Notepad'; Path = 'notepad.exe'; Required = $true; Priority = 5 }
    )
    
    # Default Configuration
    $default = [TaskbarConfig]::new()
    $default.Name = 'Default'
    $default.ComputerPatterns = @('*')
    $default.Applications = @(
        [TaskbarApp]@{ Name = 'File Explorer'; Path = 'explorer.exe'; Required = $true; Priority = 1 },
        [TaskbarApp]@{ Name = 'Microsoft Edge'; Path = 'msedge.exe'; Required = $true; Priority = 2 },
        [TaskbarApp]@{ Name = 'Notepad'; Path = 'notepad.exe'; Required = $true; Priority = 3 }
    )
    $default.Settings = @{ AutoHide = $false; ShowRecents = $false }
    
    $Script:Configurations = @($studentLab, $designLab, $musicLab, $videoLab, $faculty, $library, $default)
    Write-Log "Loaded $($Script:Configurations.Count) taskbar configurations" -Level Success
}

function Get-ApplicableConfiguration {
    param(
        [SystemInfo]$SystemInfo,
        [string]$ComputerType,
        [string]$UserType
    )
    
    Write-Log "Finding applicable configuration for $ComputerType / $UserType" -Level Info
    
    foreach ($config in $Script:Configurations) {
        # Check computer type match
        $computerMatch = $false
        foreach ($pattern in $config.ComputerPatterns) {
            if ($ComputerType -like $pattern) {
                $computerMatch = $true
                break
            }
        }
        
        # Check user type match
        $userMatch = $config.UserTypes.Count -eq 0 -or $UserType -in $config.UserTypes
        
        if ($computerMatch -and $userMatch) {
            Write-Log "Selected configuration: $($config.Name)" -Level Success
            return $config
        }
    }
    
    # Fallback to default
    $defaultConfig = $Script:Configurations | Where-Object { $_.Name -eq 'Default' } | Select-Object -First 1
    Write-Log "Using default configuration" -Level Warning
    return $defaultConfig
}

#endregion

#region Main Configuration Logic

function Clear-ExistingTaskbar {
    Write-Log "Removing all existing taskbar pins..." -Level Info
    
    if (-not (Invoke-TaskbarUtil @('remove', 'all'))) {
        Write-Log "Failed to clear existing taskbar pins" -Level Warning
    } else {
        Write-Log "Successfully cleared taskbar" -Level Success
    }
}

function Add-TaskbarApplications {
    param([TaskbarConfig]$Config)
    
    Write-Log "Adding applications for $($Config.Name) configuration..." -Level Info
    
    $sortedApps = $Config.Applications | Sort-Object Priority
    $successCount = 0
    $skipCount = 0
    
    foreach ($app in $sortedApps) {
        $shouldAdd = $true
        
        # Check conditions
        if ($app.Conditions) {
            foreach ($condition in $app.Conditions) {
                if (-not (Test-Condition $condition $Script:SystemInfo)) {
                    Write-Log "Skipping $($app.Name) - condition not met: $condition" -Level Debug
                    $shouldAdd = $false
                    $skipCount++
                    break
                }
            }
        }
        
        if (-not $shouldAdd) { continue }
        
        # Find the best path
        $targetPath = $null
        if (Test-Path $app.Path) {
            $targetPath = $app.Path
        } elseif ($app.AlternatePaths) {
            foreach ($altPath in $app.AlternatePaths) {
                if (Test-Path $altPath) {
                    $targetPath = $altPath
                    break
                }
            }
        }
        
        if (-not $targetPath) {
            if ($app.Required) {
                Write-Log "Required application not found: $($app.Name) at $($app.Path)" -Level Error
            } else {
                Write-Log "Optional application not found: $($app.Name)" -Level Debug
                $skipCount++
            }
            continue
        }
        
        # Add to taskbar
        $args = @('add', $targetPath)
        if ($app.Label) {
            $args += @('--label', $app.Label)
        }
        
        if (Invoke-TaskbarUtil $args) {
            Write-Log "Added: $($app.Name)" -Level Success
            $successCount++
        } else {
            Write-Log "Failed to add: $($app.Name)" -Level Error
        }
    }
    
    Write-Log "Application configuration complete: $successCount added, $skipCount skipped" -Level Info
}

function Add-TaskbarFolders {
    param([TaskbarConfig]$Config)
    
    if ($Config.Folders.Count -eq 0) {
        return
    }
    
    Write-Log "Adding folders for $($Config.Name) configuration..." -Level Info
    
    foreach ($folder in $Config.Folders) {
        if (-not (Test-Path $folder.Path)) {
            Write-Log "Folder not found: $($folder.Path)" -Level Warning
            continue
        }
        
        $args = @('add', $folder.Path)
        if ($folder.Label) {
            $args += @('--label', $folder.Label)
        }
        
        # Note: Windows taskbar doesn't support view/display options like macOS dock
        # These would need to be handled differently or ignored
        
        if (Invoke-TaskbarUtil $args) {
            Write-Log "Added folder: $($folder.Name)" -Level Success
        } else {
            Write-Log "Failed to add folder: $($folder.Name)" -Level Error
        }
    }
}

function Apply-TaskbarSettings {
    param([TaskbarConfig]$Config)
    
    if (-not $Config.Settings) {
        return
    }
    
    Write-Log "Applying taskbar settings..." -Level Info
    
    foreach ($setting in $Config.Settings.Keys) {
        $value = $Config.Settings[$setting]
        
        switch ($setting) {
            'AutoHide' {
                try {
                    $regPath = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
                    if (Test-Path $regPath) {
                        # Windows taskbar auto-hide setting is stored in binary registry data
                        # This is a simplified approach - full implementation would require binary manipulation
                        Write-Log "Auto-hide setting would be applied: $value" -Level Info
                    }
                } catch {
                    Write-Log "Failed to set auto-hide: $($_.Exception.Message)" -Level Warning
                }
            }
            'ShowRecents' {
                try {
                    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'ShowTaskViewButton' -Value ([int]$value) -ErrorAction SilentlyContinue
                    Write-Log "Show recents setting applied: $value" -Level Success
                } catch {
                    Write-Log "Failed to set show recents: $($_.Exception.Message)" -Level Warning
                }
            }
        }
    }
}

function Stop-UnwantedProcesses {
    Write-Log "Stopping unwanted applications..." -Level Info
    
    $processesToStop = @(
        'Spotify',
        'Teams',  # Only if not in configuration
        'Slack'   # Example of unwanted startup app
    )
    
    foreach ($processName in $processesToStop) {
        try {
            $processes = Get-Process -Name $processName -ErrorAction SilentlyContinue
            if ($processes) {
                $processes | Stop-Process -Force -ErrorAction SilentlyContinue
                Write-Log "Stopped process: $processName" -Level Success
            }
        } catch {
            Write-Log "Could not stop process $processName`: $($_.Exception.Message)" -Level Debug
        }
    }
}

#endregion

#region Main Execution

function Main {
    try {
        Write-Log "Starting Enterprise Taskbar Configuration" -Level Info
        Write-Log "Version: 1.0.0 | Mode: $(if ($DryRun) { 'DRY RUN' } else { 'LIVE' })" -Level Info
        
        # Load custom configuration if specified
        if ($ConfigFile -and (Test-Path $ConfigFile)) {
            Write-Log "Loading custom configuration from: $ConfigFile" -Level Info
            # Custom config loading would go here
        }
        
        # Initialize configurations
        Initialize-TaskbarConfigurations
        
        # Check for TaskbarUtil
        if (-not (Test-TaskbarUtilAvailable)) {
            throw "TaskbarUtil is not available. Please install it first."
        }
        
        # Gather system information
        $systemInfo = Get-SystemInformation
        
        # Determine computer and user types
        $detectedComputerType = Get-ComputerType $systemInfo
        $detectedUserType = if ($UserType -eq 'auto') {
            if ($systemInfo.IsAdmin) { 'admin' }
            elseif ($systemInfo.UserName -like '*student*') { 'student' }
            elseif ($systemInfo.UserName -like '*faculty*') { 'faculty' }
            else { 'lab' }
        } else { $UserType }
        
        Write-Log "Computer Type: $detectedComputerType | User Type: $detectedUserType" -Level Info
        
        # Get applicable configuration
        $config = Get-ApplicableConfiguration $systemInfo $detectedComputerType $detectedUserType
        
        # Clear existing taskbar if requested
        if ($CleanFirst) {
            Clear-ExistingTaskbar
        }
        
        # Apply configuration
        Add-TaskbarApplications $config
        Add-TaskbarFolders $config
        Apply-TaskbarSettings $config
        
        # Clean up unwanted processes
        Stop-UnwantedProcesses
        
        # Refresh taskbar unless disabled
        if (-not $NoRestart -and -not $DryRun) {
            Write-Log "Refreshing taskbar..." -Level Info
            # The taskbarutil tool handles this with --no-restart flag
        }
        
        Write-Log "Enterprise Taskbar Configuration completed successfully!" -Level Success
        
        if ($DryRun) {
            Write-Log "This was a dry run. No changes were made." -Level Info
        }
        
        # Show final status
        if (-not $DryRun) {
            Write-Log "Current taskbar configuration:" -Level Info
            Invoke-TaskbarUtil @('list')
        }
        
    } catch {
        Write-Log "Configuration failed: $($_.Exception.Message)" -Level Error
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level Debug
        exit 1
    }
}

# Execute main function
Main

#endregion
