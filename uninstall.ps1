<#
.SYNOPSIS
    Removes everything install.ps1 registered, and optionally the installed files.

.DESCRIPTION
    Leaves nothing behind: the COM server registration, both .xnb thumbnail-handler
    associations, the "Save as PNG" verb and the folder verb. Empty parent keys that only
    existed to hold ours are pruned, but any key that still has other content is left alone.

    Under Scoop, run this from the manifest's pre_uninstall -- BEFORE Scoop tries to delete
    the app directory. Explorer holds XnbThumbnail.comhost.dll open while it is loaded, so
    an uninstall that skips this step fails with a file lock. Use -RestartExplorer, or the
    directory stays locked until the user logs out.

      "$dir\uninstall.ps1" -RestartExplorer

    Scoop owns its own files, so do NOT pass -RemoveFiles there.

.EXAMPLE
    .\uninstall.ps1 -RemoveFiles -RestartExplorer
    Full removal of a manual install.
#>
[CmdletBinding()]
param(
    # Also delete the installed binaries. Only for manual installs; Scoop manages its own.
    [switch] $RemoveFiles,

    [string] $InstallDir = "$env:LOCALAPPDATA\Programs\tinyxnb",

    [ValidateSet('User', 'Machine')]
    [string] $Scope = 'User',

    [switch] $RestartExplorer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Clsid = '{7E723B1E-5565-4E10-8F83-9676C8CDF2F0}'
$ThumbnailHandlerIid = '{e357fccd-a995-4576-b01f-234630154e96}'

if (-not [Environment]::Is64BitProcess) {
    throw 'Run this from 64-bit PowerShell, or it will look in Wow6432Node and find nothing to remove.'
}

if ($Scope -eq 'Machine') {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw '-Scope Machine touches HKLM and requires an elevated shell.' }
}

$classes = if ($Scope -eq 'Machine') { 'HKLM:\Software\Classes' } else { 'HKCU:\Software\Classes' }

function Remove-KeyIfPresent {
    param([string]$Key)
    if (Test-Path -LiteralPath $Key) {
        Remove-Item -LiteralPath $Key -Recurse -Force
        Write-Host "  removed $Key"
    }
}

function Remove-KeyIfEmpty {
    <#
      Prunes a container we may have created, but only when it holds nothing else --
      .xnb in particular may legitimately belong to another application.
    #>
    param([string]$Key)
    if (-not (Test-Path -LiteralPath $Key)) { return }
    $item = Get-Item -LiteralPath $Key
    $hasValues = @($item.GetValueNames() | Where-Object { $_ -ne '' }).Count -gt 0
    $hasDefault = $null -ne $item.GetValue('')
    if ($item.SubKeyCount -eq 0 -and -not $hasValues -and -not $hasDefault) {
        Remove-Item -LiteralPath $Key -Force
        Write-Host "  pruned empty $Key"
    }
    else {
        Write-Verbose "  kept $Key (still in use)"
    }
}

Write-Host "Removing tinyxnb registration from $classes ($Scope scope)"

# 1. COM server.
Remove-KeyIfPresent "$classes\CLSID\$Clsid"

# 2. Thumbnail handler associations, then prune the scaffolding they sat in.
Remove-KeyIfPresent "$classes\SystemFileAssociations\.xnb\ShellEx\$ThumbnailHandlerIid"
Remove-KeyIfEmpty   "$classes\SystemFileAssociations\.xnb\ShellEx"

Remove-KeyIfPresent "$classes\.xnb\ShellEx\$ThumbnailHandlerIid"
Remove-KeyIfEmpty   "$classes\.xnb\ShellEx"
Remove-KeyIfEmpty   "$classes\.xnb"

# 3. Verbs.
Remove-KeyIfPresent "$classes\SystemFileAssociations\.xnb\shell\SaveAsPng"
Remove-KeyIfEmpty   "$classes\SystemFileAssociations\.xnb\shell"
Remove-KeyIfEmpty   "$classes\SystemFileAssociations\.xnb"

Remove-KeyIfPresent "$classes\Directory\shell\XnbConvertAll"

Add-Type -Namespace TinyXnb -Name Shell32 -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr item1, System.IntPtr item2);
'@
[TinyXnb.Shell32]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)  # SHCNE_ASSOCCHANGED

if ($RestartExplorer) {
    Write-Host 'Restarting Explorer to release the handler DLL...'
    Get-Process -Name explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    for ($i = 0; $i -lt 20; $i++) {
        if (Get-Process -Name explorer -ErrorAction SilentlyContinue) { break }
        Start-Sleep -Milliseconds 250
    }
    if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
}
else {
    Write-Warning 'Explorer still has the handler DLL loaded. Restart it (or pass -RestartExplorer) before deleting or updating the files.'
}

if ($RemoveFiles) {
    if (Test-Path -LiteralPath $InstallDir) {
        try {
            Remove-Item -LiteralPath $InstallDir -Recurse -Force
            Write-Host "Deleted $InstallDir"
        }
        catch {
            Write-Warning "Could not delete ${InstallDir}: $_"
            Write-Warning 'This is almost always Explorer still holding XnbThumbnail.comhost.dll. Restart Explorer and re-run.'
        }
    }
    else {
        Write-Host "Nothing to delete at $InstallDir"
    }
}

Write-Host ''
Write-Host 'Uninstalled.'
