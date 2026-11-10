<#
.SYNOPSIS
  Generates longer media with metadata for the player screenshots in tests/Nexify.App.UiTests (Player_Screenshots).
  CI runs this script and sets NEXIFY_SHOWCASE_MEDIA to -OutDir. The files are not part of the repository.
#>
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Ffmpeg = 'ffmpeg'
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-Media([string]$Name, [string[]]$Arguments) {
    $target = Join-Path $OutDir $Name
    Write-Host "Generating $Name"
    & $Ffmpeg -hide_banner -loglevel error -y @Arguments $target
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed for $Name (exit code $LASTEXITCODE)" }
}

function Tone([int]$Frequency, [int]$Seconds) { @('-f', 'lavfi', '-i', "sine=frequency=${Frequency}:sample_rate=44100:duration=$Seconds") }
function Meta([string]$Title, [string]$Artist) { @('-metadata', "title=$Title", '-metadata', "artist=$Artist") }

New-Media 'Nexify Demo.mp4' (@('-f', 'lavfi', '-i', 'testsrc2=size=1280x720:rate=30:duration=90') + (Tone 330 90) +
    @('-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-t', '90') + (Meta 'Nexify Demo' 'Next Solutions'))
New-Media 'City Lights.mp4' (@('-f', 'lavfi', '-i', 'testsrc=size=640x360:rate=25:duration=30') + (Tone 392 30) +
    @('-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-t', '30'))
New-Media 'Evening Drive.mp3' ((Tone 440 200) + @('-c:a', 'libmp3lame', '-b:a', '128k') + (Meta 'Evening Drive' 'Nexify Ensemble'))
New-Media 'Morning Light.flac' ((Tone 523 180) + @('-c:a', 'flac') + (Meta 'Morning Light' 'Nexify Ensemble'))
New-Media 'Night Walk.ogg' ((Tone 294 150) + @('-c:a', 'libvorbis'))
Get-ChildItem $OutDir | Format-Table Name, Length
