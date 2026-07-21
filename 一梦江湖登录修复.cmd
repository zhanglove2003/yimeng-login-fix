@echo off
setlocal
set "YIMENG_FIX_SELF=%~f0"
set "PWSH="
if exist "%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe" set "PWSH=%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe"
if not defined PWSH if exist "%ProgramW6432%\PowerShell\7\pwsh.exe" set "PWSH=%ProgramW6432%\PowerShell\7\pwsh.exe"
if not defined PWSH if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not defined PWSH for %%I in (pwsh.exe) do if not "%%~$PATH:I"=="" set "PWSH=%%~$PATH:I"
if not defined PWSH if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" set "PWSH=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not defined PWSH (
  echo [ERROR] PowerShell was not found. No game files were changed.
  set "RC=1"
  goto :done
)
echo [*] Using PowerShell: %PWSH%
"%PWSH%" -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:YIMENG_FIX_SELF; $s=[IO.File]::ReadAllText($p); $m='### POWERSHELL SECTION ###'; $i=$s.LastIndexOf($m); if($i -lt 0){throw 'PowerShell section marker missing'}; Invoke-Expression $s.Substring($i + $m.Length)"
set "RC=%ERRORLEVEL%"
:done
echo.
if not "%YIMENG_NO_PAUSE%"=="1" pause
exit /b %RC%
### POWERSHELL SECTION ###
$ErrorActionPreference = 'Stop'

$expectedHash = '535854FB2ABE71E16F645A879F1C00144FD9BA71548DE44B8E51E049BEAD2D87'
$payloadBase64 = 'G6OJwsacBvAKPJy02qdulYsYXnRk88M7PIqpBgjC1TSUYcnTWFdaCS6OBFLilrcFbpQU7s6DStth0ndt+ivfhhLCwr5gdggxyar0pbaRZfh/QLIWln3Mzlc9k/oxejBQLuOWt3ec214USx0CyZxBynwZ1UzLOp4p08E4SJQR9lBR5tJ1gcG1C2ZJpLIzzBQxU9rQ/8Dsm5mYOSTv1BBgTl1pPggfu3ckCfNhMAg3evwUTOnSk8FzFE0VxgALNhHA15eFLcW6tv+X5eVyeWDIuezFVJ075gkbGN4ZxX81/jJYoqNMM7K4kw8un0w//f971aXJT8U82wQ6hc1auR1RtcdMz1RYLzE8HCLjeYQs6vCLQGpRuCGs5ezsPanHsvF6pmjqhmp7oC2Vq2iB4u/DnfLZtXshI6qOuIcno6qjl6Ry7LubNp61LEprOOu/qey0MleeRzIQOBcryZIz2q/PU2FXirGDXXfpAnuhaQOy7PbRCmsdrhbLbP/zsYvUVIroSq9Zx0mZtk0hn8Nmt3vLuA=='
$relativeFiles = @(
  'Engine\Binaries\Win32\netease.data',
  'Engine\Binaries\Win64\netease.data'
)

function Write-Step {
  param([string]$Message)
  Write-Host "[*] $Message"
}

function Get-FullPathOrNull {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  try {
    return [System.IO.Path]::GetFullPath($Path.Trim().Trim('"'))
  } catch {
    return $null
  }
}

function Get-Sha256HexFromBytes {
  param([byte[]]$Bytes)
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    (($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('X2') }) -join '')
  } finally {
    $sha.Dispose()
  }
}

function Get-Sha256HexFromFile {
  param([string]$Path)
  Get-Sha256HexFromBytes -Bytes ([System.IO.File]::ReadAllBytes($Path))
}

function Test-YiMengGameRoot {
  param([string]$Root)

  $fullPath = Get-FullPathOrNull -Path $Root
  if (-not $fullPath) {
    return $false
  }

  if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
    return $false
  }

  foreach ($relative in $relativeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $fullPath $relative) -PathType Leaf)) {
      return $false
    }
  }

  return $true
}

