param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64")]
    [string] $RuntimeIdentifier
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repositoryRoot "src\ClearC.Desktop\ClearC.Desktop.csproj"
$publishRoot = Join-Path $repositoryRoot "artifacts\publish"
$outputPath = Join-Path $publishRoot "$RuntimeIdentifier\ClearC"
$resolvedPublishRoot = [IO.Path]::GetFullPath($publishRoot)
$resolvedOutputPath = [IO.Path]::GetFullPath($outputPath)
$publishPrefix = $resolvedPublishRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (-not $resolvedOutputPath.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace unexpected publish path: $resolvedOutputPath"
}

if (Test-Path -LiteralPath $resolvedOutputPath) {
    Remove-Item -LiteralPath $resolvedOutputPath -Recurse -Force
}

$publishArguments = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-f", "net10.0-windows",
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:RestoreForce=true",
    "-p:PublishAot=true",
    "-p:PublishTrimmed=true",
    "-p:StripSymbols=true",
    "-p:IlcSingleThreaded=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:TreatWarningsAsErrors=false",
    "-p:ILLinkTreatWarningsAsErrors=false",
    "-o", $resolvedOutputPath
)

Write-Host "Publishing ClearC.Desktop for $RuntimeIdentifier..."
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "ClearC.Desktop publish failed for $RuntimeIdentifier with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $resolvedOutputPath -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
    Remove-Item -Force
Get-ChildItem -LiteralPath $resolvedOutputPath -Recurse -File -Filter "*.xml" -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-Host "Published ClearC.Desktop to $resolvedOutputPath"
