# TaskbarUtil Build Script
# Builds native Windows binaries and MSI packages via cimipkg
#
# Usage:
#   .\build.ps1                    # Full build: binaries + MSI + NuGet + zip
#   .\build.ps1 -Sign              # Build and sign with auto-detected cert
#   .\build.ps1 -NoSign            # Build without signing
#   .\build.ps1 -Msi               # Build only MSI packages
#   .\build.ps1 -Nupkg             # Build only NuGet packages
#   .\build.ps1 -Runtime win-x64   # Build only x64
#   .\build.ps1 -Configuration Debug

param(
    [switch]$Build = $false,
    [switch]$Sign = $false,
    [switch]$NoSign = $false,
    [switch]$Msi = $false,
    [switch]$Nupkg = $false,
    [switch]$Clean = $false,
    [switch]$Test = $false,
    [string]$Configuration = "Release",
    [string[]]$Runtime = @("win-x64", "win-arm64"),
    [string]$CertificateName,
    [string]$Thumbprint
)

$ErrorActionPreference = 'Stop'

# Default: full build when no flags provided
if (-not ($Build -or $Msi -or $Nupkg -or $Test)) {
    $Build = $true
    $Msi = $true
    $Nupkg = $true
}

$rootPath = $PSScriptRoot
$projectPath = Join-Path $rootPath "src\TaskbarUtil.csproj"
$distDir = Join-Path $rootPath "dist"
$releaseDir = Join-Path $rootPath "release"
$filesToSign = New-Object System.Collections.Generic.List[string]

$script:SignToolPath = $null
$script:SignToolChecked = $false
$script:SignToolWarned = $false