function Add-YiMengCandidate {
  param(
    [System.Collections.Generic.List[string]]$Candidates,
    [System.Collections.Generic.HashSet[string]]$Seen,
    [string]$Path
  )

  $fullPath = Get-FullPathOrNull -Path $Path
  if (-not $fullPath) {
    return
  }

  if (-not (Test-YiMengGameRoot -Root $fullPath)) {
    return
  }

  $key = $fullPath.TrimEnd('\')
  if ($Seen.Add($key)) {
    $Candidates.Add($fullPath) | Out-Null
  }
}

function Add-YiMengCandidateAndParents {
  param(
    [System.Collections.Generic.List[string]]$Candidates,
    [System.Collections.Generic.HashSet[string]]$Seen,
    [string]$Path,
    [int]$MaxParents = 6
  )

  $fullPath = Get-FullPathOrNull -Path $Path
  if (-not $fullPath) {
    return
  }

  if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
    $fullPath = Split-Path -Parent $fullPath
  }

  for ($i = 0; $i -le $MaxParents -and $fullPath; $i++) {
    Add-YiMengCandidate -Candidates $Candidates -Seen $Seen -Path $fullPath
    $parent = Split-Path -Parent $fullPath
    if (-not $parent -or $parent -eq $fullPath) {
      break
    }
    $fullPath = $parent
  }
}

function Search-YiMengRootsUnder {
  param(
    [System.Collections.Generic.List[string]]$Candidates,
    [System.Collections.Generic.HashSet[string]]$Seen,
    [string]$Root,
    [int]$MaxDepth = 5
  )

  $rootPath = Get-FullPathOrNull -Path $Root
  if (-not $rootPath -or -not (Test-Path -LiteralPath $rootPath -PathType Container)) {
    return
  }

  Add-YiMengCandidate -Candidates $Candidates -Seen $Seen -Path $rootPath

  $queue = [System.Collections.Generic.Queue[object]]::new()
  $queue.Enqueue([pscustomobject]@{ Path = $rootPath; Depth = 0 })
  while ($queue.Count -gt 0) {
    $current = $queue.Dequeue()
    if ($current.Depth -ge $MaxDepth) {
      continue
    }

    $children = @()
    try {
      $children = Get-ChildItem -LiteralPath $current.Path -Directory -Force -ErrorAction SilentlyContinue
    } catch {
      $children = @()
    }

    foreach ($child in $children) {
      $childPath = $child.FullName
      $leaf = $child.Name
      if ($leaf -in @('$Recycle.Bin', 'System Volume Information', 'Windows', 'ProgramData')) {
        continue
      }

      Add-YiMengCandidate -Candidates $Candidates -Seen $Seen -Path $childPath

      if ($current.Depth -lt ($MaxDepth - 1)) {
        $queue.Enqueue([pscustomobject]@{ Path = $childPath; Depth = $current.Depth + 1 })
      }
    }
  }
}

