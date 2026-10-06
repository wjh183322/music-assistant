$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$vb = (Get-ChildItem -LiteralPath "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic" -Recurse -Filter 'Microsoft.VisualBasic.dll' | Select-Object -First 1).FullName
New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /utf8output /target:winexe /platform:x64 /optimize+ /out:"$root\dist\MusicAssistant.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:$vb $sources
if ($LASTEXITCODE -ne 0) { throw '应用编译失败' }
& $compiler /nologo /utf8output /target:exe /platform:x64 /out:"$root\dist\CoreTests.exe" /r:System.Web.Extensions.dll /r:$vb "$root\src\Core.cs" "$root\src\DataDirectory.cs" "$root\src\Audio.cs" "$root\src\Imports.cs" "$root\src\Recycle.cs" "$root\src\PublicPlaylist.cs" "$root\src\SourceDecoder.cs" "$root\src\LocalCatalog.cs" "$root\src\QQKeySession.cs" "$root\tests\CoreTests.cs" "$root\tests\DecoderTests.cs" "$root\tests\StartupTests.cs" "$root\tests\PlaylistTests.cs"
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
Write-Output '编译完成：dist\MusicAssistant.exe'
