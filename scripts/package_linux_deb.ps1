# 打包 Linux .deb 安装包（dpkg-deb，在 ubuntu runner 上运行）。
# 用法：pwsh scripts/package_linux_deb.ps1 -RuntimeIdentifier linux-x64 -Version 1.2.3
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "linux-arm64")]
    [string] $RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $SourceDirectory = "",
    [string] $OutputDirectory = "",
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$scriptRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $repositoryRoot "artifacts/publish/$RuntimeIdentifier/ClearC"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/release"
}

if (-not (Test-Path -LiteralPath $SourceDirectory)) {
    throw "Publish output was not produced: $SourceDirectory"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$architecture = if ($RuntimeIdentifier -eq "linux-arm64") { "arm64" } else { "amd64" }
$packageName = "ClearC-v$Version-$RuntimeIdentifier.deb"
$outputPath = Join-Path $OutputDirectory $packageName
if ((Test-Path -LiteralPath $outputPath) -and -not $Force) {
    throw "Output already exists (use -Force to overwrite): $outputPath"
}

$staging = Join-Path $repositoryRoot "artifacts/deb/$RuntimeIdentifier/clearc"
$staging = [IO.Path]::GetFullPath($staging)
$stagingPrefix = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/deb')).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $staging.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected staging path: $staging" }
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $staging "DEBIAN") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $staging "usr/local/share/ClearC") -Force | Out-Null

# 程序文件
Copy-Item -Path (Join-Path $SourceDirectory "*") -Destination (Join-Path $staging "usr/local/share/ClearC/") -Recurse -Force
# 调试符号保留在独立构建产物，不进入安装包。
Get-ChildItem -LiteralPath (Join-Path $staging 'usr/local/share/ClearC') -Recurse -File |
    Where-Object { $_.Extension -in '.pdb', '.dbg' } | Remove-Item -Force

# 安装后提供标准命令和桌面入口；功能仍遵循应用自身的平台限制。
$launcherDirectory = Join-Path $staging 'usr/local/bin'
$desktopDirectory = Join-Path $staging 'usr/share/applications'
$iconDirectory = Join-Path $staging 'usr/share/pixmaps'
New-Item -ItemType Directory -Path $launcherDirectory, $desktopDirectory, $iconDirectory -Force | Out-Null
$launcherPath = Join-Path $launcherDirectory 'clearc'
[IO.File]::WriteAllText($launcherPath, '#!/bin/sh' + "`n" + 'exec /usr/local/share/ClearC/ClearC.Desktop "$@"' + "`n")
$desktopPath = Join-Path $desktopDirectory 'clearc.desktop'
$desktop = @'
[Desktop Entry]
Type=Application
Name=ClearC
Comment=Windows disk cleanup tool; other platforms are not supported yet
Exec=clearc
Icon=clearc
Terminal=false
Categories=Utility;System;
'@
[IO.File]::WriteAllText($desktopPath, $desktop.ReplaceLineEndings("`n") + "`n")
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'logo.png') -Destination (Join-Path $iconDirectory 'clearc.png') -Force

# control（dpkg-deb 要求 DEBIAN/control 为 root 所属、644）
$installedSizeKb = [Math]::Ceiling(
    ((Get-ChildItem -LiteralPath (Join-Path $staging 'usr') -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB)
$control = @"
Package: clearc
Version: $Version
Architecture: $architecture
Maintainer: Dotnet9 <dotnet9.com@gmail.com>
Installed-Size: $installedSizeKb
Depends: libfontconfig1, libx11-6
Section: utils
Priority: optional
Description: ClearC 磁盘清理工具（Windows 功能为主，其他平台正在开发中）
"@
Set-Content -LiteralPath (Join-Path $staging "DEBIAN/control") -Value ($control + [Environment]::NewLine) -Encoding UTF8

# dpkg-deb 需要 posix 权限语义；control 文件权限固定 644
if ($IsLinux) {
    $null = chmod 644 (Join-Path $staging "DEBIAN/control")
    if ($LASTEXITCODE -ne 0) { throw 'Failed to set control file permissions.' }
    $null = chmod 755 $launcherPath (Join-Path $staging 'usr/local/share/ClearC/ClearC.Desktop')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to set executable permissions.' }
}

dpkg-deb --build --root-owner-group $staging $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "dpkg-deb failed for $RuntimeIdentifier"
}
if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw "Deb was not produced: $outputPath"
}

$sha = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$sha  $packageName" | Set-Content -LiteralPath "$outputPath.sha256" -Encoding ASCII

Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Deb 打包完成：$outputPath"
