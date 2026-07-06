#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { normalizeCommand } from '../lib/commands.mjs';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const serviceConfigPath = path.join(repoRoot, 'service', 'agent-light-service.ini');
const serviceName = 'AgentLightBridge';

const args = process.argv.slice(2);
const command = args[0] ?? 'help';

try {
  if (command === 'help' || command === '--help' || command === '-h') {
    printHelp();
  } else if (command === 'status') {
    await showStatus();
  } else if (command === 'scan') {
    await scanDevices();
  } else if (command === 'use') {
    await useTransport(args.slice(1));
  } else if (command === 'test') {
    await sendServiceCommand(args.slice(1).join(' ') || 'codex:thinking');
  } else if (command === 'command') {
    await sendDirectCommand(args.slice(1));
  } else if (command === 'wifi') {
    await configureWiFi(args.slice(1));
  } else {
    throw new Error(`Unknown device command: ${command}`);
  }
} catch (error) {
  console.error(error.message);
  process.exit(1);
}

function printHelp() {
  console.log(`Agent Light device manager

Usage:
  npm run device -- status
  npm run device -- scan
  npm run device -- use serial COM4
  npm run device -- use wifi 10.16.0.39 8766
  npm run device -- test codex:thinking
  npm run device -- command sys:info
  npm run device -- command --tcp 10.16.0.39:8766 sys:info
  npm run device -- command --serial COM4 sys:ping
  npm run device -- wifi --ssid ioT_P --password 88888888
  npm run device -- wifi --ssid ioT_P --password 88888888 --tcp 10.16.0.39:8766
  npm run device -- wifi --ssid ioT_P --password 88888888 --serial COM4
`);
}

async function showStatus() {
  const config = readServiceConfig();
  const status = getServiceStatus();

  console.log(`Service: ${status}`);
  console.log(`Transport: ${config.Transport ?? 'serial'}`);
  console.log(`Listen: 127.0.0.1:${config.Listen ?? '8765'}`);

  if (isTcpTransport(config.Transport)) {
    console.log(`Device: ${config.DeviceHost ?? 'agent-light.local'}:${config.DevicePort ?? '8766'}`);
    const reachable = await testTcp(config.DeviceHost ?? 'agent-light.local', Number(config.DevicePort ?? 8766));
    console.log(`Device TCP: ${reachable ? 'reachable' : 'unreachable'}`);
  } else {
    console.log(`Serial: ${config.Serial ?? 'COM3'} @ ${config.Baud ?? '9600'}`);
  }

  const bridgeReachable = await testTcp('127.0.0.1', Number(config.Listen ?? 8765));
  console.log(`Local bridge: ${bridgeReachable ? 'listening' : 'not listening'}`);
  if (bridgeReachable) {
    const response = await sendTcpCommand('127.0.0.1', Number(config.Listen ?? 8765), 'sys:info', true);
    console.log(`Device response: ${response || 'no response'}`);
  }
}

async function scanDevices() {
  console.log('Serial ports:');
  const ports = listSerialPorts();
  if (ports.length === 0) {
    console.log('  none');
  } else {
    for (const port of ports) {
      console.log(`  ${port.DeviceID} - ${port.Name}`);
    }
  }

  console.log('');
  console.log('Network targets:');
  const candidates = [
    { host: 'agent-light.local', port: 8766 },
    { host: readServiceConfig().DeviceHost ?? '10.16.0.39', port: Number(readServiceConfig().DevicePort ?? 8766) },
  ];

  const seen = new Set();
  for (const candidate of candidates) {
    const key = `${candidate.host}:${candidate.port}`;
    if (seen.has(key)) continue;
    seen.add(key);

    const reachable = await testTcp(candidate.host, candidate.port);
    console.log(`  ${key} - ${reachable ? 'reachable' : 'unreachable'}`);
  }
}

async function useTransport(parts) {
  const kind = (parts[0] ?? '').toLowerCase();
  if (!kind) {
    throw new Error('Usage: npm run device -- use serial COM4 | use wifi 10.16.0.39 8766');
  }

  if (kind === 'serial' || kind === 'usb' || kind === 'bluetooth') {
    const serial = parts[1];
    if (!/^COM\d+$/i.test(serial ?? '')) {
      throw new Error('Usage: npm run device -- use serial COM4');
    }

    updateServiceConfig({
      Transport: 'serial',
      Serial: serial.toUpperCase(),
    });
    restartService();
    console.log(`Service transport set to serial ${serial.toUpperCase()}.`);
    return;
  }

  if (kind === 'wifi' || kind === 'tcp') {
    const host = parts[1] ?? 'agent-light.local';
    const port = Number(parts[2] ?? 8766);
    if (!Number.isInteger(port) || port <= 0) {
      throw new Error('Invalid TCP port');
    }

    updateServiceConfig({
      Transport: 'tcp',
      DeviceHost: host,
      DevicePort: String(port),
    });
    restartService();
    console.log(`Service transport set to TCP ${host}:${port}.`);
    return;
  }

  throw new Error(`Unknown transport: ${kind}`);
}

