# AOT smoke test (§8.2): run ClearC.Desktop.exe --selfcheck and validate the exit code.
# Default target: artifacts\publish\win-x64\ClearC\ClearC.Desktop.exe (use -Path for another RID).
# ASCII only, so Windows PowerShell 5.1 and PowerShell 7 read it the same way.
param(
    [Parameter(Mandatory = $false)]
    [string] $Path,

    [Parameter(Mandatory = $false)]
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $repositoryRoot "artifacts\publish\win-x64\ClearC\ClearC.Desktop.exe"
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Executable not found: $Path (run publish_win-x64.bat --no-pause first)."
}

Write-Host "Smoke testing $Path ..."

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = (Resolve-Path -LiteralPath $Path).Path
$startInfo.Arguments = "--selfcheck"
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true

$process = [System.Diagnostics.Process]::Start($startInfo)
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        throw "--selfcheck did not exit within $TimeoutSeconds seconds."
    }

    $standardOutput = $process.StandardOutput.ReadToEnd().Trim()
    $standardError = $process.StandardError.ReadToEnd().Trim()
    $exitCode = $process.ExitCode
}
finally {
    $process.Dispose()
}

if ($standardOutput) { Write-Host $standardOutput }
if ($standardError) { Write-Host $standardError }

if ($exitCode -ne 0) {
    throw "--selfcheck exited with code $exitCode; expected 0."
}

Write-Host "Smoke test passed (exit code 0)."
