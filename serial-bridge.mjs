#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';

import { normalizeCommand } from './lib/commands.mjs';

const options = parseArgs(process.argv.slice(2));
const serialPort = options.serial === 'auto' ? findArduinoPort() : options.serial;
const baud = String(options.baud);

configureSerial(serialPort, baud);

const serial = fs.createWriteStream(serialPort, { flags: 'w' });
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
  console.log(`Claude light bridge connected to ${serialPort} at ${baud} baud`);
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
  sendCommand('idle');
  server.close();
  serial.end(() => process.exit(0));
}

function parseArgs(args) {
  const parsed = {
    serial: process.env.CLAUDE_LIGHT_SERIAL ?? 'auto',
    baud: Number(process.env.CLAUDE_LIGHT_BAUD ?? 9600),
    listen: Number(process.env.CLAUDE_LIGHT_PORT ?? 8765),
    initial: process.env.CLAUDE_LIGHT_INITIAL ?? 'idle',
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
  const devices = fs.readdirSync('/dev');
  const candidates = devices
    .filter((device) => /^cu\.(usbmodem|usbserial|wchusbserial)/.test(device))
    .map((device) => path.join('/dev', device))
    .sort();

  if (candidates.length === 0) {
    throw new Error('No Arduino serial port found. Use --serial /dev/cu.usbmodemXXXXXX.');
  }

  return candidates[0];
}

function configureSerial(device, speed) {
  execFileSync('stty', ['-f', device, speed, 'cs8', '-cstopb', '-parenb', 'raw', '-echo']);
}
