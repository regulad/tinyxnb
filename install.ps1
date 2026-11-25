<#
.SYNOPSIS
    Registers the tinyxnb Explorer thumbnail handler and the "Save as PNG" shell verbs.

.DESCRIPTION
    Per-user by default: writes only under HKCU\Software\Classes, so no elevation is needed.
    Deliberately does NOT use regsvr32, which would write to HKLM and prompt for admin.

    Two deployment shapes are supported:

      Manual     Copies the payload to -InstallDir (default %LOCALAPPDATA%\Programs\tinyxnb)
                 and registers it there.
      Scoop      -InPlace registers the binaries where they already sit, so Scoop keeps
                 ownership of the files. Point it at the stable 'current' junction rather
                 than the versioned directory, or every update will orphan the registration.

.NOTES
    Explorer must be restarted before any of this takes effect. Pass -RestartExplorer to do
    it here, or do it yourself later.

    Shell extensions get locked by Explorer while loaded. Under Scoop that means
    'scoop update' / 'scoop uninstall' will fail to delete the app directory unless
    uninstall.ps1 has run first and Explorer has released the DLL. Wire uninstall.ps1 into
    the manifest's pre_uninstall.

.EXAMPLE
    .\install.ps1 -RestartExplorer
    Copy to %LOCALAPPDATA%\Programs\tinyxnb, register, restart Explorer.

.EXAMPLE
    .\install.ps1 -InPlace -RestartExplorer
    Register binaries where they already are. This is the Scoop post_install form.
#>
[CmdletBinding()]
param(
    # Where to copy the binaries to. Ignored when -InPlace is given.
    [string] $InstallDir = "$env:LOCALAPPDATA\Programs\tinyxnb",

    # Where the built payload currently is. Auto-detected when omitted.
    [string] $SourceDir,

    # Register the binaries where they already are instead of copying them. For Scoop.
    [switch] $InPlace,

    # Machine scope writes HKLM and needs an elevated shell. Scoop global installs need this.
    [ValidateSet('User', 'Machine')]
    [string] $Scope = 'User',

    [switch] $RestartExplorer,

    # Testing aid: Explorer will never re-call the handler for an already-cached file,
    # so a stale thumbnail can look exactly like a broken handler. Implies -RestartExplorer.
    [switch] $ClearThumbnailCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Fixed. Must match XnbThumbnailProvider.ClsidString. Never regenerate it.
$Clsid = '{7E723B1E-5565-4E10-8F83-9676C8CDF2F0}'
# IThumbnailProvider's interface GUID, fixed by Windows.
$ThumbnailHandlerIid = '{e357fccd-a995-4576-b01f-234630154e96}'

$RequiredFiles = @(
    'XnbThumbnail.comhost.dll'
    'XnbThumbnail.dll'
    'XnbThumbnail.deps.json'
    'XnbThumbnail.runtimeconfig.json'
    'XnbCore.dll'
    # The handler's crash reports and usage counts (PRIVACY.md); xnb2png.exe embeds its own.
    'Sentry.dll'
    'xnb2png.exe'
)

#region helpers ---------------------------------------------------------------------------

function Set-RegValue {
    param([string]$Key, [string]$Name, [string]$Value)
    if (-not (Test-Path -LiteralPath $Key)) {
        New-Item -Path $Key -Force | Out-Null
    }
    # '(default)' is how the registry provider spells the unnamed value.
    New-ItemProperty -LiteralPath $Key -Name $Name -Value $Value -PropertyType String -Force | Out-Null
    Write-Verbose "  $Key :: $Name = $Value"
}

function Test-DotnetRuntime {
    <#
      XnbThumbnail is framework-dependent and pins Microsoft.NETCore.App 8.0.0 with
      rollForward=LatestMinor. Neither Minor nor LatestMinor crosses a major version, so a
      9.x or 10.x runtime does NOT satisfy it -- it must be 8.x specifically.
    #>
    # Probe the same places hostfxr does, in the same order, so a perfectly good
    # non-default install (DOTNET_ROOT, or a relocated one recorded in the registry)
    # isn't rejected out of hand.
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($env:DOTNET_ROOT) { $candidates.Add($env:DOTNET_ROOT) }

    $reg = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64' `
                            -ErrorAction SilentlyContinue
    if ($reg) {
        $loc = $reg.PSObject.Properties['InstallLocation']
        if ($loc -and $loc.Value) { $candidates.Add($loc.Value) }
    }

    foreach ($programFiles in @($env:ProgramW6432, $env:ProgramFiles)) {
        if ($programFiles) { $candidates.Add("$programFiles\dotnet") }
    }

    # String concatenation rather than Join-Path: Join-Path resolves the drive qualifier and
    # throws if it does not exist, which under $ErrorActionPreference='Stop' would turn a
    # stale DOTNET_ROOT into a fatal, baffling install failure.
    foreach ($root in ($candidates | Select-Object -Unique)) {
        $shared = "$root\shared\Microsoft.NETCore.App"
        if (-not (Test-Path -LiteralPath $shared)) { continue }
        $eight = Get-ChildItem -LiteralPath $shared -Directory |
                 ForEach-Object { $_.Name -as [version] } |
                 Where-Object { $_ -and $_.Major -eq 8 } |
                 Sort-Object -Descending
        if ($eight) { return $eight[0] }
    }
    return $null
}

function Resolve-PayloadFiles {
    <#
      Returns the full path of every required file. Handles both a flat packaged directory
      (Scoop, a release zip) and a freshly-built source tree where the payload is split
      across two publish directories.
    #>
    param([string]$Root)

    $found = @{}

    # Flat layout first.
    foreach ($name in $RequiredFiles) {
        $candidate = Join-Path $Root $name
        if (Test-Path -LiteralPath $candidate) { $found[$name] = $candidate }
    }
    if ($found.Count -eq $RequiredFiles.Count) { return $found }

    # Source-tree layout.
    $publishDirs = @(
        Join-Path $Root 'src\XnbThumbnail\bin\Release\net8.0-windows\win-x64\publish'
        Join-Path $Root 'src\Xnb2Png\bin\Release\net8.0\win-x64\publish'
    )
    foreach ($dir in $publishDirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($name in $RequiredFiles) {
            if ($found.ContainsKey($name)) { continue }
            $candidate = Join-Path $dir $name
            if (Test-Path -LiteralPath $candidate) { $found[$name] = $candidate }
        }
    }
    return $found
}

function Restart-ExplorerShell {
    Write-Host 'Restarting Explorer...'
    Get-Process -Name explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    # Windows normally relaunches the shell on its own; give it a moment, then help if not.
    for ($i = 0; $i -lt 20; $i++) {
        if (Get-Process -Name explorer -ErrorAction SilentlyContinue) { return }
        Start-Sleep -Milliseconds 250
    }
    Start-Process explorer.exe
}

#endregion --------------------------------------------------------------------------------

#region preconditions ---------------------------------------------------------------------

# A 32-bit PowerShell would have its HKCU\Software\Classes\CLSID writes silently redirected
# into Wow6432Node, and 64-bit Explorer would never see them. This is the single most
# common way a shell extension ends up doing nothing at all.
if (-not [Environment]::Is64BitProcess) {
    throw 'Run this from 64-bit PowerShell. A 32-bit host writes to Wow6432Node and 64-bit Explorer will never find the handler.'
}

if ($Scope -eq 'Machine') {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw '-Scope Machine writes to HKLM and requires an elevated shell.' }
}

