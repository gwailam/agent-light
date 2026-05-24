const DIRECT_COMMAND = /^[GYR](?::(on|off|blink)(?::(\d+))?)?$/;

const DEFAULTS = {
  idle: 'G:on',
  thinking: 'Y:blink:250',
  running: 'R:blink:250',
};

const ALIASES = {
  green: 'idle',
  yellow: 'thinking',
  red: 'running',
  execute: 'running',
  executing: 'running',
  busy: 'running',
  think: 'thinking',
};

export function normalizeCommand(rawValue, env = process.env) {
  const value = String(rawValue ?? '').trim();
  if (!value) {
    throw new Error('Missing light state');
  }

  if (DIRECT_COMMAND.test(value)) {
    return normalizeDirectCommand(value);
  }

  if (value.includes(':')) {
    throw new Error(`Invalid light command: ${value}`);
  }

  const state = ALIASES[value] ?? value;
  if (!Object.hasOwn(DEFAULTS, state)) {
    throw new Error(`Unknown light state: ${value}`);
  }

  const fullOverride = env[`CLAUDE_LIGHT_${state.toUpperCase()}`];
  if (fullOverride) {
    return normalizeCommand(fullOverride, {});
  }

  if (state === 'thinking' && env.CLAUDE_LIGHT_THINKING_MS) {
    return normalizeDirectCommand(`Y:blink:${env.CLAUDE_LIGHT_THINKING_MS}`);
  }

  if (state === 'running' && env.CLAUDE_LIGHT_RUNNING_MS) {
    return normalizeDirectCommand(`R:blink:${env.CLAUDE_LIGHT_RUNNING_MS}`);
  }

  return DEFAULTS[state];
}

function normalizeDirectCommand(command) {
  const match = command.match(DIRECT_COMMAND);
  if (!match) {
    throw new Error(`Invalid light command: ${command}`);
  }

  const [, mode, interval] = match;
  const light = command[0];

  if (!mode) {
    if (light === 'G') return DEFAULTS.idle;
    if (light === 'Y') return DEFAULTS.thinking;
    return DEFAULTS.running;
  }

  if (mode !== 'blink') {
    return `${light}:${mode}`;
  }

  if (!interval) {
    throw new Error(`Invalid light command: ${command}`);
  }

  const milliseconds = Number(interval);
  if (!Number.isInteger(milliseconds) || milliseconds < 50 || milliseconds > 10000) {
    throw new Error(`Invalid light command: ${command}`);
  }

  return `${light}:blink:${milliseconds}`;
}
