# EditMode テストを batchmode で実行する。
#
#   .\tools\Run-Tests.ps1
#
# 結果は build\test-results.xml、ログは build\unity-test.log に出る。
# 失敗したテストがあれば一覧を表示し、終了コード 1 を返す。

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

$results = Join-Path $buildDir "test-results.xml"
$log     = Join-Path $buildDir "unity-test.log"

if (Test-Path $results) { Remove-Item $results }

Write-Host "テストを実行中... (初回はインポートで数分かかる)"

# -quit は付けない。テストの完了前に Unity が落ちることがある。
& $unity -batchmode -nographics `
    -projectPath $projectRoot `
    -runTests -testPlatform EditMode `
    -testResults $results `
    -logFile $log

$unityExit = $LASTEXITCODE

if (-not (Test-Path $results)) {
    Write-Host ""
    Write-Host "結果ファイルが出ていない。コンパイルで落ちた可能性がある。" -ForegroundColor Red
    Write-Host "--- $log の末尾 ---"
    Get-Content $log -Tail 40
    exit 1
}

[xml]$xml = Get-Content $results
$run = $xml.'test-run'

Write-Host ""
Write-Host ("合計 {0} / 成功 {1} / 失敗 {2} / スキップ {3}" -f `
    $run.total, $run.passed, $run.failed, $run.skipped)

if ([int]$run.failed -gt 0) {
    Write-Host ""
    Write-Host "--- 失敗したテスト ---" -ForegroundColor Red
    $xml.SelectNodes("//test-case[@result!='Passed']") | ForEach-Object {
        Write-Host ""
        Write-Host $_.fullname -ForegroundColor Red
        $message = $_.SelectSingleNode(".//message")
        if ($message) { Write-Host $message.InnerText }
        $stack = $_.SelectSingleNode(".//stack-trace")
        if ($stack) { Write-Host $stack.InnerText }
    }
    exit 1
}

if ($unityExit -ne 0) {
    Write-Host "Unity が終了コード $unityExit で終わった。$log を確認すること。" -ForegroundColor Yellow
    exit $unityExit
}

Write-Host "すべて成功" -ForegroundColor Green
exit 0
