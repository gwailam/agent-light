#!/usr/bin/env node
import net from 'node:net';

import { normalizeCommand } from './lib/commands.mjs';

const host = process.env.CLAUDE_LIGHT_HOST ?? '127.0.0.1';
const port = Number(process.env.CLAUDE_LIGHT_PORT ?? 8765);
const timeoutMs = Number(process.env.CLAUDE_LIGHT_TIMEOUT_MS ?? 250);
const state = process.argv[2] ?? 'idle';

let command;
try {
  command = normalizeCommand(state);
} catch (error) {
  console.error(error.message);
  process.exit(0);
}

const socket = net.createConnection({ host, port });
const finish = () => process.exit(0);

socket.setTimeout(timeoutMs, finish);
socket.on('connect', () => {
  socket.end(`${command}\n`);
});
socket.on('error', finish);
socket.on('close', finish);
