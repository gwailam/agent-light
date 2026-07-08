param(
  [string]$ArduinoCli = "",
  [string]$ConfigFile = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FirstExistingPath {
  param([string[]]$Candidates)
  foreach ($candidate in $Candidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }
  return ""
}

function Invoke-ArduinoCli {
  param([string[]]$Arguments)
  Write-Host "> $ArduinoCli $($Arguments -join ' ')"
  & $ArduinoCli @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "arduino-cli failed with exit code $LASTEXITCODE"
  }
}

$repo = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath "..")).Path

if (-not $ArduinoCli) {
  $ArduinoCli = Resolve-FirstExistingPath -Candidates @(
    "$repo\tools\arduino-cli\arduino-cli.exe",
    "$repo\..\tools\arduino-cli\arduino-cli.exe"
  )
}

if (-not $ArduinoCli -or -not (Test-Path -LiteralPath $ArduinoCli)) {
  throw "arduino-cli.exe not found. Release firmware build requires Arduino CLI and ESP32 core."
}

if (-not $ConfigFile) {
  $ConfigFile = Resolve-FirstExistingPath -Candidates @(
    "$repo\arduino-cli.yaml",
    "$repo\..\arduino-cli.yaml"
  )
}

if (-not $ConfigFile) {
  throw "arduino-cli.yaml not found."
}

$sourceSketch = Join-Path -Path $repo -ChildPath "firmware\agent-light"
$stagedRoot = Join-Path -Path $repo -ChildPath "build\release-sketch"
$stagedSketch = Join-Path -Path $stagedRoot -ChildPath "agent-light"
$buildPath = Join-Path -Path $repo -ChildPath "build\firmware-release-build"
$releaseDir = Join-Path -Path $repo -ChildPath "firmware-release"

if (Test-Path -LiteralPath $stagedRoot) {
  Remove-Item -LiteralPath $stagedRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagedSketch -Force | Out-Null
New-Item -ItemType Directory -Path $buildPath -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

Copy-Item -LiteralPath (Join-Path -Path $sourceSketch -ChildPath "agent-light.ino") -Destination $stagedSketch
Copy-Item -LiteralPath (Join-Path -Path $sourceSketch -ChildPath "secrets.example.h") -Destination $stagedSketch

$fqbn = "esp32:esp32:esp32:PartitionScheme=huge_app"
Invoke-ArduinoCli @("--config-file", $ConfigFile, "compile", "--fqbn", $fqbn, "--build-path", $buildPath, $stagedSketch)

Copy-Item -LiteralPath (Join-Path -Path $buildPath -ChildPath "agent-light.ino.bootloader.bin") -Destination (Join-Path -Path $releaseDir -ChildPath "agent-light.bootloader.bin") -Force
Copy-Item -LiteralPath (Join-Path -Path $buildPath -ChildPath "agent-light.ino.partitions.bin") -Destination (Join-Path -Path $releaseDir -ChildPath "agent-light.partitions.bin") -Force
Copy-Item -LiteralPath (Join-Path -Path $buildPath -ChildPath "boot_app0.bin") -Destination (Join-Path -Path $releaseDir -ChildPath "boot_app0.bin") -Force
Copy-Item -LiteralPath (Join-Path -Path $buildPath -ChildPath "agent-light.ino.bin") -Destination (Join-Path -Path $releaseDir -ChildPath "agent-light.bin") -Force

$manifest = @"
Agent Light firmware release

Board: esp32:esp32:esp32
Partition: Huge APP
Flash size: 4MB
Flash mode: dio
Flash frequency: 80m

Offsets:
0x1000  agent-light.bootloader.bin
0x8000  agent-light.partitions.bin
0xe000  boot_app0.bin
0x10000 agent-light.bin

This release build intentionally excludes firmware/agent-light/secrets.h.
"@
[System.IO.File]::WriteAllText((Join-Path -Path $releaseDir -ChildPath "README.txt"), $manifest, [System.Text.Encoding]::UTF8)

Write-Host "Built firmware release: $releaseDir"
