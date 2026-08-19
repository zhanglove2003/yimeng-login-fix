param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$BuildInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$userDotnet = Join-Path $env:USERPROFILE '.dotnet\sdk10\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $userDotnet) {
    $userDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}
$output = Join-Path $root 'artifacts\portable-single'

& $dotnet test (Join-Path $root 'YiMengFix.slnx') -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

& $dotnet publish (Join-Path $root 'src\YiMengFix.UI\YiMengFix.UI.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $output
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Write-Host "Portable build: $(Join-Path $output 'YiMengFix.exe')"

if ($BuildInstaller) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $compiler) {
        $knownCompiler = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($knownCompiler) {
            $compiler = [pscustomobject]@{ Source = $knownCompiler }
        }
    }
    if (-not $compiler) {
        throw 'Inno Setup 6 is required to build the installer (ISCC.exe was not found).'
    }

    & $compiler.Source (Join-Path $root 'packaging\YiMengFix.iss')
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }
}
