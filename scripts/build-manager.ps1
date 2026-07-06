param(
  [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = Join-Path $repo 'manager\AgentLightManager.cs'
$output = Join-Path $repo 'manager\AgentLightManager.exe'
$manifest = Join-Path $repo 'manager\AgentLightManager.exe.manifest'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'

if (-not (Test-Path $source)) {
  throw "Manager source not found: $source"
}

if (-not (Test-Path $csc)) {
  throw "C# compiler not found: $csc"
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /utf8output /out:$output "/win32manifest:$manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Xaml.dll "/reference:$(Join-Path $wpf 'WindowsBase.dll')" "/reference:$(Join-Path $wpf 'PresentationCore.dll')" "/reference:$(Join-Path $wpf 'PresentationFramework.dll')" /reference:System.ServiceProcess.dll /reference:System.Security.dll /reference:System.Management.dll $source
if ($LASTEXITCODE -ne 0) {
  throw "csc failed with exit code $LASTEXITCODE"
}

Write-Host "Built $output"