$runtime = Test-DotnetRuntime
if (-not $runtime) {
    throw @'
The .NET 8 runtime was not found.

XnbThumbnail is a COM server, and COM hosting starts the runtime through
hostfxr_initialize_for_runtime_config, which cannot load a self-contained
runtimeconfig.json -- so the handler is framework-dependent by necessity and .NET 8 is a
hard prerequisite. A 9.x or 10.x runtime will not do; roll-forward does not cross majors.

Only the base runtime is needed -- the handler asks for Microsoft.NETCore.App, not
WindowsDesktop.App -- though the Desktop Runtime and the SDK are supersets and also work.

  winget install Microsoft.DotNet.Runtime.8      (needs admin)

Or, with no admin at all, into your profile:

  ./dotnet-install.ps1 -Runtime dotnet -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
  setx DOTNET_ROOT "$env:LOCALAPPDATA\Microsoft\dotnet"

DOTNET_ROOT is the first place both this script and the COM host's own runtime resolver
look, so a per-user install works -- but Explorer must be restarted afterwards to inherit
the new environment.

(xnb2png.exe is self-contained and needs none of this.)
'@
}
Write-Host "Found .NET runtime $runtime"

if (-not $SourceDir) { $SourceDir = $PSScriptRoot }
$SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path

$payload = Resolve-PayloadFiles -Root $SourceDir
$missing = $RequiredFiles | Where-Object { -not $payload.ContainsKey($_) }
if ($missing) {
    throw "Could not find these under '$SourceDir': $($missing -join ', '). Build first:`n" +
          "  dotnet publish src\Xnb2Png\Xnb2Png.csproj -c Release`n" +
          "  dotnet publish src\XnbThumbnail\XnbThumbnail.csproj -c Release"
}

