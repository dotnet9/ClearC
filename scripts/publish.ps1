param(
    # Accept either the plural form used by the batch wrappers or the old
    # singular switch so existing callers remain compatible.
    [Parameter(Mandatory = $false)]
    [string[]] $RuntimeIdentifiers,

    [Parameter(Mandatory = $false)]
    [string] $RuntimeIdentifier,

    # Optional version baked into the assembly metadata (for example 0.2.0);
    # the project's own Version property is used when omitted.
    [Parameter(Mandatory = $false)]
    [string] $Version
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repositoryRoot "src\ClearC.Desktop\ClearC.Desktop.csproj"
$publishRoot = Join-Path $repositoryRoot "artifacts\publish"
$resolvedPublishRoot = [IO.Path]::GetFullPath($publishRoot)

$requestedRids = @($RuntimeIdentifiers) + @($RuntimeIdentifier)
if ($requestedRids.Count -eq 0) {
    $requestedRids = @("win-x64")
}

# Batch files pass a quoted, space-separated list. Also accept commas and
# semicolons so this script is convenient to call directly from PowerShell.
$rids = @(
    $requestedRids |
        ForEach-Object { $_ -split '[\s,;]+' } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
)
$supportedRids = @("win-x64", "win-x86", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
foreach ($rid in $rids) {
    if ($supportedRids -notcontains $rid) {
        throw "Unsupported runtime identifier '$rid'. Supported platforms: $($supportedRids -join ', ')."
    }
}

foreach ($rid in $rids) {
    $targetFramework = if ($rid.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) { "net10.0-windows" } else { "net10.0" }
    # pwsh 7 reserves $IsWindows as a read-only automatic variable (case-insensitive),
    # so the per-RID flag needs a name that cannot collide with it.
    $isWindowsRid = $rid.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)
    $outputPath = Join-Path $publishRoot "$rid\ClearC"
    $resolvedOutputPath = [IO.Path]::GetFullPath($outputPath)
    $publishPrefix = $resolvedPublishRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    if (-not $resolvedOutputPath.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace unexpected publish path: $resolvedOutputPath"
    }

    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $resolvedOutputPath -Recurse -Force
    }

    # NativeAOT and trimming settings mirror xskj-component's release
    # profiles. Explicit CLI properties keep all platform wrappers consistent.
    $publishArguments = @(
        "publish",
        $projectPath,
        "-c", "Release",
        "-f", $targetFramework,
        "-r", $rid,
        "--self-contained", "true",
        "-p:RestoreForce=true",
        "-p:PublishTrimmed=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-p:TreatWarningsAsErrors=false",
        "-p:ILLinkTreatWarningsAsErrors=false",
        "-o", $resolvedOutputPath
    )
    if ($isWindowsRid) {
        $publishArguments += @("-p:PublishAot=true", "-p:StripSymbols=true", "-p:IlcSingleThreaded=true")
    } else {
        # 全平台 NativeAOT：完整反射元数据保全，单线程 ILC；
        # Apple ld_classic 不支持压缩调试段（-gz=zlib），macOS 保留符号
        $publishArguments += @(
            "-p:PublishAot=true",
            "-p:PublishTrimmed=true",
            "-p:PublishSingleFile=false",
            "-p:IlcGenerateCompleteTypeMetadata=true",
            "-p:IlcTrimMetadata=false",
            "-p:IlcSingleThreaded=true"
        )
        if ($rid.StartsWith("osx-", [StringComparison]::OrdinalIgnoreCase)) {
            $publishArguments += "-p:StripSymbols=false"
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $publishArguments += @("-p:Version=$Version")
    }

    Write-Host "Publishing ClearC.Desktop for $rid..."
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "ClearC.Desktop publish failed for $rid with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $resolvedOutputPath -Recurse -File -Filter "*.pdb" -ErrorAction SilentlyContinue |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $resolvedOutputPath -Recurse -File -Filter "*.xml" -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Host "Published ClearC.Desktop to $resolvedOutputPath"
}
