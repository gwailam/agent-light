import test from 'node:test';
import assert from 'node:assert/strict';

import { normalizeCommand } from '../lib/commands.mjs';

test('maps Claude Code state names to default light commands', () => {
  assert.equal(normalizeCommand('idle'), 'G:on');
  assert.equal(normalizeCommand('thinking'), 'Y:blink:250');
  assert.equal(normalizeCommand('running'), 'R:blink:250');
});

test('accepts direct light commands with blink frequency', () => {
  assert.equal(normalizeCommand('Y:blink:450'), 'Y:blink:450');
  assert.equal(normalizeCommand('R:on'), 'R:on');
});

test('uses environment overrides for blink frequency and full commands', () => {
  assert.equal(normalizeCommand('thinking', { CLAUDE_LIGHT_THINKING_MS: '700' }), 'Y:blink:700');
  assert.equal(normalizeCommand('running', { CLAUDE_LIGHT_RUNNING: 'R:on' }), 'R:on');
});

test('rejects unsafe or malformed commands', () => {
  assert.throws(() => normalizeCommand('rm -rf /'), /Unknown light state/);
  assert.throws(() => normalizeCommand('Y:blink:fast'), /Invalid light command/);
  assert.throws(() => normalizeCommand('B:on'), /Invalid light command/);
});
