Param(
    [string]$PackageDir = ".",
    [string]$CertificatePath = $null,
    [string]$CertificatePassword = $null,
    [string]$CertificateFingerprint = $null,
    [string]$CertificateThumbprint = $null,
    [string]$CertificateSubject = $null,
    [string]$CertificateStoreName = "My",
    [string]$CertificateStoreLocation = "CurrentUser",
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$Overwrite
)

try {
    $resolved = (Resolve-Path -Path $PackageDir -ErrorAction Stop).ProviderPath
} catch {
    Write-Error "PackageDir '$PackageDir' not found."
    exit 1
}

Write-Host "Signing packages in: $resolved"

# Check if explicit cert provided
$hasCert = -not [string]::IsNullOrEmpty($CertificatePath) -or -not [string]::IsNullOrEmpty($CertificateFingerprint) -or -not [string]::IsNullOrEmpty($CertificateThumbprint) -or -not [string]::IsNullOrEmpty($CertificateSubject)

# If no cert provided, try to auto-detect code signing certificate from store
if (-not $hasCert) {
    Write-Host "No certificate specified; searching for code signing certificate in store..."

    $certStore = "Cert:\$CertificateStoreLocation\$CertificateStoreName"
    if (-not (Test-Path $certStore)) {
        Write-Host "Certificate store not found: $certStore"
        exit 0
    }

    # Find code signing certificates (EKU: 1.3.6.1.5.5.7.3.3)
    $certs = Get-ChildItem -Path $certStore -Recurse | Where-Object {
        $_.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" } |
        Where-Object { $_.Format($false) -match "1\.3\.6\.1\.5\.5\.7\.3\.3" }
    } | Where-Object { $_.HasPrivateKey }

    if ($certs) {
        if ($certs -is [array]) {
            Write-Host "Found $($certs.Count) code signing certificate(s):"
            foreach ($cert in $certs) {
                Write-Host "  - Subject: $($cert.Subject)"
                Write-Host "    Expires: $($cert.NotAfter)"
            }
            # Use the first one (most recent)
            $selectedCert = $certs[0]
            $hasCert = $true
            Write-Host "Using certificate: $($selectedCert.Subject)"
        } else {
            $selectedCert = $certs
            Write-Host "Found code signing certificate: $($selectedCert.Subject)"
            $hasCert = $true
        }

        # Use certificate hash (SHA256 fingerprint) for signing
        # This is the most reliable method for the dotnet CLI
        $hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
        $certBytes = $selectedCert.RawData
        $hashBytes = $hashAlgorithm.ComputeHash($certBytes)
        $CertificateFingerprint = [System.BitConverter]::ToString($hashBytes).Replace("-", "")
        Write-Host "Certificate SHA256 fingerprint: $CertificateFingerprint"
    } else {
        Write-Host "No code signing certificate found in $CertificateStoreLocation\$CertificateStoreName"
        Write-Host "Skipping package signing."
        exit 0
    }
}

if (-not $hasCert) {
    Write-Host "No certificate found; skipping signing for all packages."
    exit 0
}

$packages = Get-ChildItem -Path $resolved -File -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer -and ($_.Extension -eq '.nupkg' -or $_.Extension -eq '.snupkg') }
if (-not $packages -or $packages.Count -eq 0) {
    Write-Host "No .nupkg or .snupkg files found in $resolved"
    exit 0
}

$failed = $false
$signedCount = 0

foreach ($pkg in $packages) {
    Write-Host "Processing $($pkg.FullName)"
    $signArgs = @('nuget','sign',$pkg.FullName)

    if ($CertificatePath) {
        $signArgs += '--certificate-path'
        $signArgs += $CertificatePath
        if ($CertificatePassword) {
            $signArgs += '--certificate-password'
            $signArgs += $CertificatePassword
        }
    } elseif ($CertificateSubject) {
        # Use subject name (most reliable with cert store)
        $signArgs += '--certificate-subject-name'
        $signArgs += $CertificateSubject
        $signArgs += '--certificate-store-name'
        $signArgs += $CertificateStoreName
        $signArgs += '--certificate-store-location'
        $signArgs += $CertificateStoreLocation
    } elseif ($CertificateFingerprint) {
        # Fingerprint (SHA-256+)
        $signArgs += '--certificate-fingerprint'
        $signArgs += $CertificateFingerprint
        $signArgs += '--certificate-store-name'
        $signArgs += $CertificateStoreName
        $signArgs += '--certificate-store-location'
        $signArgs += $CertificateStoreLocation
    }

    $signArgs += '--timestamper'; $signArgs += $TimestampServer
    if ($Overwrite) { $signArgs += '--overwrite' }
    $signArgs += '--output'; $signArgs += $resolved

    Write-Host "Running: dotnet $($signArgs -join ' ')"
    $timeoutSeconds = 120
    try {
        $proc = Start-Process -FilePath 'dotnet' -ArgumentList $signArgs -NoNewWindow -PassThru
        $completed = $proc | Wait-Process -Timeout $timeoutSeconds -PassThru -ErrorAction Stop
        if ($proc.ExitCode -ne 0) {
            Write-Error "Signing failed for $($pkg.Name) (exit code $($proc.ExitCode))"
            $failed = $true
            break
        }
    } catch [System.TimeoutException] {
        Write-Error "Signing timed out after $timeoutSeconds seconds for $($pkg.Name). Timestamp server may be unreachable."
        $proc.Kill()
        $failed = $true
        break
    }
    $signedCount++
}

if ($failed) {
    exit 1
} else {
    Write-Host "All packages processed. Signed $signedCount package(s)."

    # Clean up any non-package files (temp files, signatures, etc. that signing may have created)
    $allFiles = @(Get-ChildItem -Path $resolved -File -ErrorAction SilentlyContinue)
    $nonPackages = @($allFiles | Where-Object { -not $_.PSIsContainer -and ($_.Extension -ne '.nupkg' -and $_.Extension -ne '.snupkg') })
    if ($nonPackages.Count -gt 0) {
        Write-Host "Cleaning up non-package files created during signing..."
        foreach ($f in $nonPackages) {
            Write-Host "  Removing: $($f.Name)"
            Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue
        }
    }

    # Validate that ONLY .nupkg and .snupkg files remain
    $finalFiles = @(Get-ChildItem -Path $resolved -File -ErrorAction SilentlyContinue)
    $remainingNonPackages = @($finalFiles | Where-Object { -not $_.PSIsContainer -and ($_.Extension -ne '.nupkg' -and $_.Extension -ne '.snupkg') })
    if ($remainingNonPackages.Count -gt 0) {
        Write-Error "ERROR: Non-package files found in package directory:"
        foreach ($f in $remainingNonPackages) {
            Write-Error "  - $($f.FullName)"
        }
        exit 1
    }

    exit 0
}