async function sendServiceCommand(rawCommand) {
  const config = readServiceConfig();
  const command = normalizeIfLightCommand(rawCommand);
  await sendTcpCommand('127.0.0.1', Number(config.Listen ?? 8765), command, false);
  console.log(`sent via service: ${command}`);
}

async function sendDirectCommand(parts) {
  const target = parseDirectTarget(parts);
  const command = target.remaining.join(' ');
  if (!command) {
    throw new Error('Missing command');
  }

  const response = await sendDirect(target, command);
  console.log(response || 'no response');
}

async function configureWiFi(parts) {
  const options = parseOptions(parts);
  const ssid = options.ssid;
  const password = options.password;

  if (!ssid || !password) {
    throw new Error('Usage: npm run device -- wifi --ssid <ssid> --password <password> [--tcp host:port | --serial COM4]');
  }

  const target = options.serial
    ? { type: 'serial', port: options.serial }
    : options.tcp
      ? parseTcpOption(options.tcp)
      : getServiceTarget();

  const response = await sendDirect(target, `wifi:set:${ssid}:${password}`);
  console.log(response || 'no response');
}

async function sendDirect(target, command) {
  if (target.type === 'tcp') {
    return sendTcpCommand(target.host, target.port, command, true);
  }

  if (target.type === 'service') {
    return sendTcpCommand(target.host, target.port, command, true);
  }

  if (target.type === 'serial') {
    return sendSerialCommand(target.port, command);
  }

  throw new Error(`Unknown target type: ${target.type}`);
}

function parseDirectTarget(parts) {
  if (parts[0] === '--tcp') {
    if (!parts[1]) throw new Error('Missing --tcp host:port');
    return { ...parseTcpOption(parts[1]), remaining: parts.slice(2) };
  }

  if (parts[0] === '--serial') {
    if (!parts[1]) throw new Error('Missing --serial COMx');
    return { type: 'serial', port: parts[1].toUpperCase(), remaining: parts.slice(2) };
  }

  return {
    ...getServiceTarget(),
    remaining: parts,
  };
}

function getServiceTarget() {
  const config = readServiceConfig();
  return {
    type: 'service',
    host: '127.0.0.1',
    port: Number(config.Listen ?? 8765),
  };
}

function parseTcpOption(value) {
  const [host, portText] = String(value).split(':');
  const port = Number(portText ?? 8766);
  if (!host || !Number.isInteger(port) || port <= 0) {
    throw new Error(`Invalid TCP target: ${value}`);
  }

  return { type: 'tcp', host, port };
}

function parseOptions(parts) {
  const options = {};
  for (let index = 0; index < parts.length; index += 1) {
    const part = parts[index];
    if (part === '--ssid') {
      options.ssid = parts[++index];
    } else if (part === '--password') {
      options.password = parts[++index];
    } else if (part === '--tcp') {
      options.tcp = parts[++index];
    } else if (part === '--serial') {
      options.serial = parts[++index]?.toUpperCase();
    } else {
      throw new Error(`Unknown option: ${part}`);
    }
  }
  return options;
}

function normalizeIfLightCommand(rawCommand) {
  if (/^(sys|wifi):/i.test(rawCommand) || /^reboot$/i.test(rawCommand)) {
    return rawCommand;
  }

  return normalizeCommand(rawCommand);
}

function readServiceConfig() {
  if (!fs.existsSync(serviceConfigPath)) {
    return {
      ServiceName: serviceName,
      Transport: 'serial',
      Serial: 'COM3',
      Baud: '9600',
      Listen: '8765',
      DeviceHost: 'agent-light.local',
      DevicePort: '8766',
      Initial: 'claude:idle',
    };
  }

  const config = {};
  const text = fs.readFileSync(serviceConfigPath, 'utf8');
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#') || line.startsWith(';')) continue;
    const equals = line.indexOf('=');
    if (equals <= 0) continue;
    config[line.slice(0, equals).trim()] = line.slice(equals + 1).trim();
  }

  return config;
}