function Get-ShortcutTargets {
  $roots = @(
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('CommonDesktopDirectory'),
    [Environment]::GetFolderPath('StartMenu'),
    [Environment]::GetFolderPath('CommonStartMenu')
  ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique

  if ($roots.Count -eq 0) {
    return @()
  }

  $shell = $null
  try {
    $shell = New-Object -ComObject WScript.Shell
  } catch {
    return @()
  }

  $targets = [System.Collections.Generic.List[string]]::new()
  foreach ($root in $roots) {
    $links = @()
    try {
      $links = Get-ChildItem -LiteralPath $root -Filter '*.lnk' -File -Recurse -ErrorAction SilentlyContinue
    } catch {
      $links = @()
    }

    foreach ($link in $links) {
      try {
        $shortcut = $shell.CreateShortcut($link.FullName)
        if ($shortcut.TargetPath) {
          $targets.Add($shortcut.TargetPath) | Out-Null
        }
      } catch {
      }
    }
  }

  return $targets.ToArray()
}

function Find-YiMengGameRoots {
  $candidates = [System.Collections.Generic.List[string]]::new()
  $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

  Add-YiMengCandidate -Candidates $candidates -Seen $seen -Path $env:YIMENG_GAME_ROOT

  $searchRoots = @()
  if ($env:YIMENG_FIX_SEARCH_ROOTS) {
    $searchRoots += ($env:YIMENG_FIX_SEARCH_ROOTS -split ';')
  }

  foreach ($root in $searchRoots) {
    Search-YiMengRootsUnder -Candidates $candidates -Seen $seen -Root $root -MaxDepth 5
  }

  if ($env:YIMENG_FIX_SEARCH_ONLY -eq '1') {
    return $candidates.ToArray()
  }

  if ($env:YIMENG_FIX_SELF) {
    Add-YiMengCandidateAndParents -Candidates $candidates -Seen $seen -Path $env:YIMENG_FIX_SELF -MaxParents 8
  }

  foreach ($target in Get-ShortcutTargets) {
    Add-YiMengCandidateAndParents -Candidates $candidates -Seen $seen -Path $target -MaxParents 8
  }

  $yiMengName = ([char]0x4E00).ToString() + ([char]0x68A6).ToString() + ([char]0x6C5F).ToString() + ([char]0x6E56).ToString()
  $neteaseName = [string]::Concat([char]0x7F51, [char]0x6613)
  foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
    if ($drive.DriveType -ne [System.IO.DriveType]::Fixed -or -not $drive.IsReady) {
      continue
    }

    $root = $drive.RootDirectory.FullName
    foreach ($path in @(
      (Join-Path $root 'wyclx'),
      (Join-Path $root 'WYCLX'),
      (Join-Path $root $yiMengName),
      (Join-Path (Join-Path $root $neteaseName) $yiMengName),
      (Join-Path $root 'NetEase\wyclx'),
      (Join-Path $root 'Program Files\wyclx'),
      (Join-Path $root 'Program Files (x86)\wyclx')
    )) {
      Add-YiMengCandidate -Candidates $candidates -Seen $seen -Path $path
    }
  }

  return $candidates.ToArray()
}

function Read-Choice {
  param([string]$Prompt)

  if ($env:YIMENG_FIX_NONINTERACTIVE -eq '1') {
    return $env:YIMENG_FIX_CHOICE
  }

  return Read-Host $Prompt
}