#endregion --------------------------------------------------------------------------------

#region deploy ----------------------------------------------------------------------------

if ($InPlace) {
    $targetDir = $SourceDir
    Write-Host "Registering in place: $targetDir"
}
else {
    $targetDir = $InstallDir
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    foreach ($name in $RequiredFiles) {
        Copy-Item -LiteralPath $payload[$name] -Destination (Join-Path $targetDir $name) -Force
    }
    Write-Host "Copied $($RequiredFiles.Count) files to $targetDir"
}

$comhost = Join-Path $targetDir 'XnbThumbnail.comhost.dll'
$exe     = Join-Path $targetDir 'xnb2png.exe'

#endregion --------------------------------------------------------------------------------

#region register --------------------------------------------------------------------------

$classes = if ($Scope -eq 'Machine') { 'HKLM:\Software\Classes' } else { 'HKCU:\Software\Classes' }

# 1. The COM server itself.
Set-RegValue -Key "$classes\CLSID\$Clsid" -Name '(default)' -Value 'tinyxnb Thumbnail Provider'
Set-RegValue -Key "$classes\CLSID\$Clsid\InprocServer32" -Name '(default)' -Value $comhost
Set-RegValue -Key "$classes\CLSID\$Clsid\InprocServer32" -Name 'ThreadingModel' -Value 'Both'

# 2. Point .xnb at it. SystemFileAssociations is the right home because it applies
#    regardless of which application currently owns the extension.
Set-RegValue -Key "$classes\SystemFileAssociations\.xnb\ShellEx\$ThumbnailHandlerIid" `
             -Name '(default)' -Value $Clsid

#    .xnb normally has no ProgID at all, and some shell paths consult the bare extension
#    key first, so register there too -- without disturbing any default value already
#    present, which would hijack the file association.
Set-RegValue -Key "$classes\.xnb\ShellEx\$ThumbnailHandlerIid" -Name '(default)' -Value $Clsid

# 3. The "Save as PNG" verb. A static verb, not an IContextMenu extension: on Windows 11
#    static verbs stay in the primary context menu while IContextMenu DLLs are exiled to
#    "Show more options".
$verb = "$classes\SystemFileAssociations\.xnb\shell\SaveAsPng"
Set-RegValue -Key $verb -Name '(default)' -Value 'Save as PN&G'
Set-RegValue -Key $verb -Name 'Icon' -Value "$exe,0"
#    The shell caps this at roughly 15 files and launches one process per file; --dir is
#    the escape hatch for bulk work.
Set-RegValue -Key $verb -Name 'MultiSelectModel' -Value 'Document'
Set-RegValue -Key "$verb\command" -Name '(default)' -Value "`"$exe`" --gui-errors `"%1`""

# 4. Folder verb for bulk conversion. %V is the folder the user right-clicked.
$dirVerb = "$classes\Directory\shell\XnbConvertAll"
Set-RegValue -Key $dirVerb -Name '(default)' -Value 'Convert &XNB files to PNG'
Set-RegValue -Key $dirVerb -Name 'Icon' -Value "$exe,0"
Set-RegValue -Key "$dirVerb\command" -Name '(default)' -Value "`"$exe`" --dir `"%V`""

Write-Host "Registered under $classes ($Scope scope)"

# Tell the shell associations changed. Enough for the verbs to appear; NOT enough to make
# Explorer load a newly-registered handler DLL, which needs a restart.
Add-Type -Namespace TinyXnb -Name Shell32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr item1, System.IntPtr item2);
'@
[TinyXnb.Shell32]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)  # SHCNE_ASSOCCHANGED

#endregion --------------------------------------------------------------------------------

if ($ClearThumbnailCache) {
    Get-Process -Name explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Get-ChildItem "$env:LOCALAPPDATA\Microsoft\Windows\Explorer" -Filter 'thumbcache_*.db' `
        -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host 'Cleared the thumbnail cache.'
    $RestartExplorer = $true
}

if ($RestartExplorer) {
    Restart-ExplorerShell
}
else {
    Write-Host ''
    Write-Warning 'Explorer must be restarted before the thumbnail handler is loaded. Re-run with -RestartExplorer, or restart it yourself.'
}

Write-Host ''
Write-Host 'Installed. Right-click an .xnb file for "Save as PNG", or a folder for bulk conversion.'
