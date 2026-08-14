#Requires -Version 5.1
<#
.SYNOPSIS
    Builds a Release Native AOT binary and places the output in .\release.

.DESCRIPTION
    TerminalQuest.csproj sets PublishAot but deliberately omits a RuntimeIdentifier,
    so a plain "dotnet publish" produces no native binary. This script supplies the
    host RID, wipes .\release first so stale artifacts can never survive a build,
    and publishes straight into it.

    The release folder is ignored by .gitignore ([Rr]elease/).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'terminal-quest-2\TerminalQuest.csproj'
$releaseDir  = Join-Path $PSScriptRoot 'release'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project not found at '$projectPath'."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The 'dotnet' CLI was not found on PATH. Install the .NET 10 SDK and reopen your shell."
}

# Native AOT cannot cross-compile between architectures, so always target the host.
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = "win-$arch"

# The AOT link step runs findvcvarsall.bat, which calls VS's vcvarsall.bat, which
# in turn invokes a bare "vswhere.exe" and expects it on PATH. When it isn't, the
# resulting cmd error text is captured into MSBuild's $(CppLinker) property and the
# link command is built from garbage. Put the installer directory on PATH so the
# bare invocation resolves.
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    $vsInstallerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path -LiteralPath (Join-Path $vsInstallerDir 'vswhere.exe')) {
        $env:PATH = "$vsInstallerDir;$env:PATH"
    }
}

# Clear the folder's contents rather than the folder itself, so an open shell or
# editor holding a handle to .\release doesn't break the build. -Force in the
# listing picks up hidden files too.
if (Test-Path -LiteralPath $releaseDir) {
    Write-Host "Clearing $releaseDir ..."
    Get-ChildItem -LiteralPath $releaseDir -Force | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

Write-Host "Publishing $rid Release (Native AOT) -> $releaseDir"
Write-Host "This takes a few minutes: ILC compilation plus the MSVC link step."

# CopyOutputSymbolsToPublishDirectory=false keeps the ~60 MB native .pdb out of the
# release folder. The symbols are still generated under terminal-quest-2\bin, so a
# crash dump from this build can still be symbolicated -- they're just not shipped.
dotnet publish $projectPath -c Release -r $rid -o $releaseDir --nologo `
    -p:CopyOutputSymbolsToPublishDirectory=false

if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host "If the failure came from the linker, Native AOT needs the Visual Studio" -ForegroundColor Yellow
    Write-Host "'Desktop development with C++' workload (MSVC linker + Windows SDK)." -ForegroundColor Yellow
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Backstop: catch any symbol file a dependency drops in regardless of the flag above.
Get-ChildItem -LiteralPath $releaseDir -Recurse -Force -Include '*.pdb' |
    Remove-Item -Force

$exePath = Join-Path $releaseDir 'TerminalQuest.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish reported success but '$exePath' was not produced."
}

$exe = Get-Item -LiteralPath $exePath
$sizeMb = [math]::Round($exe.Length / 1MB, 1)

Write-Host ''
Write-Host "Build complete: $($exe.FullName) ($sizeMb MB)" -ForegroundColor Green
