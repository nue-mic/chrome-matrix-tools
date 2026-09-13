# Migrate a legacy "<Drive>\C<Name>_Multiple" layout into the unified
# "<Base>\<Name>_Multiple" layout used by Chrome Matrix Tools.
#
# All console output is ASCII on purpose: this file is saved as UTF-8 without
# BOM and Windows PowerShell 5.1 would garble non-ASCII literals.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\Migrate-LegacyProject.ps1 `
#       -OldRoot "F:\CRailway_Multiple" -Name "Railway" [-Base "F:\Chrome_Matrix_Browser"] [-DryRun]

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $OldRoot,
    [Parameter(Mandatory = $true)] [string] $Name,
    [string] $Base = "F:\Chrome_Matrix_Browser",
    [string] $ChromeExe = "C:\Program Files\Google\Chrome\Application\chrome.exe",
    [switch] $DryRun,
    # only rewrite the .lnk files of an already migrated project
    [switch] $FixLinksOnly
)

$ErrorActionPreference = "Stop"

function Info  ($m) { Write-Host "  $m" -ForegroundColor Gray }
function Ok    ($m) { Write-Host "  [OK] $m" -ForegroundColor Green }
function Warn  ($m) { Write-Host "  [!!] $m" -ForegroundColor Yellow }
function Fail  ($m) { Write-Host "  [XX] $m" -ForegroundColor Red; exit 1 }
function Step  ($m) { Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }

# WScript.Shell cannot open paths containing emoji / surrogate pairs: it
# silently hands back a blank shortcut. Rename to an ASCII temp name, edit,
# then rename back.
function Update-Shortcut {
    param(
        [string] $Path,
        [string] $OldUserData,
        [string] $NewUserData,
        [string] $ChromeExe,
        [string] $Description
    )

    $shell   = New-Object -ComObject WScript.Shell
    $work    = $Path
    $renamed = $false

    $sc = $shell.CreateShortcut($work)
    if (-not $sc.TargetPath) {
        $work = Join-Path (Split-Path $Path -Parent) ("__migrate_tmp__" + [guid]::NewGuid().ToString("N") + ".lnk")
        Move-Item -LiteralPath $Path -Destination $work
        $renamed = $true
        $sc = $shell.CreateShortcut($work)
    }

    try {
        if ($sc.Arguments -match '--user-data-dir=(?:"([^"]*)"|(\S+))') {
            $dir = if ($matches[1]) { $matches[1] } else { $matches[2] }
        } else {
            return "no-data-dir"
        }

        if ($dir -like ($OldUserData + "*")) {
            $dir = $NewUserData + $dir.Substring($OldUserData.Length)
        } elseif ($dir -notlike ($NewUserData + "*")) {
            return "foreign:$dir"
        }

        # only the data dir moves - never clobber extra arguments the user
        # may have added by hand (--proxy-server, --load-extension, ...)
        $sc.Arguments        = $sc.Arguments -replace '--user-data-dir=("[^"]*"|\S+)',
                                                      ('--user-data-dir="' + $dir + '"')
        $sc.TargetPath       = $ChromeExe
        $sc.WorkingDirectory = Split-Path $ChromeExe -Parent
        if ($Description) { $sc.Description = $Description }
        $sc.Save()
        return "ok"
    }
    finally {
        if ($renamed) { Move-Item -LiteralPath $work -Destination $Path }
    }
}

