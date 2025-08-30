# TaskbarUtil Build Script
# Builds and optionally signs the TaskbarUtil executable for deployment

[CmdletBinding()]
param(
    [switch]$Sign,
    [string]$Thumbprint,
    [ValidateSet("x64", "arm64", "both")]
    [string]$Architecture = "x64",
    [switch]$Clean,
    [switch]$Test
)

$ErrorActionPreference = "Stop"

# Enterprise Certificate Configuration
$Global:EnterpriseCertCN = 'EmilyCarrU Intune Windows Enterprise Certificate'

Write-Host "=== TaskbarUtil Build Script ===" -ForegroundColor Magenta
Write-Host "Architecture: $Architecture" -ForegroundColor Yellow
Write-Host "Code Signing: $Sign" -ForegroundColor Yellow
Write-Host "Clean Build: $Clean" -ForegroundColor Yellow
Write-Host ""

# Function to display messages with different log levels
function Write-Log {
    param (
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    switch ($Level) {
        "INFO"    { Write-Host "[$timestamp] [INFO] $Message" -ForegroundColor White }
        "WARN"    { Write-Host "[$timestamp] [WARN] $Message" -ForegroundColor Yellow }
        "ERROR"   { Write-Host "[$timestamp] [ERROR] $Message" -ForegroundColor Red }
        "SUCCESS" { Write-Host "[$timestamp] [SUCCESS] $Message" -ForegroundColor Green }
    }
}

# Function to check if a command exists
function Test-Command {
    param([string]$Command)
    return [bool](Get-Command $Command -ErrorAction SilentlyContinue)
}

# Function to ensure signtool is available
function Test-SignTool {
    $c = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($c) { return }
    $roots = @(
        "$env:ProgramFiles\Windows Kits\10\bin",
        "$env:ProgramFiles(x86)\Windows Kits\10\bin"
    ) | Where-Object { Test-Path $_ }

    try {
        $kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -EA Stop).KitsRoot10
        if ($kitsRoot) { $roots += (Join-Path $kitsRoot 'bin') }
    } catch {}

    foreach ($root in $roots) {
        $cand = Get-ChildItem -Path (Join-Path $root '*\x64\signtool.exe') -EA SilentlyContinue |
                Sort-Object LastWriteTime -Desc | Select-Object -First 1
        if ($cand) {
            $env:Path = "$($cand.Directory.FullName);$env:Path"
            return
        }
    }
    throw "signtool.exe not found. Install Windows 10/11 SDK (Signing Tools)."
}

# Function to find signing certificate
function Get-SigningCertificate {
    param([string]$Thumbprint = $null)
    
    if ($Thumbprint) {
        $cert = Get-ChildItem -Path "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
        if ($cert) {
            return $cert
        }
        Write-Log "Certificate with thumbprint $Thumbprint not found" "WARN"
    }
    
    # Search for enterprise certificate by common name
    $cert = Get-ChildItem -Path "Cert:\CurrentUser\My\" | Where-Object {
        $_.Subject -like "*$Global:EnterpriseCertCN*"
    } | Select-Object -First 1
    
    if ($cert) {
        Write-Log "Found enterprise certificate: $($cert.Subject)" "SUCCESS"
        Write-Log "Thumbprint: $($cert.Thumbprint)" "INFO"
        
        return $cert
    }
    
    Write-Log "No suitable signing certificate found" "WARN"
    return $null
}

