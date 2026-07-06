#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';

import { normalizeCommand } from './lib/commands.mjs';

const options = parseArgs(process.argv.slice(2));
const baud = String(options.baud);
let serialPort;

try {
  serialPort = options.serial === 'auto' ? findArduinoPort() : options.serial;
  configureSerial(serialPort, baud);
} catch (error) {
  console.error(`Failed to start bridge: ${error.message}`);
  process.exit(1);
}

const serial = fs.createWriteStream(getSerialWritePath(serialPort), { flags: 'w' });
const server = net.createServer((socket) => {
  socket.setEncoding('utf8');

  let buffer = '';
  socket.on('data', (chunk) => {
    buffer += chunk;
    const lines = buffer.split(/\r?\n/);
    buffer = lines.pop() ?? '';

    for (const line of lines) {
      sendCommand(line);
    }
  });
});

serial.on('open', () => {
  console.log(`Agent light bridge connected to ${serialPort} at ${baud} baud`);
  console.log(`Listening on 127.0.0.1:${options.listen}`);
  sendCommand(options.initial);
});

serial.on('error', (error) => {
  console.error(`Serial error: ${error.message}`);
  process.exitCode = 1;
});

server.listen(options.listen, '127.0.0.1');

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

function sendCommand(rawCommand) {
  let command;
  try {
    command = normalizeCommand(rawCommand);
  } catch (error) {
    console.error(error.message);
    return;
  }

  serial.write(`${command}\n`);
  console.log(`${new Date().toLocaleTimeString()} -> ${command}`);
}

function shutdown() {
  sendCommand('claude:idle');
  server.close();
  serial.end(() => process.exit(0));
}

function parseArgs(args) {
  const parsed = {
    serial: process.env.AGENT_LIGHT_SERIAL ?? process.env.CLAUDE_LIGHT_SERIAL ?? 'auto',
    baud: Number(process.env.AGENT_LIGHT_BAUD ?? process.env.CLAUDE_LIGHT_BAUD ?? 9600),
    listen: Number(process.env.AGENT_LIGHT_PORT ?? process.env.CLAUDE_LIGHT_PORT ?? 8765),
    initial: process.env.AGENT_LIGHT_INITIAL ?? process.env.CLAUDE_LIGHT_INITIAL ?? 'claude:idle',
  };

  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    const next = args[index + 1];

    if (arg === '--serial') {
      parsed.serial = next;
      index += 1;
    } else if (arg === '--baud') {
      parsed.baud = Number(next);
      index += 1;
    } else if (arg === '--listen') {
      parsed.listen = Number(next);
      index += 1;
    } else if (arg === '--initial') {
      parsed.initial = next;
      index += 1;
    }
  }

  if (!Number.isInteger(parsed.baud) || parsed.baud <= 0) {
    throw new Error('Invalid baud rate');
  }

  if (!Number.isInteger(parsed.listen) || parsed.listen <= 0) {
    throw new Error('Invalid listen port');
  }

  return parsed;
}

function findArduinoPort() {
  if (process.platform === 'win32') {
    return findWindowsArduinoPort();
  }

  const devices = fs.readdirSync('/dev');
  const candidates = devices
    .filter((device) => getUnixSerialPattern().test(device))
    .map((device) => path.join('/dev', device))
    .sort();

  if (candidates.length === 0) {
    throw new Error(`No Arduino serial port found. Use --serial ${getSerialHint()}.`);
  }

  return candidates[0];
}

function findWindowsArduinoPort() {
  const ports = listWindowsSerialPorts();
  const preferred = ports.find((port) =>
    /arduino|silicon labs|cp210|ch340|ch910|wch|usb[-\s]?serial|usb to uart|uart|espressif/i.test(port.name),
  );
  const selected = preferred ?? ports[0];

  if (!selected) {
    throw new Error('No Arduino serial port found. Use --serial COM3.');
  }

  return selected.device;
}

function listWindowsSerialPorts() {
  try {
    const output = execFileSync(
      'powershell.exe',
      [
        '-NoProfile',
        '-Command',
        'Get-CimInstance Win32_SerialPort | ForEach-Object { "$($_.DeviceID)|$($_.Name)" }',
      ],
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] },
    );

    return output
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
      .map((line) => {
        const [device, ...nameParts] = line.split('|');
        return { device: normalizeWindowsPortName(device), name: nameParts.join('|') };
      })
      .filter((port) => /^COM\d+$/i.test(port.device))
      .sort(compareWindowsPorts);
  } catch {
    return [];
  }
}

function compareWindowsPorts(left, right) {
  return getWindowsPortNumber(left.device) - getWindowsPortNumber(right.device);
}

function getWindowsPortNumber(device) {
  const match = device.match(/\d+/);
  return match ? Number(match[0]) : Number.MAX_SAFE_INTEGER;
}

function getUnixSerialPattern() {
  if (process.platform === 'darwin') {
    return /^cu\.(usbmodem|usbserial|wchusbserial)/;
  }

  return /^(ttyACM|ttyUSB|ttyAMA|rfcomm)/;
}

function getSerialHint() {
  if (process.platform === 'win32') return 'COM3';
  if (process.platform === 'darwin') return '/dev/cu.usbmodemXXXXXX';
  return '/dev/ttyACM0';
}

function configureSerial(device, speed) {
  if (process.platform === 'win32') {
    const port = normalizeWindowsPortName(device);
    try {
      execFileSync('mode.com', [`${port}:`, `BAUD=${speed}`, 'PARITY=N', 'DATA=8', 'STOP=1'], {
        stdio: 'ignore',
      });
    } catch (error) {
      console.warn(`Warning: failed to configure ${port} with mode.com; continuing with existing serial settings.`);
    }
    return;
  }

  const deviceFlag = process.platform === 'darwin' ? '-f' : '-F';
  execFileSync('stty', [deviceFlag, device, speed, 'cs8', '-cstopb', '-parenb', 'raw', '-echo']);
}

function getSerialWritePath(device) {
  if (process.platform !== 'win32') {
    return device;
  }

  return `\\\\.\\${normalizeWindowsPortName(device)}`;
}

function normalizeWindowsPortName(device) {
  const value = String(device).trim();
  const match = value.match(/^(?:\\\\\.\\)?(COM\d+):?$/i);
  return match ? match[1].toUpperCase() : value;
}