function Select-YiMengGameRoot {
  param([string[]]$Candidates)

  if ($env:YIMENG_GAME_ROOT) {
    $forcedRoot = Get-FullPathOrNull -Path $env:YIMENG_GAME_ROOT
    if (Test-YiMengGameRoot -Root $forcedRoot) {
      Write-Step "Using YIMENG_GAME_ROOT: $forcedRoot"
      return $forcedRoot
    }
    throw "YIMENG_GAME_ROOT is not a valid game root: $env:YIMENG_GAME_ROOT"
  }

  if ($Candidates.Count -eq 1) {
    $only = $Candidates[0]
    Write-Host ""
    Write-Host "Found game root:"
    Write-Host "  $only"
    $answer = Read-Choice -Prompt 'Use this path? [Y/n]'
    if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match '^(y|yes)$') {
      return $only
    }
  } elseif ($Candidates.Count -gt 1) {
    Write-Host ""
    Write-Host "Found multiple possible game roots:"
    for ($i = 0; $i -lt $Candidates.Count; $i++) {
      Write-Host ("  {0}. {1}" -f ($i + 1), $Candidates[$i])
    }

    while ($true) {
      $answer = Read-Choice -Prompt 'Choose a number, or paste game root path'
      if ([string]::IsNullOrWhiteSpace($answer)) {
        if ($env:YIMENG_FIX_NONINTERACTIVE -eq '1') {
          throw 'No noninteractive choice was provided.'
        }
        continue
      }

      $number = 0
      if ([int]::TryParse($answer.Trim(), [ref]$number)) {
        if ($number -ge 1 -and $number -le $Candidates.Count) {
          return $Candidates[$number - 1]
        }
        Write-Host "Invalid number: $answer"
        if ($env:YIMENG_FIX_NONINTERACTIVE -eq '1') {
          throw "Invalid noninteractive choice: $answer"
        }
        continue
      }

      $manual = Get-FullPathOrNull -Path $answer
      if (Test-YiMengGameRoot -Root $manual) {
        return $manual
      }
      Write-Host "Invalid game root: $answer"
      if ($env:YIMENG_FIX_NONINTERACTIVE -eq '1') {
        throw "Invalid noninteractive path: $answer"
      }
    }
  }

  while ($true) {
    $manual = Read-Choice -Prompt 'Game root was not selected. Paste game root path'
    if ([string]::IsNullOrWhiteSpace($manual)) {
      if ($env:YIMENG_FIX_NONINTERACTIVE -eq '1') {
        throw 'No valid game root found.'
      }
      continue
    }

    $manualRoot = Get-FullPathOrNull -Path $manual
    if (Test-YiMengGameRoot -Root $manualRoot) {
      return $manualRoot
    }

    Write-Host "Invalid game root: $manual"
    Write-Host "The folder must contain Engine\Binaries\Win32\netease.data and Engine\Binaries\Win64\netease.data."
    if ($env:YIMENG_FIX_NONINTERACTIVE -eq '1') {
      throw "Invalid noninteractive path: $manual"
    }
  }
}

function Test-GameProcessRunning {
  param([string]$Root)

  $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
  foreach ($name in @('wyclx64', 'wyclx', 'Launcher')) {
    foreach ($process in Get-Process -Name $name -ErrorAction SilentlyContinue) {
      try {
        $path = $process.MainModule.FileName
      } catch {
        $path = $null
      }

      if ($path -and [System.IO.Path]::GetFullPath($path).StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
      }
    }
  }

  return $false
}

Write-Step "YiMeng login fix started."
Write-Step "Searching for game root..."
$gameRoot = Select-YiMengGameRoot -Candidates (Find-YiMengGameRoots)
Write-Step "Game root: $gameRoot"

if (Test-GameProcessRunning -Root $gameRoot) {
  throw "Game or launcher process is still running from $gameRoot. Close it and run again."
}

$payload = [Convert]::FromBase64String($payloadBase64)
$payloadHash = Get-Sha256HexFromBytes -Bytes $payload
if ($payload.Length -ne 400 -or $payloadHash -ne $expectedHash) {
  throw "Embedded payload verification failed. Length=$($payload.Length), SHA256=$payloadHash"
}

Write-Step "Checking target files..."
foreach ($relative in $relativeFiles) {
  $target = Join-Path $gameRoot $relative
  if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
    throw "Target file does not exist: $target"
  }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $gameRoot "_tmsoft_fix_backup\$stamp"
Write-Step "Backing up target files to: $backupRoot"
foreach ($relative in $relativeFiles) {
  $target = Join-Path $gameRoot $relative
  $backup = Join-Path $backupRoot $relative
  New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
  Copy-Item -LiteralPath $target -Destination $backup -Force
}

Write-Step "Replacing files..."
foreach ($relative in $relativeFiles) {
  $target = Join-Path $gameRoot $relative
  [System.IO.File]::WriteAllBytes($target, $payload)
}

Write-Step "Verifying target hashes..."
foreach ($relative in $relativeFiles) {
  $target = Join-Path $gameRoot $relative
  $hash = Get-Sha256HexFromFile -Path $target
  if ($hash -ne $expectedHash) {
    throw "Target hash mismatch: $target`nExpected: $expectedHash`nActual:   $hash"
  }
  Write-Host "[OK] $relative"
}

Write-Host ""
Write-Host "Done. Backup: $backupRoot"
Write-Host "You can now start the game."
