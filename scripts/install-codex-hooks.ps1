param(
  [string]$HooksPath = (Join-Path (Join-Path $HOME ".codex") "hooks.json"),
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function ConvertTo-Hashtable {
  param([object]$Value)

  if ($null -eq $Value) {
    return $null
  }

  if ($Value -is [System.Collections.IDictionary]) {
    $result = [ordered]@{}
    foreach ($key in $Value.Keys) {
      $result[$key] = ConvertTo-Hashtable $Value[$key]
    }
    return $result
  }

  if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
    $result = New-Object System.Collections.ArrayList
    foreach ($item in $Value) {
      [void]$result.Add((ConvertTo-Hashtable $item))
    }
    return ,$result
  }

  if ($Value.PSObject.TypeNames -contains "System.Management.Automation.PSCustomObject") {
    $result = [ordered]@{}
    foreach ($property in $Value.PSObject.Properties) {
      $result[$property.Name] = ConvertTo-Hashtable $property.Value
    }
    return $result
  }

  return $Value
}

function ConvertTo-SequenceList {
  param([object]$Value)

  $result = New-Object System.Collections.ArrayList
  if ($null -eq $Value) {
    return ,$result
  }

  if ($Value -is [System.Collections.IDictionary] -or $Value -is [string]) {
    [void]$result.Add($Value)
    return ,$result
  }

  if ($Value -is [System.Collections.IEnumerable]) {
    foreach ($item in $Value) {
      [void]$result.Add($item)
    }
    return ,$result
  }

  [void]$result.Add($Value)
  return ,$result
}

function Normalize-HookSequences {
  param([System.Collections.IDictionary]$Hooks)

  foreach ($event in @($Hooks.Keys)) {
    $groups = ConvertTo-SequenceList $Hooks[$event]
    foreach ($group in $groups) {
      if ($group -is [System.Collections.IDictionary] -and $group.Contains("hooks")) {
        $group["hooks"] = ConvertTo-SequenceList $group["hooks"]
      }
    }
    $Hooks[$event] = $groups
  }
}

function New-AgentLightHook {
  param([string]$State)

  $windowsClient = $script:ClientPath
  $portableClient = $script:ClientPath.Replace("\", "/")

  return [ordered]@{
    type = "command"
    command = "node `"$portableClient`" $State"
    commandWindows = "node `"$windowsClient`" $State"
    statusMessage = "Agent Light $State"
  }
}

function New-HookGroup {
  param(
    [string]$State,
    [string]$Matcher = $null
  )

  $group = [ordered]@{}
  if ($Matcher) {
    $group.matcher = $Matcher
  }
  $hooks = New-Object System.Collections.ArrayList
  [void]$hooks.Add((New-AgentLightHook $State))
  $group.hooks = $hooks
  return $group
}

function Test-AgentLightHook {
  param([object]$Hook)

  $command = ""
  if ($Hook -is [System.Collections.IDictionary]) {
    $command = "$($Hook.command) $($Hook.commandWindows) $($Hook.statusMessage)"
  } else {
    $command = "$($Hook.command) $($Hook.commandWindows) $($Hook.statusMessage)"
  }

  return $command -match "hook-client\.mjs" -or $command -match "Agent Light codex:"
}

function Remove-ExistingAgentLightHooks {
  param(
    [System.Collections.IDictionary]$Hooks,
    [string]$Event
  )

  if (-not $Hooks.Contains($Event)) {
    return
  }

  $keptGroups = New-Object System.Collections.ArrayList
  foreach ($group in (ConvertTo-SequenceList $Hooks[$Event])) {
    if (-not ($group -is [System.Collections.IDictionary]) -or -not $group.Contains("hooks")) {
      [void]$keptGroups.Add($group)
      continue
    }

    $keptHandlers = New-Object System.Collections.ArrayList
    foreach ($hook in (ConvertTo-SequenceList $group["hooks"])) {
      if (-not (Test-AgentLightHook $hook)) {
        [void]$keptHandlers.Add($hook)
      }
    }

    if ($keptHandlers.Count -gt 0) {
      $group["hooks"] = $keptHandlers
      [void]$keptGroups.Add($group)
    }
  }

  if ($keptGroups.Count -gt 0) {
    $Hooks[$Event] = $keptGroups
  } else {
    $Hooks.Remove($Event)
  }
}

function Add-AgentLightGroup {
  param(
    [System.Collections.IDictionary]$Hooks,
    [string]$Event,
    [object]$Group
  )

  $groups = New-Object System.Collections.ArrayList
  if ($Hooks.Contains($Event)) {
    foreach ($group in (ConvertTo-SequenceList $Hooks[$Event])) {
      [void]$groups.Add($group)
    }
  }
  [void]$groups.Add($Group)
  $Hooks[$Event] = $groups
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$script:ClientPath = Join-Path $repo "hook-client.mjs"

if (-not (Test-Path -LiteralPath $script:ClientPath)) {
  throw "Cannot find hook client: $script:ClientPath"
}

$config = [ordered]@{ hooks = [ordered]@{} }
if (Test-Path -LiteralPath $HooksPath) {
  $raw = Get-Content -Raw -Encoding UTF8 -LiteralPath $HooksPath
  if ($raw.Trim()) {
    $config = ConvertTo-Hashtable ($raw | ConvertFrom-Json)
  }
}

if (-not $config.Contains("hooks") -or $null -eq $config["hooks"]) {
  $config["hooks"] = [ordered]@{}
}

$hooks = $config["hooks"]
Normalize-HookSequences -Hooks $hooks
foreach ($event in @("UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop")) {
  Remove-ExistingAgentLightHooks -Hooks $hooks -Event $event
}

Add-AgentLightGroup -Hooks $hooks -Event "UserPromptSubmit" -Group (New-HookGroup -State "codex:thinking")
Add-AgentLightGroup -Hooks $hooks -Event "PreToolUse" -Group (New-HookGroup -State "codex:running" -Matcher "*")
Add-AgentLightGroup -Hooks $hooks -Event "PostToolUse" -Group (New-HookGroup -State "codex:thinking" -Matcher "*")
Add-AgentLightGroup -Hooks $hooks -Event "Stop" -Group (New-HookGroup -State "codex:idle")

$json = $config | ConvertTo-Json -Depth 20

if ($DryRun) {
  Write-Host $json
  exit 0
}

$hooksDir = Split-Path -Parent $HooksPath
if (-not (Test-Path -LiteralPath $hooksDir)) {
  New-Item -ItemType Directory -Path $hooksDir | Out-Null
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($HooksPath, "$json`r`n", $utf8NoBom)

Write-Host "Installed Codex hooks: $HooksPath"
Write-Host "Hook client: $script:ClientPath"
Write-Host "Open /hooks in Codex and trust the updated Agent Light hooks."