# Function to sign executable with robust retry and multiple timestamp servers
function Invoke-SignArtifact {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Thumbprint,
        [int]$MaxAttempts = 4
    )

    if (-not (Test-Path -LiteralPath $Path)) { throw "File not found: $Path" }

    $tsas = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://timestamp.entrust.net/TSS/RFC3161sha2TS'
    )

    $attempt = 0
    while ($attempt -lt $MaxAttempts) {
        $attempt++
        foreach ($tsa in $tsas) {
            & signtool.exe sign `
                /sha1 $Thumbprint `
                /fd SHA256 `
                /td SHA256 `
                /tr $tsa `
                /v `
                "$Path"
            $code = $LASTEXITCODE

            if ($code -eq 0) {
                # Optional append of legacy timestamp for old verifiers; harmless if TSA rejects.
                & signtool.exe timestamp /t http://timestamp.digicert.com /v "$Path" 2>$null
                return
            }

            Start-Sleep -Seconds (4 * $attempt)
        }
    }

    throw "Signing failed after $MaxAttempts attempts across TSAs: $Path"
}

# Function to build for specific architecture
function Build-Architecture {
    param(
        [string]$Arch,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$SigningCert = $null
    )
    
    Write-Log "Building for $Arch architecture..." "INFO"
    
    $outputDir = "publish\$Arch"
    
    if ($Clean -and (Test-Path $outputDir)) {
        Write-Log "Cleaning output directory: $outputDir" "INFO"
        Remove-Item -Path $outputDir -Recurse -Force
    }
    
    # Ensure output directory exists
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    # Build arguments
    $buildArgs = @(
        "publish"
        "src\TaskbarUtil.csproj"
        "--configuration", "Release"
        "--runtime", "win-$Arch"
        "--output", $outputDir
        "--self-contained", "true"
        "--verbosity", "minimal"
    )
    
    try {
        Write-Log "Running: dotnet $($buildArgs -join ' ')" "INFO"
        & dotnet @buildArgs
        
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code: $LASTEXITCODE"
        }
        
        $executablePath = Join-Path $outputDir "taskbarutil.exe"
        
        if (-not (Test-Path $executablePath)) {
            throw "Expected executable not found: $executablePath"
        }
        
        # Convert to absolute path for signing
        $executablePath = (Get-Item $executablePath).FullName
        
        $fileInfo = Get-Item $executablePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Log "Build successful: $($fileInfo.Name) ($sizeMB MB)" "SUCCESS"
        
        # Sign the executable if certificate is provided
        if ($SigningCert) {
            # Check for ARM64 system building x64 - fix ownership issue
            $isARM64System = (Get-WmiObject -Class Win32_Processor | Select-Object -First 1).Architecture -eq 12
            if ($isARM64System -and $Arch -eq "x64") {
                Write-Log "ARM64 system detected - fixing x64 binary ownership for signing..." "INFO"
                try {
                    & takeown /f $executablePath | Out-Null
                    if ($LASTEXITCODE -eq 0) {
                        Write-Log "Fixed x64 binary ownership" "SUCCESS"
                    }
                } catch {
                    Write-Log "Could not fix ownership, but continuing..." "WARN"
                }
            }
            
            if (Invoke-SignArtifact -Path $executablePath -Thumbprint $SigningCert.Thumbprint) {
                Write-Log "Code signing completed for $Arch" "SUCCESS"
            } else {
                Write-Log "Code signing failed for $Arch" "ERROR"
                return $false
            }
        } else {
            Write-Log "Skipping code signing (no certificate)" "WARN"
        }
        
        return $true
        
    } catch {
        Write-Log "Build failed for $Arch`: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

# Function to run basic tests
function Test-Build {
    param([string]$ExecutablePath)
    
    Write-Log "Testing build: $ExecutablePath" "INFO"
    
    if (-not (Test-Path $ExecutablePath)) {
        Write-Log "Executable not found for testing: $ExecutablePath" "ERROR"
        return $false
    }
    
    try {
        # Test version output
        Write-Log "Testing --version command..." "INFO"
        $versionOutput = & $ExecutablePath --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Version test passed: $versionOutput" "SUCCESS"
        } else {
            Write-Log "Version test failed with exit code: $LASTEXITCODE" "WARN"
        }
        
        # Test help output  
        Write-Log "Testing --help command..." "INFO"
        $null = & $ExecutablePath --help 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Help test passed" "SUCCESS"
        } else {
            Write-Log "Help test failed with exit code: $LASTEXITCODE" "WARN"
        }
        
        return $true
        
    } catch {
        Write-Log "Testing failed: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

# Main build process
try {
    $rootPath = $PSScriptRoot
    Push-Location $rootPath
    
    # Prerequisites check
    Write-Log "Checking prerequisites..." "INFO"
    
    if (-not (Test-Command "dotnet")) {
        throw ".NET CLI not found. Please install .NET 8 SDK."
    }
    
    $dotnetVersion = & dotnet --version
    Write-Log "Using .NET version: $dotnetVersion" "INFO"
    
    # Handle signing certificate
    $signingCert = $null
    if ($Sign) {
        Test-SignTool
        $signingCert = Get-SigningCertificate -Thumbprint $Thumbprint
        if (-not $signingCert) {
            Write-Log "Code signing requested but no certificate available" "ERROR"
            throw "Cannot proceed with signing - certificate not found"
        }
    }
    
    # Build for requested architectures
    $buildResults = @()
    
    $architectures = switch ($Architecture) {
        "x64"  { @("x64") }
        "arm64" { @("arm64") }
        "both" { @("x64", "arm64") }
    }
    
    foreach ($arch in $architectures) {
        Write-Log "" "INFO"
        $success = Build-Architecture -Arch $arch -SigningCert $signingCert
        $buildResults += @{
            Architecture = $arch
            Success = $success
            Path = "publish\$arch\taskbarutil.exe"
        }
        
        if ($Test -and $success) {
            $execPath = Join-Path $rootPath "publish\$arch\taskbarutil.exe"
            Test-Build -ExecutablePath $execPath
        }
    }
    
    # Build summary
    Write-Log "" "INFO"
    Write-Log "=== BUILD SUMMARY ===" "INFO"
    
    $successCount = 0
    $signedCount = 0
    foreach ($result in $buildResults) {
        if ($result.Success) {
            $successCount++
            $fullPath = Join-Path $rootPath $result.Path
            if (Test-Path $fullPath) {
                $fileInfo = Get-Item $fullPath
                $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
                
                # Check if file is signed
                $isSigned = $false
                if ($signingCert) {
                    try {
                        $signature = Get-AuthenticodeSignature -FilePath $fullPath
                        $isSigned = ($signature.Status -eq "Valid")
                        if ($isSigned) { $signedCount++ }
                    } catch {
                        $isSigned = $false
                    }
                }
                
                $signStatus = if ($signingCert) { if ($isSigned) { " [SIGNED]" } else { " [UNSIGNED]" } } else { "" }
                Write-Log "✅ $($result.Architecture): $($result.Path) ($sizeMB MB)$signStatus" "SUCCESS"
            } else {
                Write-Log "✅ $($result.Architecture): Built successfully" "SUCCESS"
            }
        } else {
            Write-Log "❌ $($result.Architecture): Build failed" "ERROR"
        }
    }
    
    Write-Log "" "INFO"
    Write-Log "Built $successCount of $($buildResults.Count) architectures successfully" "INFO"
    
    if ($signingCert) {
        if ($signedCount -eq $successCount) {
            Write-Log "All executables signed with certificate: $($signingCert.Subject)" "SUCCESS"
        } else {
            Write-Log "Signing completed for $signedCount of $successCount executables" "WARN"
            Write-Log "Certificate: $($signingCert.Subject)" "INFO"
        }
    }
    
    if ($successCount -eq $buildResults.Count) {
        Write-Log "All builds completed successfully!" "SUCCESS"
        exit 0
    } else {
        Write-Log "Some builds failed" "ERROR"
        exit 1
    }
    
} catch {
    Write-Log "Build process failed: $($_.Exception.Message)" "ERROR"
    exit 1
} finally {
    Pop-Location
}
