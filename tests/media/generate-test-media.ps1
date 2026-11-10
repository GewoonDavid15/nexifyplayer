<#
.SYNOPSIS
  Generates the 3-second test media used by tests/Nexify.Playback.Tests (spec §6.2).
  The files are not part of the repository; CI runs this script and sets NEXIFY_TEST_MEDIA to -OutDir.
.EXAMPLE
  pwsh tests/media/generate-test-media.ps1 -OutDir "$env:RUNNER_TEMP\media"
#>
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Ffmpeg = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $Ffmpeg) {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { $Ffmpeg = $cmd.Source }
    elseif (Test-Path 'C:\ProgramData\chocolatey\bin\ffmpeg.exe') { $Ffmpeg = 'C:\ProgramData\chocolatey\bin\ffmpeg.exe' }
    else { throw 'ffmpeg not found on PATH; pass -Ffmpeg <path>.' }
}
Write-Host "Using ffmpeg: $Ffmpeg"
& $Ffmpeg -hide_banner -version | Select-Object -First 1

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$duration = 3
$tone = "sine=frequency=440:sample_rate=44100:duration=$duration"
$video = "testsrc2=size=320x240:rate=25:duration=$duration"

function Invoke-Ffmpeg([string]$Name, [string[]]$Arguments) {
    $target = Join-Path $OutDir $Name
    Write-Host "Generating $Name"
    & $Ffmpeg -hide_banner -loglevel error -y @Arguments $target
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $Name (exit code $LASTEXITCODE)" }
    if (-not (Test-Path $target) -or (Get-Item $target).Length -eq 0) { throw "ffmpeg produced no output for $Name" }
}

$audioIn = @('-f', 'lavfi', '-i', $tone)
$avIn = @('-f', 'lavfi', '-i', $video, '-f', 'lavfi', '-i', $tone, '-t', "$duration")

# Audio
Invoke-Ffmpeg 'tone.mp3'      ($audioIn + @('-c:a', 'libmp3lame', '-b:a', '128k'))
Invoke-Ffmpeg 'tone.wav'      ($audioIn + @('-c:a', 'pcm_s16le'))
Invoke-Ffmpeg 'tone.flac'     ($audioIn + @('-c:a', 'flac'))
Invoke-Ffmpeg 'tone.ogg'      ($audioIn + @('-c:a', 'libvorbis'))
Invoke-Ffmpeg 'tone-aac.m4a'  ($audioIn + @('-c:a', 'aac', '-b:a', '128k'))
Invoke-Ffmpeg 'tone-alac.m4a' ($audioIn + @('-c:a', 'alac'))
Invoke-Ffmpeg 'tone.wma'      ($audioIn + @('-c:a', 'wmav2', '-b:a', '128k'))

# Video (with an audio track, like real clips)
Invoke-Ffmpeg 'clip.mp4'  ($avIn + @('-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac'))
Invoke-Ffmpeg 'clip.mkv'  ($avIn + @('-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'libvorbis'))
Invoke-Ffmpeg 'clip.avi'  ($avIn + @('-c:v', 'mpeg4', '-c:a', 'libmp3lame'))
Invoke-Ffmpeg 'clip.mov'  ($avIn + @('-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac'))
Invoke-Ffmpeg 'clip.webm' ($avIn + @('-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', '40', '-deadline', 'realtime', '-c:a', 'libopus'))
Invoke-Ffmpeg 'clip.wmv'  ($avIn + @('-c:v', 'wmv2', '-c:a', 'wmav2'))
Invoke-Ffmpeg 'clip.mpeg' ($avIn + @('-c:v', 'mpeg2video', '-c:a', 'mp2', '-f', 'mpeg'))

# Path with characters that need URI escaping
Copy-Item -LiteralPath (Join-Path $OutDir 'tone.mp3') -Destination (Join-Path $OutDir 'Beyoncé #1 100%.mp3') -Force
Copy-Item -LiteralPath (Join-Path $OutDir 'tone.mp3') -Destination (Join-Path $OutDir 'tone %41.mp3') -Force

# 64 KB of random bytes with a video extension
$bytes = New-Object byte[] 65536
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'corrupt.mp4'), $bytes)

Get-ChildItem -LiteralPath $OutDir | Format-Table Name, Length -AutoSize | Out-String | Write-Host
