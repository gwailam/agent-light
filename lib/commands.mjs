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

const TARGETS = {
  claude: 'claude',
  main: 'main',
  default: 'main',
  agent: 'main',
  codex: 'codex',
  c: 'codex',
};

export function normalizeCommand(rawValue, env = process.env) {
  const value = String(rawValue ?? '').trim();
  if (!value) {
    throw new Error('Missing light state');
  }

  const targetCommand = parseTargetCommand(value);
  if (targetCommand) {
    return `${targetCommand.target}:${normalizeCommand(targetCommand.command, env)}`;
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

  const fullOverride = getEnvValue(env, `${state.toUpperCase()}`);
  if (fullOverride) {
    return normalizeCommand(fullOverride, {});
  }

  const intervalOverride = getEnvValue(env, `${state.toUpperCase()}_MS`);
  if (state === 'thinking' && intervalOverride) {
    return normalizeDirectCommand(`Y:blink:${intervalOverride}`);
  }

  if (state === 'running' && intervalOverride) {
    return normalizeDirectCommand(`R:blink:${intervalOverride}`);
  }

  return DEFAULTS[state];
}

function parseTargetCommand(value) {
  const colonIndex = value.indexOf(':');
  if (colonIndex < 0) {
    return null;
  }

  const target = TARGETS[value.slice(0, colonIndex).toLowerCase()];
  if (!target) {
    return null;
  }

  return {
    target,
    command: value.slice(colonIndex + 1),
  };
}

function getEnvValue(env, key) {
  return env[`AGENT_LIGHT_${key}`] ?? env[`CLAUDE_LIGHT_${key}`];
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
