param(
  [string]$DriverPath = ""
)

$ErrorActionPreference = "Stop"

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw "请用以管理员身份运行的 PowerShell 执行 CP210x 驱动安装脚本。"
}

if (-not $DriverPath) {
  $repo = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath "..")).Path
  $DriverPath = Join-Path -Path $repo -ChildPath "CP210x_Universal_Windows_Driver"
}

$driverRoot = (Resolve-Path -LiteralPath $DriverPath).Path
$infPath = Join-Path -Path $driverRoot -ChildPath "silabser.inf"
if (-not (Test-Path -LiteralPath $infPath)) {
  throw "找不到 CP210x 驱动 INF 文件: $infPath"
}

$pnputil = Join-Path -Path $env:WINDIR -ChildPath "System32\pnputil.exe"
if (-not (Test-Path -LiteralPath $pnputil)) {
  throw "找不到 pnputil.exe，无法安装驱动。"
}

Write-Host "Installing CP210x driver: $infPath"
& $pnputil /add-driver $infPath /install
if ($LASTEXITCODE -ne 0) {
  throw "pnputil failed with exit code $LASTEXITCODE"
}

Write-Host "CP210x driver installation completed."
