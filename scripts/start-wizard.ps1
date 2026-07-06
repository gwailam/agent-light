$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repo 'wizard\AgentLightSetupWizard.cs'
$exe = Join-Path $repo 'wizard\AgentLightSetupWizard.exe'
$build = Join-Path $PSScriptRoot 'build-wizard.ps1'

if (-not (Test-Path -LiteralPath $exe) -or ((Get-Item -LiteralPath $source).LastWriteTime -gt (Get-Item -LiteralPath $exe).LastWriteTime)) {
  & $build
}

Start-Process -FilePath $exe -WorkingDirectory $repo
