# テストを batchmode で実行する。
#
#   .\tools\Run-Tests.ps1                  … EditMode と PlayMode の両方
#   .\tools\Run-Tests.ps1 -Platform EditMode
#
# 結果は build\test-results-<platform>.xml、ログは build\unity-test-<platform>.log に出る。
# 失敗したテストがあれば一覧を表示し、終了コード 1 を返す。
#
# このファイルは BOM 付き UTF-8 で保存すること。
# Windows PowerShell 5.1 は BOM が無いと ANSI として読み、日本語が化ける。

[CmdletBinding()]
param(
    [string]$UnityVersion = "6000.6.2f1",
    [ValidateSet("EditMode", "PlayMode", "All")]
    [string]$Platform = "All"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$unity = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"

if (-not (Test-Path $unity)) {
    Write-Error "Unity $UnityVersion が見つからない: $unity"
}

$buildDir = Join-Path $projectRoot "build"
if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory $buildDir | Out-Null }

# 1 つのプラットフォームを回す。成功なら $true。
function Invoke-TestPlatform([string]$platform) {
    $results = Join-Path $buildDir "test-results-$platform.xml"
    $log     = Join-Path $buildDir "unity-test-$platform.log"

    if (Test-Path $results) { Remove-Item $results }

    Write-Host ""
    Write-Host "=== $platform ===" -ForegroundColor Cyan

    # -quit は付けない。テストの完了前に Unity が落ちることがある。
    #
    # 呼び出しに Start-Process -Wait を使うのは、Unity.exe が GUI サブシステムの
    # 実行ファイルであるため。`& $unity ...` だと PowerShell が終了を待たずに戻り、
    # 結果ファイルがまだ無い状態で判定してしまう。
    $unityArgs = @(
        '-batchmode'
        '-nographics'
        '-projectPath'; "`"$projectRoot`""
        '-runTests'
        '-testPlatform'; $platform
        '-testResults'; "`"$results`""
        '-logFile'; "`"$log`""
    )

    $proc = Start-Process -FilePath $unity -ArgumentList $unityArgs `
                          -NoNewWindow -Wait -PassThru

    if (-not (Test-Path $results)) {
        Write-Host "結果ファイルが出ていない。コンパイルで落ちた可能性がある。" -ForegroundColor Red
        Write-Host "--- $log の末尾 ---"
        Get-Content $log -Tail 40 -Encoding UTF8
        return $false
    }

    # Get-Content だと ANSI として読まれ、日本語のテスト名が壊れて XML の解析に失敗する。
    # XmlDocument.Load は XML 宣言の encoding を見るので、これを使う。
    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($results)
    $run = $xml.'test-run'

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
        return $false
    }

    if ($proc.ExitCode -ne 0) {
        Write-Host "Unity が終了コード $($proc.ExitCode) で終わった。$log を確認すること。" -ForegroundColor Yellow
        return $false
    }

    return $true
}

$platforms = if ($Platform -eq "All") { @("EditMode", "PlayMode") } else { @($Platform) }

Write-Host "テストを実行中... (初回はインポートで数分かかる)"

$allOk = $true
foreach ($p in $platforms) {
    if (-not (Invoke-TestPlatform $p)) { $allOk = $false }
}

Write-Host ""
if ($allOk) {
    Write-Host "すべて成功" -ForegroundColor Green
    exit 0
}

Write-Host "失敗あり" -ForegroundColor Red
exit 1
