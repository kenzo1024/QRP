param(
    [string]$ProjectPath = "D:\_Proj\QTAU6",
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe",
    [ValidateSet("Smoke", "Baseline", "Pressure", "All")]
    [string]$ScenarioSet = "Baseline",
    [int]$Repeat = 1,
    [int]$MaxAttempts = 5,
    [string]$OutputRoot = "D:\_QData\QOBData\望月 Work\工具\角色材质统一参数管理 Feature\PerformanceResults\BatchMode"
)

$ErrorActionPreference = "Stop"

function Get-ScenarioNames {
    param([string]$SetName)

    switch ($SetName) {
        "Smoke" {
            return @("Smoke_10Objects")
        }
        "Baseline" {
            return @(
                "B00_PhasedBaseline",
                "B01_SmallSingleSource",
                "B02_SmallConflict",
                "B03_ObjectScale",
                "B04_MainRealLoad",
                "B07_ForInstance",
                "B08_NarrowProperties"
            )
        }
        "Pressure" {
            return @(
                "B05_LargeObjectPressure",
                "B06_ManySourcesPressure",
                "B09_Logging"
            )
        }
        "All" {
            return @(
                "Smoke_10Objects",
                "B00_PhasedBaseline",
                "B01_SmallSingleSource",
                "B02_SmallConflict",
                "B03_ObjectScale",
                "B04_MainRealLoad",
                "B05_LargeObjectPressure",
                "B06_ManySourcesPressure",
                "B07_ForInstance",
                "B08_NarrowProperties",
                "B09_Logging"
            )
        }
    }
}

function Quote-CommandLinePath {
    param([string]$Path)

    return '"' + $Path.Replace('"', '\"') + '"'
}

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity executable not found: $UnityPath"
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project path not found: $ProjectPath"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$runStamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runnerRoot = Join-Path ([IO.Path]::GetTempPath()) "MatDataTransfer_BatchMode_$runStamp"
New-Item -ItemType Directory -Force -Path $runnerRoot | Out-Null
$scenarios = Get-ScenarioNames -SetName $ScenarioSet
$testClass = "Rendering.MatDataTransfer.PerformanceTests.MatDataTransferBatchModePerformanceTests"

for ($repeatIndex = 1; $repeatIndex -le $Repeat; $repeatIndex++) {
    foreach ($scenario in $scenarios) {
        $scenarioStamp = "$runStamp`_r$repeatIndex"
        $resultPath = Join-Path $runnerRoot "MatDataTransfer_BatchMode_$scenario`_$scenarioStamp.xml"
        $logPath = Join-Path $runnerRoot "MatDataTransfer_BatchMode_$scenario`_$scenarioStamp.log"
        $filter = "$testClass.$scenario"

        Write-Host "Running $scenario repeat $repeatIndex/$Repeat"

        $attemptPassed = $false
        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            if (Test-Path -LiteralPath $resultPath) {
                Remove-Item -LiteralPath $resultPath -Force
            }

            $arguments = @(
                "-batchmode",
                "-projectPath", (Quote-CommandLinePath $ProjectPath),
                "-runTests",
                "-testPlatform", "PlayMode",
                "-testFilter", $filter,
                "-testResults", (Quote-CommandLinePath $resultPath),
                "-logFile", (Quote-CommandLinePath $logPath),
                "-mdtOutputRoot", (Quote-CommandLinePath $OutputRoot),
                "-mdtRunStamp", $scenarioStamp
            )

            $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -NoNewWindow -PassThru -Wait
            if ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $resultPath)) {
                $attemptPassed = $true
                break
            }

            Write-Host "Attempt $attempt did not produce a passing result for $scenario."
        }

        if (-not $attemptPassed) {
            throw "Unity test failed or produced no result: $scenario repeat $repeatIndex. See log: $logPath"
        }
    }
}

Remove-Item -LiteralPath $runnerRoot -Recurse -Force
Get-ChildItem -LiteralPath $OutputRoot -File -Filter '*.meta' -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-Host "MatDataTransfer batch performance run completed."
Write-Host "Output: $OutputRoot"
