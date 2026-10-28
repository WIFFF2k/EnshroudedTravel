# 将运行资源内置到仓库根目录程序，用户拉取后无需编译
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$resourceArgs=@()
foreach($name in @('registry-original.bin','zoom-original.bin','storage-recipes-original.bin')) {
    $path=Join-Path (Join-Path $root 'payload') $name
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw ('构建所需的本地资源缺失：'+$name)}
    $resourceArgs+=('/resource:'+ $path + ',EnshroudedTravel.Templates.' + $name)
}
$temporary=Join-Path $root 'EnshroudedTravel.next.exe'
$target=Join-Path $root 'EnshroudedTravel.exe'
$files=Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -Recurse | ForEach-Object {$_.FullName}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /out:$temporary $resourceArgs $files
if($LASTEXITCODE -ne 0){throw '编译失败，原程序保持不变'}
Copy-Item -LiteralPath $temporary -Destination $target -Force
Get-FileHash -LiteralPath $target -Algorithm SHA256
Write-Output ('仓库启动程序：'+$target)
