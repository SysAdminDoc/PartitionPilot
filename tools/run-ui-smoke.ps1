param(
    [switch]$AllowHeadlessSkip,
    [string]$Configuration = "Release",
    [string]$ArtifactsDir = "artifacts\ui-smoke"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([System.IO.Path]::IsPathRooted($ArtifactsDir)) {
    $artifactPath = [System.IO.Path]::GetFullPath($ArtifactsDir)
} else {
    $artifactPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsDir))
}

if (-not $artifactPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write UI smoke artifacts outside the repository: $artifactPath"
}

if (Test-Path -LiteralPath $artifactPath) {
    Remove-Item -LiteralPath $artifactPath -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactPath -Force | Out-Null

$screenshotDir = Join-Path $artifactPath "screenshots"
New-Item -ItemType Directory -Path $screenshotDir -Force | Out-Null

$env:PARTITIONPILOT_UI_SCREENSHOT_DIR = $screenshotDir
if ($AllowHeadlessSkip) {
    $env:PARTITIONPILOT_UI_TEST_ALLOW_HEADLESS_SKIP = "1"
} else {
    Remove-Item Env:\PARTITIONPILOT_UI_TEST_ALLOW_HEADLESS_SKIP -ErrorAction SilentlyContinue
}

$appProject = Join-Path $repoRoot "src\PartitionPilot\PartitionPilot.csproj"
$testProject = Join-Path $repoRoot "tests\PartitionPilot.UiTests\PartitionPilot.UiTests.csproj"
$trxPath = Join-Path $artifactPath "ui-smoke.trx"
$logPath = Join-Path $artifactPath "ui-smoke.log"

Push-Location $repoRoot
try {
    & dotnet build $appProject -c $Configuration -m:1 --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "PartitionPilot build failed with exit code $LASTEXITCODE."
    }

    $testOutput = & dotnet test $testProject -c $Configuration --no-restore --logger "trx;LogFileName=ui-smoke.trx" --results-directory $artifactPath 2>&1
    $testExitCode = $LASTEXITCODE
    $testOutput | Tee-Object -FilePath $logPath

    if (-not (Test-Path -LiteralPath $trxPath)) {
        throw "UI smoke test result was not created: $trxPath"
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath
    $results = $trx.SelectNodes("//*[local-name()='UnitTestResult']")
    $total = $results.Count
    $passed = @($results | Where-Object { $_.outcome -eq "Passed" }).Count
    $failed = @($results | Where-Object { $_.outcome -eq "Failed" }).Count
    $skipped = @($results | Where-Object { $_.outcome -in @("NotExecuted", "Skipped") }).Count

    "UI smoke tests: total=$total passed=$passed failed=$failed skipped=$skipped" | Tee-Object -FilePath $logPath -Append
    "TRX: $trxPath" | Tee-Object -FilePath $logPath -Append
    "Screenshots: $screenshotDir" | Tee-Object -FilePath $logPath -Append

    if ($testExitCode -ne 0) {
        throw "UI smoke tests failed with exit code $testExitCode. See $logPath"
    }

    if ($total -eq 0) {
        throw "No UI smoke tests were discovered. See $logPath"
    }

    if ($skipped -eq $total -and -not $AllowHeadlessSkip) {
        throw "All UI smoke tests skipped. Run from an interactive desktop session or pass -AllowHeadlessSkip for explicit headless verification."
    }

    if ($failed -gt 0) {
        throw "$failed UI smoke test(s) failed. See $logPath and $screenshotDir"
    }
}
finally {
    Remove-Item Env:\PARTITIONPILOT_UI_SCREENSHOT_DIR -ErrorAction SilentlyContinue
    if (-not $AllowHeadlessSkip) {
        Remove-Item Env:\PARTITIONPILOT_UI_TEST_ALLOW_HEADLESS_SKIP -ErrorAction SilentlyContinue
    }
    Pop-Location
}
