Param(
    [string]$OutDir = ".\dist",
    [string]$Configuration = "Release",
    [string[]]$Projects = @(
        "src\DevFlow\LeXtudio.DevFlow.Agent.Core\LeXtudio.DevFlow.Agent.Core.csproj",
        "src\DevFlow\LeXtudio.DevFlow.Agent.WPF\LeXtudio.DevFlow.Agent.WPF.csproj",
        "src\DevFlow\LeXtudio.DevFlow.Agent.WinForms\LeXtudio.DevFlow.Agent.WinForms.csproj",
        "src\DevFlow\LeXtudio.DevFlow.Agent.Uno\LeXtudio.DevFlow.Agent.Uno.csproj",
        "src\DevFlow\LeXtudio.DevFlow.Agent.MewUI\LeXtudio.DevFlow.Agent.MewUI.csproj",
        "src\DevFlow\LeXtudio.DevFlow.Driver\LeXtudio.DevFlow.Driver.csproj",
        "src\Cli\src\LeXtudio.Wpf.Cli\LeXtudio.Wpf.Cli.csproj",
        "src\Cli\src\LeXtudio.WinForms.Cli\LeXtudio.WinForms.Cli.csproj",
        "src\Cli\src\LeXtudio.MewUI.Cli\LeXtudio.MewUI.Cli.csproj",
        "src\Cli\src\LeXtudio.Uno.Cli\LeXtudio.Uno.Cli.csproj"
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$BuildRoot = Join-Path $RepoRoot ".build_out"
$PackageStaging = Join-Path $RepoRoot ".pkg_staging"

function Find-MSBuild {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand -and [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) {
        return $dotnetCommand.Path
    }

    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    $vswhere = if ($programFilesX86) {
        Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
    }

    if ($vswhere -and (Test-Path $vswhere)) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($installPath) {
            $candidate = Join-Path $installPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) {
                return (Resolve-Path $candidate).Path
            }
        }
    }

    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Path
    }

    throw "MSBuild was not found. Install Visual Studio/MSBuild to pack the WinUI target."
}

function Resolve-UnoSdkExtrasTasksAssembly([string]$ProjectPath) {
    $packageRoot = [Environment]::GetEnvironmentVariable("NUGET_PACKAGES", "Process")
    if (-not $packageRoot) {
        $packageRoot = [Environment]::GetEnvironmentVariable("NUGET_PACKAGES", "User")
    }
    if (-not $packageRoot) {
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        $packageRoot = Join-Path $userProfile ".nuget\packages"
    }

    $extrasRoot = Join-Path $packageRoot "uno.sdk.extras"
    if (-not (Test-Path $extrasRoot)) {
        return $null
    }

    $assetsPath = Join-Path ([IO.Path]::GetDirectoryName($ProjectPath)) "obj\project.assets.json"
    if (Test-Path $assetsPath) {
        $assets = Get-Content -LiteralPath $assetsPath -Raw
        $match = [regex]::Match($assets, '"Uno\.Sdk\.Extras/(?<version>[^"]+)"')
        if ($match.Success) {
            $restoredAssembly = Get-ChildItem -Path (Join-Path $extrasRoot $match.Groups["version"].Value) -Recurse -Filter "Uno.Sdk.Extras_*.dll" -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($restoredAssembly) {
                return $restoredAssembly.FullName
            }
        }
    }

    $assembly = Get-ChildItem -Path $extrasRoot -Recurse -Filter "Uno.Sdk.Extras_*.dll" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($assembly) {
        return $assembly.FullName
    }

    return $null
}

function Resolve-ProjectPath([string]$Project) {
    if (Test-Path $Project) {
        return (Resolve-Path $Project).Path
    }

    $candidate = Join-Path $RepoRoot $Project
    if (Test-Path $candidate) {
        return (Resolve-Path $candidate).Path
    }

    throw "Project file not found: $Project"
}

