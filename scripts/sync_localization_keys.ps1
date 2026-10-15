param([switch] $Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'T4 工具恢复失败。' }
    $template = Join-Path $root 'src/ClearC.Desktop/I18n/Language.tt'
    $output = Join-Path $root 'src/ClearC.Desktop/I18n/Language.cs'
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('clearc-t4-' + [Guid]::NewGuid().ToString('N') + '.cs')
    try {
        dotnet tool run t4 -- $template -o $temporary
        if ($LASTEXITCODE -ne 0) { throw 'T4 资源键生成失败。' }
        $generated = [IO.File]::ReadAllText($temporary).ReplaceLineEndings("`n").TrimEnd() + "`n"
        if ($Check) {
            if ([IO.File]::ReadAllText($output).ReplaceLineEndings("`n") -cne $generated) { throw '强类型资源键未同步。' }
        } else { [IO.File]::WriteAllText($output, $generated, [Text.UTF8Encoding]::new($false)) }
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
} finally { Pop-Location }