function updateServiceConfig(updates) {
  const config = {
    ServiceName: serviceName,
    NodePath: 'C:\\Program Files\\nodejs\\node.exe',
    RepoRoot: repoRoot,
    Transport: 'serial',
    Serial: 'COM3',
    Baud: '9600',
    Listen: '8765',
    DeviceHost: 'agent-light.local',
    DevicePort: '8766',
    Initial: 'claude:idle',
    ...readServiceConfig(),
    ...updates,
  };

  const order = ['ServiceName', 'NodePath', 'RepoRoot', 'Transport', 'Serial', 'Baud', 'Listen', 'DeviceHost', 'DevicePort', 'Initial'];
  const text = `${order.map((key) => `${key}=${config[key]}`).join('\r\n')}\r\n`;
  fs.mkdirSync(path.dirname(serviceConfigPath), { recursive: true });
  fs.writeFileSync(serviceConfigPath, text, 'utf8');
}

function getServiceStatus() {
  try {
    return runPowerShell(`$s = Get-Service -Name ${quotePowerShell(serviceName)} -ErrorAction Stop; "$($s.Status) / $($s.StartType)"`).trim();
  } catch {
    return 'not installed';
  }
}

function restartService() {
  try {
    runPowerShell(`Restart-Service -Name ${quotePowerShell(serviceName)} -Force; Start-Sleep -Seconds 2`);
  } catch (error) {
    console.warn(`Warning: service restart failed. Run as Administrator or restart it manually. ${error.message}`);
  }
}

function listSerialPorts() {
  try {
    const output = runPowerShell(
      "Get-CimInstance Win32_SerialPort | Select-Object DeviceID,Name,Description,PNPDeviceID,Status | ConvertTo-Json -Compress",
    );
    if (!output.trim()) return [];
    const parsed = JSON.parse(output);
    return Array.isArray(parsed) ? parsed : [parsed];
  } catch {
    return [];
  }
}

async function testTcp(host, port) {
  try {
    await sendTcp(host, port, '', false, 700);
    return true;
  } catch {
    return false;
  }
}

async function sendTcpCommand(host, port, command, readResponse) {
  return sendTcp(host, port, command, readResponse, 2500);
}

function sendTcp(host, port, command, readResponse, timeoutMs) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host, port });
    let response = '';
    let finished = false;

    const finish = (error) => {
      if (finished) return;
      finished = true;
      socket.destroy();
      if (error) reject(error);
      else resolve(response.trim());
    };

    socket.setTimeout(timeoutMs, () => finish(new Error(`TCP timeout: ${host}:${port}`)));
    socket.on('connect', () => {
      if (command) {
        socket.write(`${command}\n`, () => {
          if (!readResponse) socket.end();
        });
      } else if (!readResponse) {
        socket.end();
      }
    });
    socket.on('data', (chunk) => {
      response += chunk.toString('utf8');
      if (response.includes('\n')) finish();
    });
    socket.on('error', finish);
    socket.on('close', () => {
      finish();
    });
  });
}

function sendSerialCommand(port, command) {
  const script = `
$ErrorActionPreference = "Stop"
$serviceName = ${quotePowerShell(serviceName)}
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$wasRunning = $service -and $service.Status -eq 'Running'
$stoppedService = $false
if ($wasRunning) {
  Stop-Service -Name $serviceName -Force
  $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(10))
  $stoppedService = $true
}

$serial = $null
try {
  $portName = $env:AGENT_LIGHT_DEVICE_PORT
  $command = $env:AGENT_LIGHT_DEVICE_COMMAND
  $serial = New-Object System.IO.Ports.SerialPort -ArgumentList $portName, 9600, ([System.IO.Ports.Parity]::None), 8, ([System.IO.Ports.StopBits]::One)
  $serial.NewLine = "\`n"
  $serial.ReadTimeout = 22000
  $serial.WriteTimeout = 5000
  $serial.Open()
  Start-Sleep -Milliseconds 300
  $serial.DiscardInBuffer()
  $serial.Write($command + "\`n")
  try {
    $response = $serial.ReadLine()
  } catch [System.TimeoutException] {
    $response = $serial.ReadExisting()
  }
  $response.Trim()
} finally {
  if ($serial -ne $null) {
    if ($serial.IsOpen) {
      $serial.Close()
    }
    $serial.Dispose()
  }
  if ($stoppedService) {
    Start-Service -Name $serviceName
  }
}
`;

  return runPowerShell(script, {
    AGENT_LIGHT_DEVICE_PORT: port,
    AGENT_LIGHT_DEVICE_COMMAND: command,
  }).trim();
}

function runPowerShell(script, extraEnv = {}) {
  try {
    return execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', script], {
      cwd: repoRoot,
      encoding: 'utf8',
      env: { ...process.env, ...extraEnv },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } catch (error) {
    const stderr = error.stderr?.toString('utf8').trim();
    const stdout = error.stdout?.toString('utf8').trim();
    throw new Error(stderr || stdout || error.message);
  }
}

function quotePowerShell(value) {
  return `'${String(value).replace(/'/g, "''")}'`;
}

function isTcpTransport(value) {
  return /^(tcp|wifi)$/i.test(value ?? '');
}