function Write-Log {
    param (
        [string]$Message,
        [ValidateSet("INFO", "SUCCESS", "WARNING", "ERROR")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    switch ($Level) {
        "INFO"    { Write-Host "[$timestamp] [INFO] $Message" -ForegroundColor Cyan }
        "SUCCESS" { Write-Host "[$timestamp] [SUCCESS] $Message" -ForegroundColor Green }
        "WARNING" { Write-Host "[$timestamp] [WARNING] $Message" -ForegroundColor Yellow }
        "ERROR"   { Write-Host "[$timestamp] [ERROR] $Message" -ForegroundColor Red }
    }
}

function Resolve-SignToolPath {
    if ($script:SignToolChecked) { return $script:SignToolPath }
    $script:SignToolChecked = $true

    $commandLookup = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($commandLookup) { $script:SignToolPath = $commandLookup.Source; return $script:SignToolPath }

    foreach ($envVar in @("SIGNTOOL_PATH", "SIGNTOOL")) {
        $value = [Environment]::GetEnvironmentVariable($envVar)
        if (-not [string]::IsNullOrWhiteSpace($value) -and (Test-Path $value -PathType Leaf)) {
            $script:SignToolPath = (Resolve-Path $value).Path; return $script:SignToolPath
        }
    }

    $kitRoots = @()
    if ($env:ProgramFiles) { $kitRoots += Join-Path $env:ProgramFiles "Windows Kits\10\bin" }
    if (${env:ProgramFiles(x86)}) { $kitRoots += Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin" }

    foreach ($kitRoot in $kitRoots | Where-Object { Test-Path $_ }) {
        $versions = Get-ChildItem -Path $kitRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
        foreach ($versionDir in $versions) {
            foreach ($arch in @("x64", "arm64", "x86")) {
                $exePath = Join-Path $versionDir.FullName "$arch\signtool.exe"
                if (Test-Path $exePath) { $script:SignToolPath = (Resolve-Path $exePath).Path; return $script:SignToolPath }
            }
        }
    }
    return $null
}

function Get-SigningCertThumbprint {
    [OutputType([hashtable])]
    param(
        [string]$Name,
        [string]$ProvidedThumbprint
    )

    # Priority 1: Explicit thumbprint (parameter or env var)
    $tp = if ($ProvidedThumbprint) { $ProvidedThumbprint } else { $env:CERT_THUMBPRINT }
    if ($tp) {
        foreach ($store in @("CurrentUser", "LocalMachine")) {
            $cert = Get-ChildItem "Cert:\$store\My\$tp" -ErrorAction SilentlyContinue
            if ($cert) { return @{ Thumbprint = $cert.Thumbprint; Store = $store; Subject = $cert.Subject } }
        }
    }

    # Resolve certificate CN: parameter > project env var > generic env var
    $certCN = if ($Name) { $Name }
              elseif ($env:TASKBARUTIL_CERT_CN) { $env:TASKBARUTIL_CERT_CN }
              elseif ($env:ENTERPRISE_CERT_CN) { $env:ENTERPRISE_CERT_CN }
              else { '' }
    $certSubject = if ($env:TASKBARUTIL_CERT_SUBJECT) { $env:TASKBARUTIL_CERT_SUBJECT }
                   elseif ($env:ENTERPRISE_CERT_SUBJECT) { $env:ENTERPRISE_CERT_SUBJECT }
                   else { '' }

    # Priority 2: Match by CN from parameter or env var
    if ($certCN) {
        foreach ($store in @("CurrentUser", "LocalMachine")) {
            $cert = Get-ChildItem "Cert:\$store\My" -ErrorAction SilentlyContinue |
                Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and $_.Subject -like "*$certCN*" } |
                Sort-Object NotAfter -Descending | Select-Object -First 1
            if ($cert) { return @{ Thumbprint = $cert.Thumbprint; Store = $store; Subject = $cert.Subject } }
        }
    }

    # Priority 3: Match by subject env var
    if ($certSubject) {
        foreach ($store in @("CurrentUser", "LocalMachine")) {
            $cert = Get-ChildItem "Cert:\$store\My" -ErrorAction SilentlyContinue |
                Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and $_.Subject -like "*$certSubject*" } |
                Sort-Object NotAfter -Descending | Select-Object -First 1
            if ($cert) { return @{ Thumbprint = $cert.Thumbprint; Store = $store; Subject = $cert.Subject } }
        }
    }

    # Priority 4: Enterprise keyword matching (Intune/MDM certs may lack Code Signing EKU)
    $enterpriseKeywords = @("Enterprise", "Intune", "Corporate", "Organization")
    foreach ($store in @("LocalMachine", "CurrentUser")) {
        foreach ($kw in $enterpriseKeywords) {
            $cert = Get-ChildItem "Cert:\$store\My" -ErrorAction SilentlyContinue |
                Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and $_.Subject -like "*$kw*" -and $_.Subject -notmatch "\bTest\b" } |
                Sort-Object NotAfter -Descending | Select-Object -First 1
            if ($cert) { return @{ Thumbprint = $cert.Thumbprint; Store = $store; Subject = $cert.Subject } }
        }
    }

    # Priority 5: Any valid code signing certificate (prefer LocalMachine)
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    foreach ($store in @("LocalMachine", "CurrentUser")) {
        $cert = Get-ChildItem "Cert:\$store\My" -ErrorAction SilentlyContinue |
            Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and $_.Subject -notmatch "\bTest\b" -and ($_.EnhancedKeyUsageList.ObjectId -contains $codeSigningOid) } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if ($cert) { return @{ Thumbprint = $cert.Thumbprint; Store = $store; Subject = $cert.Subject } }
    }

    return $null
}

