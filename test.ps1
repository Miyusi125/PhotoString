param([switch]$Desktop)
$ErrorActionPreference = 'Stop'
$testExecutable = Join-Path $PSScriptRoot 'dist\PhotoString.exe'
$testArtifacts = Join-Path $PSScriptRoot 'artifacts'
$testModes = @('--self-test')
if ($Desktop) { $testModes += '--desktop-check'; $testModes += '--app-check' }
foreach ($mode in $testModes) {
    $testProcess = Start-Process -FilePath $testExecutable -ArgumentList @($mode, ('"' + $testArtifacts + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($testProcess.ExitCode -ne 0) { throw ('Verification failed: ' + $mode + '. See artifacts for details.') }
    Write-Output ('Passed: ' + $mode)
}