$OldRoot = $OldRoot.TrimEnd('\')
$NewRoot = Join-Path $Base ($Name + "_Multiple")

$oldUserData  = Join-Path $OldRoot ($Name + "_UserData")
$oldShortCuts = Join-Path $OldRoot ($Name + "_ShortCuts")
# legacy template folder was named either <Name>_UserData_Template or <Name>_Template_UserData
$oldTemplate  = @(
    (Join-Path $OldRoot ($Name + "_UserData_Template")),
    (Join-Path $OldRoot ($Name + "_Template_UserData"))
) | Where-Object { Test-Path $_ } | Select-Object -First 1

$newUserData  = Join-Path $NewRoot ($Name + "_UserData")
$newShortCuts = Join-Path $NewRoot ($Name + "_ShortCuts")
$newTemplate  = Join-Path $NewRoot ($Name + "_Template_UserData")
$templateLnk  = Join-Path $NewRoot ($Name + "_Template_UserData.lnk")
$legacyDir    = Join-Path $Base ("_legacy_scripts\" + $Name)

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Chrome Matrix - legacy project migration"    -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Info "from : $OldRoot"
Info "to   : $NewRoot"
if ($DryRun) { Warn "DRY RUN - nothing will be changed" }

# ------------------------------------------------------------- fix-links only
if ($FixLinksOnly) {
    Step "Rewriting shortcut targets only"
    if (-not (Test-Path $newShortCuts)) { Fail "shortcut folder not found: $newShortCuts" }

    $fixed = 0; $untouched = 0
    foreach ($f in Get-ChildItem $newShortCuts -Filter *.lnk -File) {
        $r = Update-Shortcut -Path $f.FullName -OldUserData $oldUserData `
                             -NewUserData $newUserData -ChromeExe $ChromeExe -Description $Name
        if ($r -eq "ok") { $fixed++ } else { Warn "$($f.Name): $r"; $untouched++ }
    }
    Ok "rewritten: $fixed   skipped: $untouched"
    Write-Host ""
    exit 0
}

# ---------------------------------------------------------------- pre-checks
Step "Pre-flight checks"

if (-not (Test-Path $OldRoot))      { Fail "source root not found: $OldRoot" }
if (-not (Test-Path $oldUserData))  { Fail "user data folder not found: $oldUserData" }
if (-not (Test-Path $oldShortCuts)) { Fail "shortcut folder not found: $oldShortCuts" }
if (Test-Path $NewRoot)             { Fail "target already exists: $NewRoot" }

$busy = Get-CimInstance Win32_Process -Filter "name='chrome.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like ("*" + $OldRoot + "*") }
if ($busy) { Fail "$($busy.Count) chrome process(es) are still using $OldRoot - close them first" }

$lnkFiles = @(Get-ChildItem $oldShortCuts -Filter *.lnk -File)
$profiles = @(Get-ChildItem $oldUserData -Directory)
Ok "shortcuts: $($lnkFiles.Count)   profiles: $($profiles.Count)"
if ($oldTemplate) { Ok "template : $oldTemplate" } else { Warn "no template folder found - will skip" }

if ($DryRun) { Write-Host ""; Warn "dry run finished"; exit 0 }

# ---------------------------------------------------------------- move dirs
Step "Moving folders"

New-Item -ItemType Directory -Path $NewRoot -Force | Out-Null

Move-Item -LiteralPath $oldUserData  -Destination $newUserData
Ok "$($Name)_UserData"

Move-Item -LiteralPath $oldShortCuts -Destination $newShortCuts
Ok "$($Name)_ShortCuts"

if ($oldTemplate) {
    Move-Item -LiteralPath $oldTemplate -Destination $newTemplate
    Ok "$(Split-Path $oldTemplate -Leaf)  ->  $($Name)_Template_UserData"
}

# ---------------------------------------------------------------- fix links
Step "Rewriting shortcut targets"

$shell     = New-Object -ComObject WScript.Shell
$chromeDir = Split-Path $ChromeExe -Parent
$fixed = 0; $untouched = 0

foreach ($f in Get-ChildItem $newShortCuts -Filter *.lnk -File) {
    $r = Update-Shortcut -Path $f.FullName -OldUserData $oldUserData `
                         -NewUserData $newUserData -ChromeExe $ChromeExe -Description $Name
    if ($r -eq "ok") { $fixed++ } else { Warn "$($f.Name): $r"; $untouched++ }
}
Ok "rewritten: $fixed   skipped: $untouched"

# ---------------------------------------------------------------- template lnk
if (Test-Path $newTemplate) {
    Step "Creating template shortcut"
    $sc = $shell.CreateShortcut($templateLnk)
    $sc.TargetPath       = $ChromeExe
    $sc.Arguments        = '--user-data-dir="' + $newTemplate + '"'
    $sc.WorkingDirectory = $chromeDir
    $sc.Description      = "$Name template"
    $sc.Save()
    Ok (Split-Path $templateLnk -Leaf)
}

# ---------------------------------------------------------------- desktop
Step "Updating desktop entries"

$desktops = @([Environment]::GetFolderPath('Desktop'), "$env:PUBLIC\Desktop") |
            Where-Object { Test-Path $_ }
$hit = 0
foreach ($d in $desktops) {
    foreach ($f in Get-ChildItem $d -Filter *.lnk -File -ErrorAction SilentlyContinue) {
        $sc = $shell.CreateShortcut($f.FullName)
        if ($sc.TargetPath -like ($OldRoot + "*")) {
            $sc.TargetPath = $NewRoot + $sc.TargetPath.Substring($OldRoot.Length)
            $sc.Save()
            Ok "$($f.Name)  ->  $($sc.TargetPath)"
            $hit++
        }
    }
}
if ($hit -eq 0) { Info "none found" }

# ---------------------------------------------------------------- leftovers
Step "Archiving leftover files"

# the old per-project scripts are superseded by the GUI tool -> archive them.
# anything else (certs, notes, ...) is project data and follows the project.
$legacyNames = @(($Name + "Generator.ps1"), ($Name + "CloneUserData.ps1"), "README.md")

foreach ($item in @(Get-ChildItem $OldRoot -Force)) {
    if ($legacyNames -contains $item.Name) {
        New-Item -ItemType Directory -Path $legacyDir -Force | Out-Null
        Move-Item -LiteralPath $item.FullName -Destination $legacyDir -Force
        Ok "$($item.Name)  ->  $legacyDir"
    } else {
        Move-Item -LiteralPath $item.FullName -Destination $NewRoot -Force
        Ok "$($item.Name)  ->  $NewRoot"
    }
}

Remove-Item -LiteralPath $OldRoot -Force
Ok "removed empty $OldRoot"

# ---------------------------------------------------------------- done
Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Migration finished" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Cyan
Info "root      : $NewRoot"
Info "shortcuts : $newShortCuts"
Info "user data : $newUserData"
if (Test-Path $newTemplate) { Info "template  : $newTemplate" }
Write-Host ""
