#requires -Version 5.1
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw '未找到系统自带的 C# 编译器 csc.exe。' }
if (-not (Test-Path $src)) { throw '未找到 src 目录。' }

New-Item -ItemType Directory -Force -Path $bin | Out-Null
Get-ChildItem -LiteralPath $bin -Force | Remove-Item -Recurse -Force

$sources = Get-ChildItem -LiteralPath $src -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName }
$manifest = Join-Path $src 'app.manifest'
$icon = Join-Path $src 'app.ico'
$output = Join-Path $bin 'PCCheck.exe'

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /debug- /langversion:5 /codepage:65001 /warn:4 `
    /win32manifest:$manifest /win32icon:$icon /out:$output `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll `
    $sources

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$files = @(Get-ChildItem -LiteralPath $bin -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'PCCheck.exe') {
    throw 'bin 目录不满足单文件要求。'
}
if ($files[0].Length -gt 5MB) {
    throw "产物超过 5 MB：$($files[0].Length) 字节"
}

$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
Write-Host ''
Write-Host 'PCCheck 构建完成'
Write-Host ("产物：" + $output)
Write-Host ("大小：" + $files[0].Length + " 字节（" + [Math]::Round($files[0].Length / 1KB, 1) + " KB）")
Write-Host ("SHA256：" + $hash)
Write-Host ''
$files | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
