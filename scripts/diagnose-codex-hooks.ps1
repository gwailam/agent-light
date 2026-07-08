param(
  [string]$HooksPath = (Join-Path (Join-Path $HOME ".codex") "hooks.json"),
  [string]$HostName = "127.0.0.1",
  [int]$Port = 8765
)

$ErrorActionPreference = "Continue"

function Write-Section {
  param([string]$Text)
  Write-Host ""
  Write-Host "== $Text =="
}

function Test-Tcp {
  param(
    [string]$HostName,
    [int]$Port
  )

  $client = New-Object System.Net.Sockets.TcpClient
  try {
    $async = $client.BeginConnect($HostName, $Port, $null, $null)
    if (-not $async.AsyncWaitHandle.WaitOne(1000)) {
      return $false
    }
    $client.EndConnect($async)
    return $true
  } catch {
    return $false
  } finally {
    $client.Close()
  }
}

function Show-Tail {
  param(
    [string]$Path,
    [int]$Count = 20
  )

  if (Test-Path -LiteralPath $Path) {
    Write-Host "-- $Path"
    Get-Content -Encoding UTF8 -LiteralPath $Path -Tail $Count
  } else {
    Write-Host "-- missing: $Path"
  }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$client = Join-Path $repo "hook-client.mjs"
$logDir = Join-Path $repo "service\logs"
$hookClientPattern = [regex]::Escape("hook-client.mjs")

Write-Section "Codex hooks file"
Write-Host "Path: $HooksPath"
Write-Host "Current user: $env:USERNAME"
Write-Host "HOME: $HOME"
if (-not (Test-Path -LiteralPath $HooksPath)) {
  Write-Host "Missing hooks.json. Run Manager -> 安装/修复 Codex Hooks."
} else {
  try {
    $json = Get-Content -Raw -Encoding UTF8 -LiteralPath $HooksPath | ConvertFrom-Json
    Write-Host "JSON: ok"
    foreach ($event in @("UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop")) {
      $value = $json.hooks.$event
      if ($null -eq $value) {
        Write-Host "${event}: missing"
      } elseif ($value -is [array]) {
        Write-Host "${event}: sequence ($($value.Count))"
      } else {
        Write-Host "${event}: INVALID, expected sequence but got $($value.GetType().FullName)"
      }
    }

    Write-Host ""
    Write-Host "Agent Light hook commands:"
    foreach ($event in @("UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop")) {
      foreach ($group in @($json.hooks.$event)) {
        foreach ($hook in @($group.hooks)) {
          $command = if ($hook.commandWindows) { $hook.commandWindows } else { $hook.command }
          if ($command -match $hookClientPattern -or $command -match "Agent Light") {
            $pointsToCurrentClient = $command -like "*$client*"
            Write-Host "$event -> $command"
            Write-Host "  points to current package: $pointsToCurrentClient"
          }
        }
      }
    }
  } catch {
    Write-Host "JSON parse failed: $($_.Exception.Message)"
  }
}

Write-Section "Node and hook client"
try {
  $nodeVersion = & node --version 2>&1
  Write-Host "node: $nodeVersion"
} catch {
  Write-Host "node: not found in PATH"
}
Write-Host "hook-client: $client"
Write-Host "hook-client exists: $(Test-Path -LiteralPath $client)"

Write-Section "Bridge service"
$service = Get-Service -Name AgentLightBridge -ErrorAction SilentlyContinue
if ($service) {
  Write-Host "AgentLightBridge: $($service.Status)"
} else {
  Write-Host "AgentLightBridge: not installed"
}
Write-Host "Local bridge ${HostName}:$Port listening: $(Test-Tcp -HostName $HostName -Port $Port)"

Write-Section "Send through hook-client"
if (Test-Path -LiteralPath $client) {
  & node $client codex:thinking
  Start-Sleep -Milliseconds 400
  & node $client codex:idle
  Write-Host "Sent codex:thinking and codex:idle through hook-client."
} else {
  Write-Host "Skipped because hook-client.mjs is missing."
}

Write-Section "Recent logs"
Show-Tail -Path (Join-Path $logDir "hook-client.log")
Show-Tail -Path (Join-Path $logDir "bridge-service.out.log")
Show-Tail -Path (Join-Path $logDir "bridge-service.err.log")
