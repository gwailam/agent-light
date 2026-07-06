# ESP32 开发板针脚定义

本文档记录当前项目使用的 ESP32 开发板针脚资料。已识别到的芯片为 `ESP32-D0WD-V3`，USB 转串口为 `Silicon Labs CP210x`，当前串口为 `COM3`。

本项目当前 LED 接线：

| 模块 | 绿灯 | 黄灯 | 红灯 | 说明 |
| --- | --- | --- | --- | --- |
| 默认状态灯 | GPIO25 | GPIO26 | GPIO27 | 通用/Claude 状态灯 |
| Codex 状态灯 | GPIO19 | GPIO18 | GPIO17 | Codex 专用状态灯 |

## 使用建议

- ESP32 GPIO 电平是 `3.3V`，不要把 `5V` 信号直接接到 GPIO。
- 输出 LED 时建议使用限流电阻。
- `GPIO6` 到 `GPIO11` 通常连接板载 SPI Flash，不要使用。
- `GPIO34`、`GPIO35`、`GPIO36`、`GPIO39` 只能输入，不能输出。
- `GPIO0`、`GPIO2`、`GPIO4`、`GPIO5`、`GPIO12`、`GPIO15` 是启动配置相关引脚，外接电路不当可能导致无法启动或无法烧录。
- 使用 Wi-Fi 时，`ADC2` 通道会受限制；需要稳定模拟输入时优先选 `ADC1` 引脚。

## 常见 ESP32 DevKit 针脚表

| GPIO | 常见板上标注 | 输入 | 输出 | ADC | Touch | 常见功能 | 注意事项 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| GPIO0 | IO0 / BOOT | 是 | 是 | ADC2_CH1 | T1 | 启动模式、PWM | 启动配置引脚，下载模式常用 |
| GPIO1 | TX0 / U0TXD | 是 | 是 | \- | \- | UART0 TX | 烧录/串口日志占用，慎用 |
| GPIO2 | IO2 | 是 | 是 | ADC2_CH2 | T2 | PWM、部分板载 LED | 启动配置引脚，启动时不要被错误拉电平 |
| GPIO3 | RX0 / U0RXD | 是 | 是 | \- | \- | UART0 RX | 烧录/串口日志占用，慎用 |
| GPIO4 | IO4 | 是 | 是 | ADC2_CH0 | T0 | PWM、触摸 | 启动配置引脚 |
| GPIO5 | IO5 | 是 | 是 | \- | \- | VSPI CS、PWM | 启动配置引脚 |
| GPIO6 | SCK/CLK | \- | \- | \- | \- | SPI Flash | 不要使用 |
| GPIO7 | SDO/SD0 | \- | \- | \- | \- | SPI Flash | 不要使用 |
| GPIO8 | SDI/SD1 | \- | \- | \- | \- | SPI Flash | 不要使用 |
| GPIO9 | SHD/SD2 | \- | \- | \- | \- | SPI Flash | 不要使用 |
| GPIO10 | SWP/SD3 | \- | \- | \- | \- | SPI Flash | 不要使用 |
| GPIO11 | CSC/CMD | \- | \- | \- | \- | SPI Flash | 不要使用 |
| GPIO12 | IO12 / MTDI | 是 | 是 | ADC2_CH5 | T5 | JTAG、PWM | 启动配置引脚，拉高可能影响启动 |
| GPIO13 | IO13 / MTCK | 是 | 是 | ADC2_CH4 | T4 | JTAG、HSPI MOSI、PWM | 可用，注意 JTAG 复用 |
| GPIO14 | IO14 / MTMS | 是 | 是 | ADC2_CH6 | T6 | JTAG、HSPI CLK、PWM | 可用，注意 JTAG 复用 |
| GPIO15 | IO15 / MTDO | 是 | 是 | ADC2_CH3 | T3 | JTAG、HSPI CS、PWM | 启动配置引脚 |
| GPIO16 | IO16 | 是 | 是 | \- | \- | UART2 RX、PWM | 常用普通 GPIO |
| GPIO17 | IO17 | 是 | 是 | \- | \- | UART2 TX、PWM | 本项目 Codex 红灯 |
| GPIO18 | IO18 | 是 | 是 | \- | \- | VSPI SCK、PWM | 本项目 Codex 黄灯 |
| GPIO19 | IO19 | 是 | 是 | \- | \- | VSPI MISO、PWM | 本项目 Codex 绿灯 |
| GPIO21 | IO21 | 是 | 是 | \- | \- | I2C SDA、PWM | 常用 I2C SDA |
| GPIO22 | IO22 | 是 | 是 | \- | \- | I2C SCL、PWM | 常用 I2C SCL |
| GPIO23 | IO23 | 是 | 是 | \- | \- | VSPI MOSI、PWM | 常用 SPI 引脚 |
| GPIO25 | IO25 | 是 | 是 | ADC2_CH8 | \- | DAC1、PWM | 本项目绿灯 |
| GPIO26 | IO26 | 是 | 是 | ADC2_CH9 | \- | DAC2、PWM | 本项目黄灯 |
| GPIO27 | IO27 | 是 | 是 | ADC2_CH7 | T7 | PWM、触摸 | 本项目红灯 |
| GPIO32 | IO32 | 是 | 是 | ADC1_CH4 | T9 | PWM、触摸、RTC | 适合模拟输入 |
| GPIO33 | IO33 | 是 | 是 | ADC1_CH5 | T8 | PWM、触摸、RTC | 适合模拟输入 |
| GPIO34 | IO34 | 是 | 否 | ADC1_CH6 | \- | 模拟输入 | 只能输入，无内部上拉/下拉 |
| GPIO35 | IO35 | 是 | 否 | ADC1_CH7 | \- | 模拟输入 | 只能输入，无内部上拉/下拉 |
| GPIO36 | VP / SENSOR_VP | 是 | 否 | ADC1_CH0 | \- | 模拟输入 | 只能输入，无内部上拉/下拉 |
| GPIO39 | VN / SENSOR_VN | 是 | 否 | ADC1_CH3 | \- | 模拟输入 | 只能输入，无内部上拉/下拉 |

## 常用外设默认引脚

| 外设 | 默认/常用引脚 | 说明 |
| --- | --- | --- |
| UART0 | TX `GPIO1`，RX `GPIO3` | USB 串口烧录和日志，通常不要占用 |
| UART2 | TX `GPIO17`，RX `GPIO16` | 常用于额外串口设备 |
| I2C | SDA `GPIO21`，SCL `GPIO22` | Arduino 中可改引脚 |
| VSPI | MOSI `GPIO23`，MISO `GPIO19`，SCK `GPIO18`，CS `GPIO5` | 常用 SPI 总线 |
| HSPI | MOSI `GPIO13`，MISO `GPIO12`，SCK `GPIO14`，CS `GPIO15` | 可用但涉及启动/JTAG 引脚 |
| DAC | DAC1 `GPIO25`，DAC2 `GPIO26` | 可输出模拟电压 |
| PWM | 大多数可输出 GPIO | `GPIO34/35/36/39` 不能输出 |

## 供电与控制针脚

| 针脚 | 作用 | 注意事项 |
| --- | --- | --- |
| `3V3` | 3.3V 输出 | 给低功耗外设供电，不要过载 |
| `5V` / `VIN` | USB 5V 或外部输入 | 不同开发板标注可能不同 |
| `GND` | 地 | 所有外设必须共地 |
| `EN` / `RST` | 复位/使能 | 拉低会复位 ESP32 |
| `BOOT` | 下载模式按钮 | 烧录失败时可按住 BOOT 再上传 |
