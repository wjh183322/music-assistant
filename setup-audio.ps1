$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$tools = Join-Path $root 'dist\tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$archive = Join-Path $tools 'ffmpeg.7z'
& curl.exe -L --fail --retry 1 --connect-timeout 20 --max-time 300 --silent --show-error 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.7z' -o $archive
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg 下载失败' }
# SHA256 published by the build provider, pinned to this specific release.
$expected = '4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077'
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actual -ine $expected) { throw 'FFmpeg 下载文件校验不通过' }
$extractor = Join-Path $tools '7zr.exe'
if (-not (Test-Path -LiteralPath $extractor)) {
    & curl.exe -L --fail --connect-timeout 20 --max-time 60 --silent --show-error 'https://www.7-zip.org/a/7zr.exe' -o $extractor
    if ($LASTEXITCODE -ne 0) { throw '解压组件下载失败' }
}
& $extractor x $archive "-o$tools" -y
if ($LASTEXITCODE -ne 0) { throw '解压失败' }
$package = Join-Path $tools 'ffmpeg-9.0.2-essentials_build'
Copy-Item -LiteralPath (Join-Path $package 'bin\ffmpeg.exe') -Destination $tools
Copy-Item -LiteralPath (Join-Path $package 'bin\ffprobe.exe') -Destination $tools
Write-Output "音频组件已准备，SHA256：$actual"
