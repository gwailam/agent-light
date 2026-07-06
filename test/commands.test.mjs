import test from 'node:test';
import assert from 'node:assert/strict';

import { normalizeCommand } from '../lib/commands.mjs';

test('maps agent state names to default light commands', () => {
  assert.equal(normalizeCommand('idle'), 'G:on');
  assert.equal(normalizeCommand('thinking'), 'Y:blink:250');
  assert.equal(normalizeCommand('running'), 'R:blink:250');
});

test('accepts direct light commands with blink frequency', () => {
  assert.equal(normalizeCommand('Y:blink:450'), 'Y:blink:450');
  assert.equal(normalizeCommand('R:on'), 'R:on');
});

test('supports targeted commands for multiple light modules', () => {
  assert.equal(normalizeCommand('claude:idle'), 'claude:G:on');
  assert.equal(normalizeCommand('claude:thinking'), 'claude:Y:blink:250');
  assert.equal(normalizeCommand('claude:R:on'), 'claude:R:on');
  assert.equal(normalizeCommand('main:idle'), 'main:G:on');
  assert.equal(normalizeCommand('codex:thinking'), 'codex:Y:blink:250');
  assert.equal(normalizeCommand('codex:R:on'), 'codex:R:on');
  assert.equal(normalizeCommand('c:running'), 'codex:R:blink:250');
});

test('uses environment overrides for blink frequency and full commands', () => {
  assert.equal(normalizeCommand('thinking', { AGENT_LIGHT_THINKING_MS: '700' }), 'Y:blink:700');
  assert.equal(normalizeCommand('running', { AGENT_LIGHT_RUNNING: 'R:on' }), 'R:on');
  assert.equal(normalizeCommand('thinking', { CLAUDE_LIGHT_THINKING_MS: '800' }), 'Y:blink:800');
});

test('rejects unsafe or malformed commands', () => {
  assert.throws(() => normalizeCommand('rm -rf /'), /Unknown light state/);
  assert.throws(() => normalizeCommand('Y:blink:fast'), /Invalid light command/);
  assert.throws(() => normalizeCommand('B:on'), /Invalid light command/);
  assert.throws(() => normalizeCommand('codex:blue'), /Unknown light state/);
});
