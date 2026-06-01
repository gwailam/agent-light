# Agent Light

[中文版](README.zh-CN.md)

Turn an Arduino traffic-light module into a real-time status indicator for Claude Code.

| State         | Light          |
|---------------|----------------|
| Idle          | Green solid    |
| Thinking      | Yellow blink   |
| Running tools | Red blink      |

## Prerequisites

- Node.js (no dependencies to install)
- An Arduino with a traffic-light LED module connected via USB serial
- Close the Arduino IDE serial monitor before starting the bridge

### Hardware

![Hardware shopping list](images/list.jpg)

## Quick Start

### 1. Start the bridge

```sh
npm run bridge
```

The bridge auto-detects the Arduino serial port. To specify one manually:

```sh
npm run bridge -- --serial /dev/cu.usbmodem832101
```

### 2. Wire up Claude Code hooks

Merge [`claude-settings-snippet.json`](claude-settings-snippet.json) into your `~/.claude/settings.json`. Update the path inside the snippet to match where you cloned this repo.

Hook events used:

- **UserPromptSubmit** — yellow blink (thinking)
- **PreToolUse** — red blink (running)
- **PostToolUse** — yellow blink (back to thinking)
- **Stop** — green solid (idle)

### 3. Test manually

```sh
npm run light -- idle
npm run light -- thinking
npm run light -- running
npm run light -- Y:blink:700
npm run light -- R:on
```

## Command Format

Named states: `idle`, `thinking`, `running` (plus aliases like `green`, `yellow`, `red`, `busy`, `think`).

Direct commands: `{G|Y|R}:{on|off|blink}[:{ms}]` — e.g. `Y:blink:700`, `R:on`, `G:off`.

Blink intervals must be between 50 and 10000 ms.

## Configuration

### Bridge options

| Flag        | Env var                | Default  |
|-------------|------------------------|----------|
| `--serial`  | `CLAUDE_LIGHT_SERIAL`  | auto     |
| `--baud`    | `CLAUDE_LIGHT_BAUD`    | 9600     |
| `--listen`  | `CLAUDE_LIGHT_PORT`    | 8765     |
| `--initial` | `CLAUDE_LIGHT_INITIAL` | idle     |

### Override default light patterns

| Env var                    | Example       |
|----------------------------|---------------|
| `CLAUDE_LIGHT_THINKING`    | `Y:on`        |
| `CLAUDE_LIGHT_RUNNING`     | `R:blink:100` |
| `CLAUDE_LIGHT_THINKING_MS` | `700`         |
| `CLAUDE_LIGHT_RUNNING_MS`  | `100`         |

## Architecture

```
Claude Code hooks ──> hook-client.mjs ──TCP──> serial-bridge.mjs ──Serial──> Arduino
```

- **hook-client.mjs** — fire-and-forget TCP client, sends a single command then exits.
- **serial-bridge.mjs** — long-running TCP server that forwards commands to the serial port.
- **lib/commands.mjs** — shared command parsing and validation.

## Tutorial

For a detailed step-by-step tutorial, follow the WeChat Official Account **阿皓AI** (Chinese only).

![WeChat Official Account: 阿皓AI](images/ahao.png)

## License

MIT
