#!/usr/bin/env node
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { normalizeCommand } from './lib/commands.mjs';

const repoRoot = path.dirname(fileURLToPath(import.meta.url));
const host = process.env.AGENT_LIGHT_HOST ?? process.env.CLAUDE_LIGHT_HOST ?? '127.0.0.1';
const port = Number(process.env.AGENT_LIGHT_PORT ?? process.env.CLAUDE_LIGHT_PORT ?? 8765);
const timeoutMs = Number(process.env.AGENT_LIGHT_TIMEOUT_MS ?? process.env.CLAUDE_LIGHT_TIMEOUT_MS ?? 250);
const state = process.argv[2] ?? 'idle';

let command;
try {
  command = normalizeCommand(state);
} catch (error) {
  logHook(`invalid state="${state}" error="${error.message}"`);
  console.error(error.message);
  process.exit(0);
}

const socket = net.createConnection({ host, port });
const finish = (message) => {
  if (message) {
    logHook(message);
  }
  process.exit(0);
};

socket.setTimeout(timeoutMs, () => finish(`timeout command="${command}" target="${host}:${port}" timeoutMs=${timeoutMs}`));
socket.on('connect', () => {
  logHook(`send command="${command}" target="${host}:${port}"`);
  socket.end(`${command}\n`);
});
socket.on('error', (error) => finish(`error command="${command}" target="${host}:${port}" error="${error.message}"`));
socket.on('close', () => finish());

function logHook(message) {
  if (/^(0|false|off)$/i.test(process.env.AGENT_LIGHT_HOOK_LOG ?? '')) {
    return;
  }

  try {
    const logDirectory = path.join(repoRoot, 'service', 'logs');
    fs.mkdirSync(logDirectory, { recursive: true });
    const line = `${new Date().toISOString()} ${message}\n`;
    fs.appendFileSync(path.join(logDirectory, 'hook-client.log'), line, 'utf8');
  } catch {
  }
}
