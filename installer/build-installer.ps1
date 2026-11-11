# ローカルでインストーラーを作る（Release ワークフローと同じ手順）。
#   ./installer/build-installer.ps1 -Version 1.0.0
param(
    [string]$Version = "0.0.0"
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^(\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' は SemVer 形式ではありません（例: 1.2.0）" }
$numeric = "$($Matches[1]).0"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish"
$outputDir = Join-Path $root "artifacts\installer"
$project = Join-Path $root "src\TechnicsControl.App\TechnicsControl.App.csproj"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project -c Release -p:Platform=x64 -p:Version=$Version -p:AssemblyVersion=$numeric -p:FileVersion=$numeric `
    -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました ($LASTEXITCODE)" }

# TechnicsControl.pri（XAML の索引）が無いと、インストール後に起動直後に落ちる（0xc000027b）
if (-not (Test-Path (Join-Path $publishDir "TechnicsControl.pri"))) { throw "publish の出力に TechnicsControl.pri がありません" }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw "ISCC.exe（Inno Setup 6）が見つかりません" }

& $iscc "/DAppVersion=$Version" "/DAppVersionNumeric=$numeric" "/DPublishDir=$publishDir" "/DOutputDir=$outputDir" (Join-Path $PSScriptRoot "TechnicsControl.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC に失敗しました ($LASTEXITCODE)" }