function Reset-Directory([string]$Path) {
    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Get-AssemblyReferenceNames([string]$AssemblyPath) {
    Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction Stop

    $stream = [IO.File]::OpenRead($AssemblyPath)
    try {
        $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
            foreach ($handle in $metadata.AssemblyReferences) {
                $assemblyRef = $metadata.GetAssemblyReference($handle)
                $metadata.GetString($assemblyRef.Name)
            }
        } finally {
            $peReader.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Get-TypeDefinitionNames([string]$AssemblyPath) {
    Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction Stop

    $stream = [IO.File]::OpenRead($AssemblyPath)
    try {
        $peReader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
            foreach ($handle in $metadata.TypeDefinitions) {
                $type = $metadata.GetTypeDefinition($handle)
                $namespace = $metadata.GetString($type.Namespace)
                $name = $metadata.GetString($type.Name)
                if ($namespace) {
                    "$namespace.$name"
                } else {
                    $name
                }
            }
        } finally {
            $peReader.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Test-Package([string]$PackagePath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop

    $extractRoot = Join-Path ([IO.Path]::GetTempPath()) "pkg_verify_$([Guid]::NewGuid())"
    Reset-Directory $extractRoot
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $extractRoot)

        $dlls = @(
            Get-ChildItem -Path (Join-Path $extractRoot "lib") -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue
        )
        if (-not $dlls -or $dlls.Count -eq 0) {
            $toolDlls = @(
                Get-ChildItem -Path (Join-Path $extractRoot "tools") -Recurse -Filter "*.dll" -ErrorAction SilentlyContinue
            )
            if (-not $toolDlls -or $toolDlls.Count -eq 0) {
                throw "Package '$PackagePath' does not contain library or tool DLL assets."
            }
            $dlls = $toolDlls
        }

        foreach ($assembly in $dlls) {
            Write-Host "  Verified package contains: $($assembly.FullName)"
        }
    } finally {
        if (Test-Path $extractRoot) {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-PackProject([string]$MSBuild, [string]$ProjectPath) {
    $projectName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $projectOutput = Join-Path $BuildRoot $projectName
    Reset-Directory $projectOutput
    $unoSdkExtrasTasksAssembly = Resolve-UnoSdkExtrasTasksAssembly $ProjectPath

    $msbuildFileName = [IO.Path]::GetFileNameWithoutExtension($MSBuild)
    $arguments = @(
        "/restore",
        "/t:Pack",
        "/p:Configuration=$Configuration",
        "/p:BaseOutputPath=$projectOutput\",
        "/p:PackageOutputPath=$PackageStaging",
        "/v:minimal",
        "/nologo"
    )
    if ($msbuildFileName -ieq "dotnet") {
        $arguments = @("msbuild", $ProjectPath) + $arguments
    } else {
        $arguments = @($ProjectPath) + $arguments
    }

    if ($unoSdkExtrasTasksAssembly) {
        $arguments += "/p:UnoSdkExtrasTasksAssembly=$unoSdkExtrasTasksAssembly"
    }

    Write-Host ""
    Write-Host "Packing $ProjectPath"
    Write-Host "  Output:   $projectOutput"
    Write-Host "  Packages: $PackageStaging"
    if ($unoSdkExtrasTasksAssembly) {
        Write-Host "  Uno SDK extras tasks: $unoSdkExtrasTasksAssembly"
    }

    # MSBuild imports environment variables as properties. Clear OutDir so callers
    # cannot accidentally force all target frameworks into one shared folder.
    $previousOutDir = [Environment]::GetEnvironmentVariable("OutDir", "Process")
    [Environment]::SetEnvironmentVariable("OutDir", $null, "Process")
    try {
        & $MSBuild @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild pack failed for '$ProjectPath' with exit code $LASTEXITCODE."
        }
    } finally {
        [Environment]::SetEnvironmentVariable("OutDir", $previousOutDir, "Process")
    }
}

function Copy-PackagesToOutput([string]$Source, [string]$Destination) {
    $packages = @(
        Get-ChildItem -Path $Source -File |
            Where-Object { $_.Extension -eq ".nupkg" -or $_.Extension -eq ".snupkg" } |
            Sort-Object Name
    )

    if ($packages.Count -eq 0) {
        throw "No .nupkg or .snupkg files were produced in '$Source'."
    }

    Write-Host ""
    Write-Host "Verifying packages"
    foreach ($package in $packages | Where-Object { $_.Extension -eq ".nupkg" }) {
        Test-Package $package.FullName
    }

    Write-Host ""
    Write-Host "Copying packages to $Destination"
    foreach ($package in $packages) {
        Copy-Item -LiteralPath $package.FullName -Destination $Destination -Force
        Write-Host "  $($package.Name)"
    }

    $nonPackages = @(
        Get-ChildItem -Path $Destination -File |
            Where-Object { $_.Extension -ne ".nupkg" -and $_.Extension -ne ".snupkg" }
    )
    if ($nonPackages.Count -gt 0) {
        throw "Non-package files found in output directory: $($nonPackages.FullName -join ', ')"
    }
}

try {
    $msbuild = Find-MSBuild
    $outputPath = if ([IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path (Get-Location) $OutDir }
    $outputPath = [IO.Path]::GetFullPath($outputPath)

    Write-Host "MSBuild: $msbuild"
    Write-Host "Configuration: $Configuration"
    Write-Host "Output directory: $outputPath"

    Reset-Directory $BuildRoot
    Reset-Directory $PackageStaging
    Reset-Directory $outputPath

    foreach ($project in $Projects) {
        Invoke-PackProject $msbuild (Resolve-ProjectPath $project)
    }

    Copy-PackagesToOutput $PackageStaging $outputPath

    Write-Host ""
    Write-Host "Packing complete." -ForegroundColor Green
    Get-ChildItem -Path $outputPath -File |
        Sort-Object Name |
        ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Green }
} finally {
    if (Test-Path $BuildRoot) {
        Remove-Item -LiteralPath $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $PackageStaging) {
        Remove-Item -LiteralPath $PackageStaging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
