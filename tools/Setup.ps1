# クローン直後の下ごしらえ。
#
#   .\tools\Setup.ps1
#
# 1. TextMeshPro の必須リソースを入れる
# 2. Assets/Scenes/Main.unity を生成する
#
# 1 が無いと TMP Settings が存在せず、実行時にフォントを作ろうとした時点で
# NullReferenceException になる。画面が 1 文字も出ない壊れ方をする。
#
# このファイルは BOM 付き UTF-8 で保存すること。
# Windows PowerShell 5.1 は BOM が無いと ANSI として読み、日本語が化ける。

[CmdletBinding()]
param(
    [string]$UnityVersion = "6000.6.2f1"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$unity = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"

if (-not (Test-Path $unity)) {
    Write-Error "Unity $UnityVersion が見つからない: $unity"
}

$buildDir = Join-Path $projectRoot "build"
if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory $buildDir | Out-Null }

# $quit を $false にするのは、非同期の処理を自分で待ってから終了するメソッド用。
# -quit を付けると取り込みが終わる前に Unity が落ちる。
function Invoke-UnityMethod([string]$method, [string]$logName, [bool]$quit) {
    $log = Join-Path $buildDir $logName

    $unityArgs = @('-batchmode', '-nographics')
    if ($quit) { $unityArgs += '-quit' }
    $unityArgs += @(
        '-projectPath'; "`"$projectRoot`""
        '-executeMethod'; $method
        '-logFile'; "`"$log`""
    )

    # Unity.exe は GUI サブシステムなので Start-Process -Wait で待つ。
    $proc = Start-Process -FilePath $unity -ArgumentList $unityArgs `
                          -NoNewWindow -Wait -PassThru

    if ($proc.ExitCode -ne 0) {
        Write-Host "$method が終了コード $($proc.ExitCode) で終わった" -ForegroundColor Red
        Write-Host "--- $log の末尾 ---"
        Get-Content $log -Tail 30 -Encoding UTF8
        return $false
    }
    return $true
}

Write-Host "1/2 TextMeshPro の必須リソースを導入中..."
if (-not (Invoke-UnityMethod "Concafe.EditorTools.ProjectSetup.SetUpBatch" "setup.log" $false)) {
    exit 1
}

if (-not (Test-Path (Join-Path $projectRoot "Assets\TextMesh Pro\Resources\TMP Settings.asset"))) {
    Write-Host "TMP Settings が出来ていない" -ForegroundColor Red
    exit 1
}

Write-Host "2/2 シーンを生成中..."
if (-not (Invoke-UnityMethod "Concafe.EditorTools.SceneBuilder.BuildScene" "scene-build.log" $true)) {
    exit 1
}

if (-not (Test-Path (Join-Path $projectRoot "Assets\Scenes\Main.unity"))) {
    Write-Host "シーンが出来ていない" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "下ごしらえ完了" -ForegroundColor Green
exit 0