function Invoke-CodeSign {
    param(
        [Parameter(Mandatory)][string]$TargetFile,
        [string]$CertName,
        [string]$CertThumbprint,
        [int]$MaxAttempts = 4
    )

    $resolvedPath = Resolve-SignToolPath
    if (-not $resolvedPath) {
        if (-not $script:SignToolWarned) {
            Write-Log "Skipping signing: signtool.exe not found. Install Windows SDK or set SIGNTOOL_PATH." "WARNING"
            $script:SignToolWarned = $true
        }
        return $false
    }

    if (-not (Test-Path $TargetFile)) { Write-Log "File not found for signing: $TargetFile" "WARNING"; return $false }

    $tsas = @('http://timestamp.digicert.com', 'http://timestamp.sectigo.com', 'http://timestamp.entrust.net/TSS/RFC3161sha2TS')
    $attempt = 0
    $signed = $false

    while ($attempt -lt $MaxAttempts -and -not $signed) {
        $attempt++
        foreach ($tsa in $tsas) {
            try {
                $signArgs = @("sign", "/fd", "SHA256", "/tr", $tsa, "/td", "SHA256")
                if ($CertThumbprint) { $signArgs += @("/sha1", $CertThumbprint) }
                elseif ($CertName) { $signArgs += @("/n", $CertName) }
                else { $signArgs += "/a" }
                $signArgs += $TargetFile

                & $resolvedPath @signArgs 2>&1 | Out-Null
                if ($LASTEXITCODE -eq 0) { Write-Log "Signed: $TargetFile" "SUCCESS"; $signed = $true; break }
            } catch {}
            if (-not $signed) { Start-Sleep -Seconds (2 * $attempt) }
        }
    }

    if (-not $signed) { Write-Log "Failed to sign after $MaxAttempts attempts: $TargetFile" "WARNING" }
    return $signed
}

function Find-Cimipkg {
    # Check PATH
    $cmd = Get-Command "cimipkg.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # Check tools directory
    $local = Join-Path $rootPath "tools\cimipkg.exe"
    if (Test-Path $local) { return $local }

    # Check Program Files
    $pf = "C:\Program Files\sbin\cimipkg.exe"
    if (Test-Path $pf) { return $pf }

    return $null
}

function Ensure-Cimipkg {
    $path = Find-Cimipkg
    if ($path) { return $path }

    Write-Log "cimipkg not found locally. Downloading from GitHub..." "INFO"
    $toolsDir = Join-Path $rootPath "tools"
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null

    try {
        & gh release download --repo windowsadmins/cimian-pkg --pattern "cimipkg-win-x64.zip" --dir $toolsDir
        Expand-Archive -Path (Join-Path $toolsDir "cimipkg-win-x64.zip") -DestinationPath $toolsDir -Force
        Remove-Item (Join-Path $toolsDir "cimipkg-win-x64.zip") -Force -ErrorAction SilentlyContinue

        $downloaded = Join-Path $toolsDir "cimipkg.exe"
        if (Test-Path $downloaded) {
            Write-Log "Downloaded cimipkg.exe" "SUCCESS"
            return $downloaded
        }
    } catch {
        Write-Log "Failed to download cimipkg: $($_.Exception.Message)" "ERROR"
    }

    throw "cimipkg.exe not found. Install it or place it in the tools/ directory."
}

# --- Signing decision ---

$autoDetectedCert = $null
if (-not $Sign -and -not $NoSign) {
    try {
        $certInfo = Get-SigningCertThumbprint -Name $CertificateName -ProvidedThumbprint $Thumbprint
        if ($certInfo) {
            $autoDetectedCert = $certInfo
            Write-Log "Auto-detected certificate: $($certInfo.Subject) in $($certInfo.Store) store" "INFO"
            $Sign = $true
            if (-not $Thumbprint) { $Thumbprint = $certInfo.Thumbprint }
        } else {
            Write-Log "No code signing certificate found - binaries will be unsigned." "WARNING"
        }
    } catch {
        Write-Log "Could not check for certificates: $_" "WARNING"
    }
}
if ($NoSign) { Write-Log "NoSign specified - skipping all signing." "INFO"; $Sign = $false }
if ($Sign -and -not (Resolve-SignToolPath)) {
    Write-Log "Signing requested but signtool.exe not found. Install Windows SDK." "ERROR"
    exit 1
}

# --- Clean ---

if ($Clean) {
    Write-Log "Cleaning..." "INFO"
    foreach ($dir in @($distDir, $releaseDir, "staging-*")) {
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    }
}

# --- Build ---

