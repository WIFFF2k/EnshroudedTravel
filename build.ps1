# 使用 Windows 自带编译器构建桌面助手
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$files=Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -Recurse | ForEach-Object {$_.FullName}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /out:$root/EnshroudedTravel.exe $files
if($LASTEXITCODE -ne 0){throw '编译失败'}
Get-FileHash -LiteralPath (Join-Path $root 'EnshroudedTravel.exe') -Algorithm SHA256
