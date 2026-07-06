param(
  [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repo 'service\AgentLightBridgeService.cs'
$output = Join-Path $repo 'service\AgentLightBridgeService.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $source)) {
  throw "Service source not found: $source"
}

if (-not (Test-Path $csc)) {
  throw "C# compiler not found: $csc"
}

& $csc /nologo /target:exe /platform:x64 /optimize+ /out:$output /reference:System.dll /reference:System.ServiceProcess.dll $source
if ($LASTEXITCODE -ne 0) {
  throw "csc failed with exit code $LASTEXITCODE"
}

Write-Host "Built $output"