# Agent Light

[English](README.md)

将 Arduino 交通灯模块变成 Claude Code 的实时状态指示灯。

| 状态       | 灯光         |
|------------|-------------|
| 空闲       | 绿灯常亮     |
| 思考中     | 黄灯闪烁     |
| 执行工具   | 红灯闪烁     |

## 前置条件

- Node.js（无需安装依赖）
- 通过 USB 串口连接的 Arduino 交通灯 LED 模块
- 启动桥接前请关闭 Arduino IDE 的串口监视器

## 快速开始

### 1. 启动桥接服务

```sh
npm run bridge
```

桥接服务会自动检测 Arduino 串口。手动指定串口：

```sh
npm run bridge -- --serial /dev/cu.usbmodem832101
```

### 2. 配置 Claude Code Hooks

将 [`claude-settings-snippet.json`](claude-settings-snippet.json) 合并到 `~/.claude/settings.json` 中，并将代码片段中的路径修改为本仓库的实际位置。

Hook 事件对应关系：

- **UserPromptSubmit** — 黄灯闪烁（思考中）
- **PreToolUse** — 红灯闪烁（执行中）
- **PostToolUse** — 黄灯闪烁（回到思考）
- **Stop** — 绿灯常亮（空闲）

### 3. 手动测试

```sh
npm run light -- idle
npm run light -- thinking
npm run light -- running
npm run light -- Y:blink:700
npm run light -- R:on
```

## 命令格式

命名状态：`idle`、`thinking`、`running`（支持别名：`green`、`yellow`、`red`、`busy`、`think`）。

直接命令：`{G|Y|R}:{on|off|blink}[:{ms}]` — 例如 `Y:blink:700`、`R:on`、`G:off`。

闪烁间隔范围：50 ~ 10000 毫秒。

## 配置项

### 桥接服务参数

| 参数        | 环境变量               | 默认值   |
|-------------|------------------------|----------|
| `--serial`  | `CLAUDE_LIGHT_SERIAL`  | auto     |
| `--baud`    | `CLAUDE_LIGHT_BAUD`    | 9600     |
| `--listen`  | `CLAUDE_LIGHT_PORT`    | 8765     |
| `--initial` | `CLAUDE_LIGHT_INITIAL` | idle     |

### 自定义灯光模式

| 环境变量                   | 示例          |
|----------------------------|---------------|
| `CLAUDE_LIGHT_THINKING`    | `Y:on`        |
| `CLAUDE_LIGHT_RUNNING`     | `R:blink:100` |
| `CLAUDE_LIGHT_THINKING_MS` | `700`         |
| `CLAUDE_LIGHT_RUNNING_MS`  | `100`         |

## 架构

```
Claude Code hooks ──> hook-client.mjs ──TCP──> serial-bridge.mjs ──串口──> Arduino
```

- **hook-client.mjs** — 即发即忘的 TCP 客户端，发送单条命令后退出。
- **serial-bridge.mjs** — 常驻 TCP 服务器，将命令转发到串口。
- **lib/commands.mjs** — 共享的命令解析与校验逻辑。

## 许可证

MIT
