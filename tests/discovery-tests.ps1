$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$scriptPath = Join-Path $repoRoot '一梦江湖登录修复.cmd'
$scriptText = [System.IO.File]::ReadAllText($scriptPath)
$marker = '### POWERSHELL SECTION ###'
$markerIndex = $scriptText.LastIndexOf($marker)
if ($markerIndex -lt 0) {
  throw 'PowerShell section marker missing.'
}

$psSection = $scriptText.Substring($markerIndex + $marker.Length)

foreach ($required in @('function Test-YiMengGameRoot', 'function Find-YiMengGameRoots', 'function Select-YiMengGameRoot')) {
  if ($psSection -notlike "*$required*") {
    throw "Missing expected discovery function: $required"
  }
}

function New-FakeGameRoot {
  param(
    [string]$Root,
    [byte]$Seed
  )

  $win32 = Join-Path $Root 'Engine\Binaries\Win32'
  $win64 = Join-Path $Root 'Engine\Binaries\Win64'
  New-Item -ItemType Directory -Path $win32, $win64 -Force | Out-Null
  [System.IO.File]::WriteAllBytes((Join-Path $win32 'netease.data'), [byte[]](1..400 | ForEach-Object { ($_ + $Seed) % 251 }))
  [System.IO.File]::WriteAllBytes((Join-Path $win64 'netease.data'), [byte[]](1..400 | ForEach-Object { ($_ + $Seed + 7) % 251 }))
}

function Assert-TargetHash {
  param(
    [string]$Root,
    [string]$ExpectedHash
  )

  foreach ($relative in @('Engine\Binaries\Win32\netease.data', 'Engine\Binaries\Win64\netease.data')) {
    $file = Join-Path $Root $relative
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
    if ($actual -ne $ExpectedHash) {
      throw "Hash mismatch for $file. Actual=$actual"
    }
  }
}

function Get-TargetHashes {
  param([string]$Root)

  @(
    (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Root 'Engine\Binaries\Win32\netease.data')).Hash
    (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Root 'Engine\Binaries\Win64\netease.data')).Hash
  )
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('一梦江湖 测试 用户 ' + [guid]::NewGuid().ToString('N'))
$directRoot = Join-Path $testRoot 'direct'
$discoveryRoot = Join-Path $testRoot 'discovery'
$gameRoot = Join-Path $directRoot '网易 游戏\一梦江湖 客户端'
New-FakeGameRoot -Root $gameRoot -Seed 1

try {
  $beforeDirect = Get-TargetHashes -Root $gameRoot

  $env:YIMENG_NO_PAUSE = '1'
  $env:YIMENG_GAME_ROOT = $gameRoot
  $output = & $scriptPath 2>&1
  if ($LASTEXITCODE -ne 0) {
    $output | ForEach-Object { Write-Host $_ }
    throw "Script failed with exit code $LASTEXITCODE."
  }

  $expectedHash = '535854FB2ABE71E16F645A879F1C00144FD9BA71548DE44B8E51E049BEAD2D87'
  Assert-TargetHash -Root $gameRoot -ExpectedHash $expectedHash

  $backupFiles = Get-ChildItem -LiteralPath (Join-Path $gameRoot '_tmsoft_fix_backup') -Filter 'netease.data' -Recurse
  if ($backupFiles.Count -ne 2) {
    throw "Expected 2 backup files, found $($backupFiles.Count)."
  }

  $backupHashes = $backupFiles | ForEach-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash }
  if ($backupHashes -notcontains $beforeDirect[0] -or $backupHashes -notcontains $beforeDirect[1]) {
    throw 'Backup hashes do not match original files.'
  }

  Remove-Item Env:\YIMENG_GAME_ROOT -ErrorAction SilentlyContinue

  $gameOne = Join-Path $discoveryRoot 'A Root\YiMeng One'
  $gameTwo = Join-Path $discoveryRoot 'B Root 中文 & 空格\YiMeng Two'
  New-FakeGameRoot -Root $gameOne -Seed 11
  New-FakeGameRoot -Root $gameTwo -Seed 22
  $beforeOne = Get-TargetHashes -Root $gameOne

  $env:YIMENG_FIX_SEARCH_ROOTS = $discoveryRoot
  $env:YIMENG_FIX_CHOICE = '2'
  $env:YIMENG_FIX_NONINTERACTIVE = '1'
  $env:YIMENG_FIX_SEARCH_ONLY = '1'
  $output = & $scriptPath 2>&1
  if ($LASTEXITCODE -ne 0) {
    $output | ForEach-Object { Write-Host $_ }
    throw "Discovery script failed with exit code $LASTEXITCODE."
  }

  $afterOne = Get-TargetHashes -Root $gameOne
  if ($beforeOne[0] -ne $afterOne[0] -or $beforeOne[1] -ne $afterOne[1]) {
    throw 'The first discovered candidate was modified even though choice 2 was selected.'
  }

  Assert-TargetHash -Root $gameTwo -ExpectedHash $expectedHash

  Write-Host 'discovery-tests passed'
} finally {
  Remove-Item Env:\YIMENG_NO_PAUSE -ErrorAction SilentlyContinue
  Remove-Item Env:\YIMENG_GAME_ROOT -ErrorAction SilentlyContinue
  Remove-Item Env:\YIMENG_FIX_SEARCH_ROOTS -ErrorAction SilentlyContinue
  Remove-Item Env:\YIMENG_FIX_CHOICE -ErrorAction SilentlyContinue
  Remove-Item Env:\YIMENG_FIX_NONINTERACTIVE -ErrorAction SilentlyContinue
  Remove-Item Env:\YIMENG_FIX_SEARCH_ONLY -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
