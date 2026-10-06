$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$release = Join-Path $root 'release\音乐助手-0.5'
New-Item -ItemType Directory -Force -Path (Join-Path $release 'tools'), (Join-Path $release 'examples') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $release 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'dist\MusicAssistant.exe') -Destination $release
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $release '使用说明.md')
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY.md') -Destination $release
Get-ChildItem -LiteralPath (Join-Path $root 'licenses') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $release 'licenses') }
Copy-Item -LiteralPath (Join-Path $root 'examples\歌单模板.csv'), (Join-Path $root 'examples\歌单模板.json') -Destination (Join-Path $release 'examples')
Copy-Item -LiteralPath (Join-Path $root 'dist\tools\ffmpeg.exe'), (Join-Path $root 'dist\tools\ffprobe.exe') -Destination (Join-Path $release 'tools')
Copy-Item -LiteralPath (Join-Path $root 'dist\tools\ffmpeg-9.0.2-essentials_build\LICENSE') -Destination (Join-Path $release 'tools\FFmpeg-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'dist\tools\ffmpeg-9.0.2-essentials_build\README.txt') -Destination (Join-Path $release 'tools\FFmpeg-README.txt')
@'
FFmpeg version: 9.0.2 essentials x64, Gyan build
Build source: https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2
FFmpeg corresponding source: https://github.com/FFmpeg/FFmpeg/commit/946fcce07b
Build source/configuration details: FFmpeg-README.txt
License: GPLv3; see FFmpeg-LICENSE.txt
Verified archive SHA256: 4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077
'@ | Set-Content -LiteralPath (Join-Path $release 'tools\SOURCE.txt') -Encoding utf8
Compress-Archive -LiteralPath $release -DestinationPath (Join-Path $root 'release\音乐助手-0.5-win-x64.zip') -Force
Write-Output 'release\音乐助手-0.5-win-x64.zip'
