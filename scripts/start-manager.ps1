$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repo 'manager\AgentLightManager.cs'
$exe = Join-Path $repo 'manager\AgentLightManager.exe'
$build = Join-Path $PSScriptRoot 'build-manager.ps1'

if (-not (Test-Path -LiteralPath $exe) -or ((Get-Item -LiteralPath $source).LastWriteTime -gt (Get-Item -LiteralPath $exe).LastWriteTime)) {
  & $build
}

Start-Process -FilePath $exe -WorkingDirectory $repo