if ($Build) {
    Write-Log "Building TaskbarUtil ($Configuration)..." "INFO"

    if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
        Write-Log ".NET SDK not found. Install from https://dotnet.microsoft.com/download" "ERROR"; exit 1
    }

    # Run tests first
    if ($Test) {
        Write-Log "Running tests..." "INFO"
        & dotnet test (Join-Path $rootPath "tests\TaskbarUtil.Tests.csproj") --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) { Write-Log "Tests failed" "ERROR"; exit 1 }
        Write-Log "All tests passed" "SUCCESS"
    }

    $version = Get-Date -Format "yyyy.MM.dd.HHmm"

    foreach ($rid in $Runtime) {
        Write-Log "Publishing for $rid..." "INFO"

        & dotnet publish $projectPath `
            -c $Configuration -r $rid --self-contained `
            -p:PublishSingleFile=true `
            -p:PublishTrimmed=true `
            -p:TrimMode=partial `
            -p:Version=$version

        if ($LASTEXITCODE -ne 0) { Write-Log "Failed to publish for $rid" "ERROR"; exit 1 }

        $arch = if ($rid -match 'x64') { 'x64' } elseif ($rid -match 'arm64') { 'arm64' } else { $rid }
        $archDir = Join-Path $distDir $arch
        New-Item -ItemType Directory -Path $archDir -Force | Out-Null

        $publishDir = Join-Path $rootPath "src\bin\$Configuration\net10.0-windows10.0.22621.0\$rid\publish"
        $builtExe = Join-Path $publishDir "taskbarutil.exe"

        if (-not (Test-Path $builtExe)) { Write-Log "taskbarutil.exe not found in publish output for $rid" "ERROR"; exit 1 }

        $destPath = Join-Path $archDir "taskbarutil.exe"
        Copy-Item $builtExe $destPath -Force
        $filesToSign.Add($destPath)

        $sizeMB = '{0:N2}' -f ((Get-Item $destPath).Length / 1MB)
        Write-Log "Built: $arch\taskbarutil.exe ($sizeMB MB)" "SUCCESS"
    }

    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    Start-Sleep -Seconds 2
}

# --- Sign ---

if ($Sign -and $filesToSign.Count -gt 0) {
    Write-Log "Signing artifacts..." "INFO"
    foreach ($file in $filesToSign | Sort-Object -Unique) {
        Invoke-CodeSign -TargetFile $file -CertThumbprint $Thumbprint -CertName $CertificateName
    }
}

# --- MSI Package (cimipkg) ---

if ($Msi) {
    $cimipkg = Ensure-Cimipkg
    $version = Get-Date -Format "yyyy.MM.dd.HHmm"
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

    foreach ($arch in @("x64", "arm64")) {
        $exePath = Join-Path $distDir "$arch\taskbarutil.exe"
        if (-not (Test-Path $exePath)) { Write-Log "Binary not found: $exePath - skipping MSI" "WARNING"; continue }

        Write-Log "Building MSI for $arch..." "INFO"
        $stagingDir = Join-Path $rootPath "staging-$arch"

        New-Item -ItemType Directory -Path "$stagingDir\payload" -Force | Out-Null
        New-Item -ItemType Directory -Path "$stagingDir\scripts" -Force | Out-Null

        Copy-Item $exePath "$stagingDir\payload\" -Force

        (Get-Content "build\pkg\build-info.yaml" -Raw) `
            -replace '\{\{VERSION\}\}', $version `
            -replace '\{\{ARCHITECTURE\}\}', $arch |
            Set-Content "$stagingDir\build-info.yaml" -Encoding UTF8

        (Get-Content "build\pkg\preinstall.ps1" -Raw) `
            -replace '\{\{VERSION\}\}', $version |
            Set-Content "$stagingDir\scripts\preinstall.ps1" -Encoding UTF8

        (Get-Content "build\pkg\postinstall.ps1" -Raw) `
            -replace '\{\{VERSION\}\}', $version |
            Set-Content "$stagingDir\scripts\postinstall.ps1" -Encoding UTF8

        & $cimipkg --verbose $stagingDir
        if ($LASTEXITCODE -ne 0) { Write-Log "cimipkg MSI build failed for $arch" "ERROR"; exit 1 }

        $msiFile = Get-ChildItem "$stagingDir\build\*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($msiFile) {
            $dest = Join-Path $releaseDir "TaskbarUtil-$arch-$version.msi"
            Move-Item $msiFile.FullName $dest -Force
            $sizeMB = '{0:N2}' -f ((Get-Item $dest).Length / 1MB)
            Write-Log "MSI created: TaskbarUtil-$arch-$version.msi ($sizeMB MB)" "SUCCESS"

            if ($Sign) {
                Write-Log "Signing MSI: $(Split-Path $dest -Leaf)" "INFO"
                Invoke-CodeSign -TargetFile $dest -CertThumbprint $Thumbprint -CertName $CertificateName
            }
        } else {
            Write-Log "MSI output not found for $arch" "ERROR"; exit 1
        }

        Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- NuGet Package (cimipkg) ---

if ($Nupkg) {
    $cimipkg = Ensure-Cimipkg
    $version = Get-Date -Format "yyyy.MM.dd.HHmm"
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

    foreach ($arch in @("x64", "arm64")) {
        $stagingDir = Join-Path $rootPath "staging-nupkg-$arch"
        $exePath = Join-Path $distDir "$arch\taskbarutil.exe"
        if (-not (Test-Path $exePath)) { Write-Log "Binary not found: $exePath - skipping NuGet" "WARNING"; continue }

        # Rebuild staging for nupkg
        New-Item -ItemType Directory -Path "$stagingDir\payload" -Force | Out-Null
        New-Item -ItemType Directory -Path "$stagingDir\scripts" -Force | Out-Null
        Copy-Item $exePath "$stagingDir\payload\" -Force

        (Get-Content "build\pkg\build-info.yaml" -Raw) `
            -replace '\{\{VERSION\}\}', $version `
            -replace '\{\{ARCHITECTURE\}\}', $arch |
            Set-Content "$stagingDir\build-info.yaml" -Encoding UTF8

        Write-Log "Building NuGet package for $arch..." "INFO"
        & $cimipkg --nupkg --verbose $stagingDir
        if ($LASTEXITCODE -ne 0) { Write-Log "cimipkg NuGet build failed for $arch" "WARNING"; continue }

        $nupkgFile = Get-ChildItem "$stagingDir\build\*.nupkg" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($nupkgFile) {
            $dest = Join-Path $releaseDir "TaskbarUtil-$arch-$version.nupkg"
            Move-Item $nupkgFile.FullName $dest -Force
            Write-Log "NuGet package created: TaskbarUtil-$arch-$version.nupkg" "SUCCESS"
        } else {
            Write-Log "NuGet output not found for $arch, skipping" "WARNING"
        }

        Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# --- Zip standalone binaries ---

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
foreach ($arch in @("x64", "arm64")) {
    $exePath = Join-Path $distDir "$arch\taskbarutil.exe"
    if (Test-Path $exePath) {
        $zipPath = Join-Path $releaseDir "taskbarutil-$arch.zip"
        Compress-Archive -Path $exePath -DestinationPath $zipPath -Force
        Write-Log "Created: taskbarutil-$arch.zip" "SUCCESS"
    }
}

# --- Clean old releases ---

if (Test-Path $releaseDir) {
    foreach ($pattern in @("TaskbarUtil-*-*.msi", "TaskbarUtil-*-*.nupkg")) {
        $files = Get-ChildItem $releaseDir -Filter $pattern | Sort-Object Name -Descending
        $kept = @{}
        foreach ($file in $files) {
            # Group key: everything before the version stamp (e.g. "TaskbarUtil-x64")
            if ($file.BaseName -match '^(.+)-\d{4}\.\d{2}\.\d{2}\.\d{4}$') {
                $key = $Matches[1]
                if ($kept.ContainsKey($key)) {
                    Remove-Item $file.FullName -Force
                } else {
                    $kept[$key] = $true
                }
            }
        }
    }
}

# --- Summary ---

Write-Log "" "INFO"
Write-Log "=== BUILD COMPLETE ===" "SUCCESS"
if (Test-Path $releaseDir) {
    Get-ChildItem $releaseDir | ForEach-Object {
        $sizeMB = '{0:N2}' -f ($_.Length / 1MB)
        Write-Log "  $($_.Name) ($sizeMB MB)" "INFO"
    }
}
