using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentLightManager
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new MainWindow());
        }
    }

    internal sealed class MainWindow : Window
    {
        private const string ServiceName = "AgentLightBridge";
        private const string SilentCommandPrefix = "__agent_light_silent__:";

        private readonly string repoRoot;
        private readonly string configPath;
        private readonly string logDirectory;
        private readonly string lightEventLogPath;
        private readonly string managerSettingsPath;
        private readonly StringBuilder outputHistoryText;
        private readonly List<Button> busyButtons;

        private TextBlock serviceStatusText;
        private TextBlock bridgeStatusText;
        private TextBlock deviceStatusText;
        private TextBlock transportStatusText;

        private ComboBox transportCombo;
        private ComboBox serialCombo;
        private ComboBox firmwareSerialCombo;
        private TextBox hostText;
        private TextBox portText;
        private TextBlock serialFieldLabel;
        private TextBlock tcpEndpointFieldLabel;
        private Grid tcpEndpointPanel;
        private Button refreshSerialButton;
        private Button applyTransportButton;

        private TextBox ssidText;
        private PasswordBox passwordBox;
        private TextBox passwordPlainText;
        private CheckBox rememberPasswordCheckBox;
        private CheckBox showPasswordCheckBox;

        private TextBox commandText;
        private TextBox outputText;
        private TextBox logText;
        private TabControl outputTabs;
        private TabItem outputTab;
        private TabItem logTab;

        private Dictionary<string, string> config;
        private string currentLogText;
        private long lightEventLogPosition;
        private bool lightEventLogPositionInitialized;
        private DispatcherTimer logRefreshTimer;
        private bool logRefreshInProgress;

        public MainWindow()
        {
            repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            configPath = Path.Combine(repoRoot, "service", "agent-light-service.ini");
            logDirectory = Path.Combine(repoRoot, "service", "logs");
            lightEventLogPath = Path.Combine(logDirectory, "light-events.log");
            managerSettingsPath = Path.Combine(repoRoot, "manager", "AgentLightManager.settings");
            outputHistoryText = new StringBuilder();
            busyButtons = new List<Button>();

            Title = "Agent Light 设备管理器 - WPF";
            Width = 760;
            Height = 860;
            MinWidth = 760;
            MaxWidth = 760;
            MinHeight = 860;
            MaxHeight = 860;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));

            BuildLayout();
            LoadConfigIntoForm();
            Loaded += delegate
            {
                AppendOutput("管理器已启动");
                StartRealtimeLogRefresh();
                AppendOutput("启动流程: 刷新服务日志");
                RefreshLogs();
                AppendOutput("启动流程: 扫描串口列表");
                RefreshSerialPorts(true, false);
                AppendOutput("启动流程: 刷新状态分组");
                RefreshAll();
            };
            Closed += delegate { StopRealtimeLogRefresh(); };
        }

        private void BuildLayout()
        {
            var root = new Grid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(198) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Content = root;

            var statusGroup = BuildStatusGroup();
            root.Children.Add(statusGroup);
            Grid.SetRow(statusGroup, 0);

            var tabs = BuildMainTabs();
            tabs.Margin = new Thickness(0, 8, 0, 6);
            root.Children.Add(tabs);
            Grid.SetRow(tabs, 1);

            outputTabs = BuildOutputTabs();
            root.Children.Add(outputTabs);
            Grid.SetRow(outputTabs, 2);
        }

        private GroupBox BuildStatusGroup()
        {
            var group = CreateGroup("状态");
            var grid = new Grid();
            grid.Margin = new Thickness(8);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });

            serviceStatusText = AddStatusCell(grid, "服务状态", 0, 0);
            bridgeStatusText = AddStatusCell(grid, "桥接", 0, 2);
            deviceStatusText = AddStatusCell(grid, "ESP32", 1, 0);
            transportStatusText = AddStatusCell(grid, "通讯方式", 1, 2);

            var refreshButton = CreateButton("刷新状态", delegate { RefreshAll(); });
            refreshButton.Width = 96;
            refreshButton.Margin = new Thickness(8, 0, 0, 0);
            refreshButton.HorizontalAlignment = HorizontalAlignment.Right;
            refreshButton.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(refreshButton);
            Grid.SetRow(refreshButton, 0);
            Grid.SetRowSpan(refreshButton, 2);
            Grid.SetColumn(refreshButton, 4);

            group.Content = grid;
            return group;
        }

        private TextBlock AddStatusCell(Grid grid, string label, int row, int labelColumn)
        {
            var labelText = CreateLabel(label, true);
            grid.Children.Add(labelText);
            Grid.SetRow(labelText, row);
            Grid.SetColumn(labelText, labelColumn);

            var value = CreateValueText("-");
            grid.Children.Add(value);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, labelColumn + 1);
            return value;
        }

        private TabControl BuildMainTabs()
        {
            var tabs = new TabControl();
            tabs.Items.Add(CreateTab("通讯方式", BuildTransportTab()));
            tabs.Items.Add(CreateTab("Wi-Fi 配网", BuildWifiTab()));
            tabs.Items.Add(CreateTab("状态灯", BuildLightTab()));
            tabs.Items.Add(CreateTab("固件烧录", BuildFirmwareTab()));
            tabs.Items.Add(CreateTab("管理命令", BuildCommandTab()));
            return tabs;
        }

        private UIElement BuildTransportTab()
        {
            var grid = CreateFormGrid(2);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            transportCombo = new ComboBox();
            transportCombo.Items.Add("serial");
            transportCombo.Items.Add("tcp");
            transportCombo.SelectionChanged += delegate { UpdateTransportFields(); };
            AddField(grid, "类型", transportCombo, 0, 1);

            serialCombo = new ComboBox();
            serialFieldLabel = AddField(grid, "串口", serialCombo, 1, 1);
            refreshSerialButton = CreateButton("刷新串口", delegate { RefreshSerialPorts(); });
            CenterSideButton(refreshSerialButton);
            grid.Children.Add(refreshSerialButton);
            Grid.SetRow(refreshSerialButton, 0);
            Grid.SetColumn(refreshSerialButton, 2);

            tcpEndpointFieldLabel = CreateLabel("TCP", false);
            grid.Children.Add(tcpEndpointFieldLabel);
            Grid.SetRow(tcpEndpointFieldLabel, 1);
            Grid.SetColumn(tcpEndpointFieldLabel, 0);

            hostText = new TextBox();
            portText = new TextBox();
            tcpEndpointPanel = CreateTcpEndpointPanel();
            grid.Children.Add(tcpEndpointPanel);
            Grid.SetRow(tcpEndpointPanel, 1);
            Grid.SetColumn(tcpEndpointPanel, 1);

            applyTransportButton = CreateButton("应用并重启服务", delegate { ApplyTransport(); });
            CenterSideButton(applyTransportButton);
            grid.Children.Add(applyTransportButton);
            Grid.SetRow(applyTransportButton, 1);
            Grid.SetColumn(applyTransportButton, 2);

            return grid;
        }

        private Grid CreateTcpEndpointPanel()
        {
            var panel = new Grid();
            panel.Margin = new Thickness(0, 0, 8, 0);
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });

            hostText.Margin = new Thickness(0, 4, 12, 4);
            hostText.VerticalContentAlignment = VerticalAlignment.Center;
            panel.Children.Add(hostText);
            Grid.SetColumn(hostText, 0);

            AddInlineLabel(panel, "端口", 1);
            portText.Margin = new Thickness(0, 4, 0, 4);
            portText.VerticalContentAlignment = VerticalAlignment.Center;
            panel.Children.Add(portText);
            Grid.SetColumn(portText, 2);
            return panel;
        }

        private void AddInlineLabel(Grid grid, string text, int column)
        {
            var label = CreateLabel(text, false);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(label);
            Grid.SetColumn(label, column);
        }

        private void CenterSideButton(Button button)
        {
            button.Width = 150;
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Margin = new Thickness(0, 2, 0, 2);
        }

        private UIElement BuildWifiTab()
        {
            var grid = CreateFormGrid(3);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ssidText = new TextBox();
            AddField(grid, "SSID", ssidText, 0, 1);
            var readButton = CreateButton("读取 Wi-Fi 状态", delegate { ReadWifiStatusIntoForm(); });
            CenterSideButton(readButton);
            grid.Children.Add(readButton);
            Grid.SetRow(readButton, 0);
            Grid.SetColumn(readButton, 2);

            passwordBox = new PasswordBox();
            passwordPlainText = new TextBox();
            passwordPlainText.Visibility = Visibility.Collapsed;
            AddField(grid, "密码", passwordBox, 1, 1);
            passwordPlainText.Margin = new Thickness(0, 4, 8, 4);
            passwordPlainText.VerticalContentAlignment = VerticalAlignment.Center;
            grid.Children.Add(passwordPlainText);
            Grid.SetRow(passwordPlainText, 1);
            Grid.SetColumn(passwordPlainText, 1);
            var writeButton = CreateButton("写入 ESP32", delegate { ConfigureWifi(); });
            CenterSideButton(writeButton);
            grid.Children.Add(writeButton);
            Grid.SetRow(writeButton, 1);
            Grid.SetColumn(writeButton, 2);

            var options = new StackPanel();
            options.Orientation = Orientation.Horizontal;
            options.VerticalAlignment = VerticalAlignment.Center;
            rememberPasswordCheckBox = new CheckBox { Content = "记住密码", Margin = new Thickness(0, 0, 18, 0) };
            rememberPasswordCheckBox.Unchecked += delegate { ClearRememberedWifiSettings(); };
            showPasswordCheckBox = new CheckBox { Content = "显示密码" };
            showPasswordCheckBox.Checked += delegate { SetPasswordVisible(true); };
            showPasswordCheckBox.Unchecked += delegate { SetPasswordVisible(false); };
            options.Children.Add(rememberPasswordCheckBox);
            options.Children.Add(showPasswordCheckBox);
            grid.Children.Add(options);
            Grid.SetRow(options, 2);
            Grid.SetColumn(options, 1);

            return grid;
        }

        private UIElement BuildLightTab()
        {
            var grid = CreateFormGrid(3);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            AddText(grid, "Codex", 0, 0, true);
            AddCommandButton(grid, "空闲", "codex:idle", 0, 1);
            AddCommandButton(grid, "思考", "codex:thinking", 0, 2);
            AddCommandButton(grid, "执行", "codex:running", 0, 3);

            AddText(grid, "Claude", 1, 0, true);
            AddCommandButton(grid, "空闲", "claude:idle", 1, 1);
            AddCommandButton(grid, "思考", "claude:thinking", 1, 2);
            AddCommandButton(grid, "执行", "claude:running", 1, 3);

            var restore = CreateButton("全部恢复空闲", delegate
            {
                RunBackground("恢复空闲", delegate
                {
                    SendToService("claude:idle", true);
                    SendToService("codex:idle", true);
                    return "已恢复为空闲状态";
                });
            });
            restore.HorizontalAlignment = HorizontalAlignment.Right;
            restore.VerticalAlignment = VerticalAlignment.Center;
            restore.Margin = new Thickness(0, 2, 8, 2);
            restore.Width = 150;
            grid.Children.Add(restore);
            Grid.SetRow(restore, 2);
            Grid.SetColumn(restore, 1);
            Grid.SetColumnSpan(restore, 3);

            return grid;
        }

        private UIElement BuildFirmwareTab()
        {
            var grid = CreateFormGrid(3);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            firmwareSerialCombo = new ComboBox();
            AddField(grid, "烧录串口", firmwareSerialCombo, 0, 1);
            var refreshButton = CreateButton("刷新串口", delegate { RefreshSerialPorts(); });
            CenterSideButton(refreshButton);
            grid.Children.Add(refreshButton);
            Grid.SetRow(refreshButton, 0);
            Grid.SetColumn(refreshButton, 2);

            var compileButton = CreateButton("生成固件包(开发)", delegate { FlashFirmware(true); });
            CenterSideButton(compileButton);
            grid.Children.Add(compileButton);
            Grid.SetRow(compileButton, 1);
            Grid.SetColumn(compileButton, 1);

            var flashButton = CreateButton("烧录固件", delegate { FlashFirmware(false); });
            CenterSideButton(flashButton);
            grid.Children.Add(flashButton);
            Grid.SetRow(flashButton, 1);
            Grid.SetColumn(flashButton, 2);

            var hint = new TextBlock
            {
                Text = "烧录使用 firmware-release 中的预编译固件，不依赖 Arduino ESP32 core。生成固件包是开发功能，需要本机 Arduino 编译环境。",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(hint);
            Grid.SetRow(hint, 2);
            Grid.SetColumn(hint, 1);
            Grid.SetColumnSpan(hint, 2);

            return grid;
        }

        private UIElement BuildCommandTab()
        {
            var grid = new Grid { Margin = new Thickness(12, 6, 12, 6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });

            commandText = new TextBox
            {
                Text = "sys:info",
                Height = 28,
                Margin = new Thickness(0, 0, 12, 8),
                VerticalAlignment = VerticalAlignment.Top,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(commandText);
            Grid.SetRow(commandText, 0);
            Grid.SetColumn(commandText, 0);

            var send = CreateButton("发送", delegate { SendManagerCommand(commandText.Text, true); });
            send.Margin = new Thickness(0, 0, 0, 8);
            send.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(send);
            Grid.SetRow(send, 0);
            Grid.SetColumn(send, 1);

            var presets = new WrapPanel();
            presets.Margin = new Thickness(0, 0, 0, 4);
            presets.Children.Add(CreateButton("sys:info", delegate { UsePreset("sys:info"); }));
            presets.Children.Add(CreateButton("sys:ping", delegate { UsePreset("sys:ping"); }));
            presets.Children.Add(CreateButton("wifi:status", delegate { UsePreset("wifi:status"); }));
            presets.Children.Add(CreateButton("reboot", delegate { UsePreset("reboot"); }));
            presets.Children.Add(CreateButton("安装/修复 Codex Hooks", delegate { InstallOrRepairCodexHooks(); }));
            presets.Children.Add(CreateButton("预览 Codex Hooks", delegate { PreviewCodexHooks(); }));
            presets.Children.Add(CreateButton("测试 Codex 灯", delegate { SendManagerCommand("codex:thinking", false); }));
            presets.Children.Add(CreateButton("诊断 Codex Hooks", delegate { DiagnoseCodexHooks(); }));
            grid.Children.Add(presets);
            Grid.SetRow(presets, 1);
            Grid.SetColumnSpan(presets, 2);

            var serviceActions = new WrapPanel();
            serviceActions.Margin = new Thickness(0, 0, 0, 4);
            serviceActions.Children.Add(CreateButton("安装/修复服务", delegate { InstallOrRepairService(); }));
            serviceActions.Children.Add(CreateButton("卸载服务", delegate { ConfirmAndUninstallService(); }));
            serviceActions.Children.Add(CreateButton("启动服务", delegate { RunBackground("启动服务", delegate { StartService(); return "服务启动完成"; }); }));
            serviceActions.Children.Add(CreateButton("停止服务", delegate { RunBackground("停止服务", delegate { StopService(); return "服务停止完成"; }); }));
            serviceActions.Children.Add(CreateButton("重启服务", delegate { RunBackground("重启服务", delegate { RestartService(); return "服务重启完成"; }); }));
            grid.Children.Add(serviceActions);
            Grid.SetRow(serviceActions, 2);
            Grid.SetColumnSpan(serviceActions, 2);

            return grid;
        }

        private TabControl BuildOutputTabs()
        {
            var tabs = new TabControl();
            outputText = CreateLogTextBox();
            logText = CreateLogTextBox();
            outputTab = CreateTab("实时输出", outputText);
            logTab = CreateTab("服务日志", logText);
            tabs.Items.Add(outputTab);
            tabs.Items.Add(logTab);
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs args)
            {
                if (!object.ReferenceEquals(args.OriginalSource, tabs))
                {
                    return;
                }

                ScrollSelectedOutputTabToEnd();
            };
            return tabs;
        }

        private void LoadConfigIntoForm()
        {
            config = ReadConfig();
            LoadTransportFields();
            LoadRememberedWifiSettings();
            transportStatusText.Text = FormatTransportSummary();
        }

        private void LoadTransportFields()
        {
            var transport = GetConfig("Transport", "serial");
            transportCombo.SelectedItem = IsTcpTransport(transport) ? "tcp" : "serial";
            if (transportCombo.SelectedIndex < 0)
            {
                transportCombo.SelectedIndex = 0;
            }

            SelectSerialPort(GetConfig("Serial", "COM4"));
            hostText.Text = GetConfig("DeviceHost", "agent-light.local");
            portText.Text = GetConfig("DevicePort", "8766");
            UpdateTransportFields();
        }

        private void RefreshAll()
        {
            SetBusy(true);
            Task.Factory.StartNew(delegate
            {
                config = ReadConfig();

                var snapshot = new StatusSnapshot();
                snapshot.ServiceStatus = GetServiceStatus();
                snapshot.BridgeReachable = TestLocalBridge();
                snapshot.BridgeStatus = snapshot.BridgeReachable ? "监听中" : "未监听";
                snapshot.DeviceResponse = snapshot.BridgeReachable ? SendToService(SilentCommandPrefix + "sys:info", true) : "无法连接本机桥接服务";
                snapshot.TransportStatus = FormatTransportSummary();
                snapshot.Logs = ReadLogs();
                return snapshot;
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).ContinueWith(delegate(Task<StatusSnapshot> task)
            {
                SetBusy(false);
                if (task.Exception != null)
                {
                    ShowError(task.Exception.GetBaseException().Message);
                    return;
                }

                ApplyStatusSnapshot(task.Result);
                AppendOutput("状态分组已刷新: 服务 " + task.Result.ServiceStatus + "，ESP32 " + FormatDeviceSummary(task.Result.DeviceResponse) + "，" + task.Result.TransportStatus);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void RefreshSerialPorts()
        {
            RefreshSerialPorts(true, true);
        }

        private void RefreshSerialPorts(bool showOutput, bool affectBusy)
        {
            var current = GetSelectedSerialPortName();
            if (string.IsNullOrEmpty(current))
            {
                current = GetConfig("Serial", string.Empty);
            }
            RunBackground("刷新串口", delegate
            {
                var ports = GetUsableSerialPortOptions();
                ports.Sort(ComparePortNames);

                return SerializePorts(ports);
            }, delegate(string text)
            {
                var ports = DeserializePorts(text);
                ApplySerialPortOptions(ports, current);
                if (showOutput)
                {
                    AppendOutput("串口列表已刷新");
                }
            }, false, showOutput, affectBusy);
        }

        private string SerializePorts(List<SerialPortOption> ports)
        {
            var builder = new StringBuilder();
            foreach (var port in ports)
            {
                builder.Append(port.PortName).Append('\t').Append(port.Kind).Append('\n');
            }
            return builder.ToString();
        }

        private List<SerialPortOption> DeserializePorts(string text)
        {
            var ports = new List<SerialPortOption>();
            var lines = (text ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length == 2)
                {
                    ports.Add(new SerialPortOption(parts[0], parts[1]));
                }
            }
            return ports;
        }

        private void ApplySerialPortOptions(List<SerialPortOption> ports, string selectedPort)
        {
            serialCombo.Items.Clear();
            foreach (var port in ports)
            {
                serialCombo.Items.Add(port);
            }

            if (!SelectSerialPort(selectedPort))
            {
                serialCombo.SelectedIndex = ports.Count == 1 ? 0 : -1;
            }

            if (firmwareSerialCombo != null)
            {
                var firmwareSelected = GetSelectedFirmwareSerialPortName();
                if (string.IsNullOrEmpty(firmwareSelected))
                {
                    firmwareSelected = selectedPort;
                }

                firmwareSerialCombo.Items.Clear();
                foreach (var port in ports)
                {
                    firmwareSerialCombo.Items.Add(port);
                }

                if (!SelectFirmwareSerialPort(firmwareSelected))
                {
                    firmwareSerialCombo.SelectedIndex = ports.Count == 1 ? 0 : -1;
                }
            }
        }

        private List<SerialPortOption> GetUsableSerialPortOptions()
        {
            var ports = new List<SerialPortOption>();

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, Description, PNPDeviceID FROM Win32_SerialPort"))
                {
                    foreach (ManagementObject port in searcher.Get())
                    {
                        var deviceId = Convert.ToString(port["DeviceID"]);
                        var name = Convert.ToString(port["Name"]);
                        var description = Convert.ToString(port["Description"]);
                        var pnpDeviceId = Convert.ToString(port["PNPDeviceID"]);
                        if (string.IsNullOrEmpty(deviceId) || IsIncomingBluetoothSerialPort(pnpDeviceId))
                        {
                            continue;
                        }

                        if (!ContainsSerialPort(ports, deviceId))
                        {
                            ports.Add(new SerialPortOption(deviceId, GetSerialPortKind(name, description, pnpDeviceId)));
                        }
                    }
                }

                if (ports.Count > 0)
                {
                    return ports;
                }
            }
            catch
            {
            }

            foreach (var port in SerialPort.GetPortNames())
            {
                if (!ContainsSerialPort(ports, port))
                {
                    ports.Add(new SerialPortOption(port, "串口"));
                }
            }

            return ports;
        }

        private bool ContainsSerialPort(List<SerialPortOption> ports, string portName)
        {
            foreach (var port in ports)
            {
                if (string.Equals(port.PortName, portName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SelectSerialPort(string portName)
        {
            foreach (var item in serialCombo.Items)
            {
                var option = item as SerialPortOption;
                if (option != null && string.Equals(option.PortName, portName, StringComparison.OrdinalIgnoreCase))
                {
                    serialCombo.SelectedItem = option;
                    return true;
                }
            }

            serialCombo.SelectedIndex = -1;
            return false;
        }

        private string GetSelectedSerialPortName()
        {
            var option = serialCombo.SelectedItem as SerialPortOption;
            if (option != null)
            {
                return option.PortName;
            }

            return string.Empty;
        }

        private bool SelectFirmwareSerialPort(string portName)
        {
            if (firmwareSerialCombo == null)
            {
                return false;
            }

            foreach (var item in firmwareSerialCombo.Items)
            {
                var option = item as SerialPortOption;
                if (option != null && string.Equals(option.PortName, portName, StringComparison.OrdinalIgnoreCase))
                {
                    firmwareSerialCombo.SelectedItem = option;
                    return true;
                }
            }

            firmwareSerialCombo.SelectedIndex = -1;
            return false;
        }

        private string GetSelectedFirmwareSerialPortName()
        {
            if (firmwareSerialCombo == null)
            {
                return string.Empty;
            }

            var option = firmwareSerialCombo.SelectedItem as SerialPortOption;
            if (option != null)
            {
                return option.PortName;
            }

            return string.Empty;
        }

        private string GetSerialPortKind(string name, string description, string pnpDeviceId)
        {
            var text = ((name ?? string.Empty) + " " + (description ?? string.Empty) + " " + (pnpDeviceId ?? string.Empty)).ToUpperInvariant();
            if (text.Contains("BTHENUM") || text.Contains("蓝牙") || text.Contains("BLUETOOTH"))
            {
                return "蓝牙";
            }

            if (text.Contains("USB") || text.Contains("CP210") || text.Contains("CH340") || text.Contains("CH910") || text.Contains("UART") || text.Contains("SILICON LABS"))
            {
                return "板载 USB";
            }

            return "串口";
        }

        private bool IsIncomingBluetoothSerialPort(string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId))
            {
                return false;
            }

            var value = pnpDeviceId.ToUpperInvariant();
            return value.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("000000000000_00000000");
        }

        private void UpdateTransportFields()
        {
            if (transportCombo == null || serialCombo == null || hostText == null || portText == null)
            {
                return;
            }

            var tcp = string.Equals(Convert.ToString(transportCombo.SelectedItem), "tcp", StringComparison.OrdinalIgnoreCase);
            SetVisible(serialFieldLabel, !tcp);
            SetVisible(serialCombo, !tcp);
            SetVisible(tcpEndpointFieldLabel, tcp);
            SetVisible(tcpEndpointPanel, tcp);
        }

        private void ApplyTransport()
        {
            var selected = Convert.ToString(transportCombo.SelectedItem);
            if (string.IsNullOrEmpty(selected))
            {
                selected = "serial";
            }

            if (selected == "tcp")
            {
                var port = ParsePort(portText.Text, "TCP 端口");
                SaveConfigValue("Transport", "tcp");
                SaveConfigValue("DeviceHost", hostText.Text.Trim());
                SaveConfigValue("DevicePort", Convert.ToString(port));
            }
            else
            {
                var serial = GetSelectedSerialPortName().Trim().ToUpperInvariant();
                if (serial.Length == 0)
                {
                    ShowError("请先从串口列表中选择一个端口。");
                    return;
                }
                if (!serial.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    ShowError("串口格式应类似 COM4。");
                    return;
                }
                SaveConfigValue("Transport", "serial");
                SaveConfigValue("Serial", serial);
            }

            RunBackground("应用通讯方式", delegate
            {
                WriteConfig(config);
                RestartService();
                return "通讯方式已应用并重启服务";
            }, delegate(string text)
            {
                AppendOutput(text);
                RefreshAll();
            }, false);
        }

        private void FlashFirmware(bool compileOnly)
        {
            var serial = GetSelectedFirmwareSerialPortName().Trim().ToUpperInvariant();
            if (serial.Length == 0)
            {
                serial = GetSelectedSerialPortName().Trim().ToUpperInvariant();
            }

            if (!compileOnly && serial.Length == 0)
            {
                ShowError("请先选择 ESP32 对应的烧录串口。");
                return;
            }

            if (serial.Length > 0 && !serial.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("串口格式应类似 COM4。");
                return;
            }

            var action = compileOnly ? "生成固件包" : "烧录固件";
            RunBackground(action, delegate
            {
                var arguments = new List<string>();
                if (serial.Length > 0)
                {
                    arguments.Add("-Port");
                    arguments.Add(serial);
                }
                if (compileOnly)
                {
                    arguments.Add("-CompileOnly");
                }

                RunPowerShellScript("flash-firmware.ps1", arguments);
                return compileOnly ? "固件包生成完成" : "固件烧录完成";
            }, delegate(string text)
            {
                AppendOutput(text);
                RefreshAll();
            }, false);
        }

        private void InstallOrRepairService()
        {
            List<string> arguments;
            try
            {
                arguments = BuildInstallServiceArguments();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            RunBackground("安装/修复服务", delegate
            {
                RunPowerShellScript("install-windows-service.ps1", arguments);
                config = ReadConfig();
                return "服务安装/修复完成";
            }, delegate(string text)
            {
                AppendOutput(text);
                RefreshAll();
            }, false);
        }

        private List<string> BuildInstallServiceArguments()
        {
            var selected = Convert.ToString(transportCombo.SelectedItem);
            if (string.IsNullOrEmpty(selected))
            {
                selected = GetConfig("Transport", "serial");
            }

            var arguments = new List<string>();
            arguments.Add("-Transport");
            arguments.Add(selected == "tcp" ? "tcp" : "serial");
            arguments.Add("-Baud");
            arguments.Add(GetConfig("Baud", "9600"));
            arguments.Add("-Listen");
            arguments.Add(GetConfig("Listen", "8765"));
            arguments.Add("-Initial");
            arguments.Add(GetConfig("Initial", "claude:idle"));

            var serial = GetSelectedSerialPortName().Trim().ToUpperInvariant();
            if (serial.Length == 0 && selected == "tcp")
            {
                serial = GetConfig("Serial", "COM4");
            }

            var host = hostText.Text.Trim();
            if (host.Length == 0)
            {
                host = GetConfig("DeviceHost", "agent-light.local");
            }

            var portValue = portText.Text.Length == 0 ? GetConfig("DevicePort", "8766") : portText.Text;
            var port = ParsePort(portValue, "TCP 端口");

            if (selected == "tcp")
            {
                if (host.Length == 0)
                {
                    throw new InvalidOperationException("TCP 主机不能为空。");
                }
            }
            else if (serial.Length == 0)
            {
                throw new InvalidOperationException("请先从串口列表中选择一个端口。");
            }
            else if (!serial.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("串口格式应类似 COM4。");
            }

            arguments.Add("-Serial");
            arguments.Add(serial);
            arguments.Add("-DeviceHost");
            arguments.Add(host);
            arguments.Add("-DevicePort");
            arguments.Add(Convert.ToString(port));
            return arguments;
        }

        private void ConfirmAndUninstallService()
        {
            var result = MessageBox.Show(
                this,
                "确定要卸载 AgentLightBridge 服务吗？\r\n\r\n卸载后 Codex/Claude 状态灯不会自动响应，直到重新安装服务。",
                "卸载服务",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            RunBackground("卸载服务", delegate
            {
                RunPowerShellScript("uninstall-windows-service.ps1", new List<string> { "-ServiceName", ServiceName });
                return "服务卸载完成";
            }, delegate(string text)
            {
                AppendOutput(text);
                RefreshAll();
            }, false);
        }

        private void InstallOrRepairCodexHooks()
        {
            RunBackground("安装/修复 Codex Hooks", delegate
            {
                RunPowerShellScript("install-codex-hooks.ps1", new List<string>());
                return "Codex Hooks 安装/修复完成。请在 Codex 中打开 /hooks，信任更新后的 Agent Light hooks。";
            }, AppendOutput, false);
        }

        private void PreviewCodexHooks()
        {
            RunBackground("预览 Codex Hooks", delegate
            {
                RunPowerShellScript("install-codex-hooks.ps1", new List<string> { "-DryRun" });
                return "Codex Hooks 预览完成，未写入 hooks.json。";
            }, AppendOutput, false);
        }

        private void DiagnoseCodexHooks()
        {
            RunBackground("诊断 Codex Hooks", delegate
            {
                RunPowerShellScript("diagnose-codex-hooks.ps1", new List<string>());
                return "Codex Hooks 诊断完成。若测试灯正常但 Codex 无响应，请看 hook-client.log 是否在 Codex 对话时新增记录。";
            }, AppendOutput, false);
        }

        private void RunPowerShellScript(string scriptName, List<string> arguments)
        {
            var scriptPath = Path.Combine(repoRoot, "scripts", scriptName);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("脚本不存在。", scriptPath);
            }

            var commandLine = new StringBuilder();
            commandLine.Append("-NoProfile -ExecutionPolicy Bypass -File ");
            commandLine.Append(QuoteProcessArgument(scriptPath));
            foreach (var argument in arguments)
            {
                commandLine.Append(' ');
                commandLine.Append(QuoteProcessArgument(argument));
            }

            var startInfo = new ProcessStartInfo();
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = commandLine.ToString();
            startInfo.WorkingDirectory = repoRoot;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;

            if (IsAdministrator())
            {
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
            }
            else
            {
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
            }

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动 PowerShell。");
                }

                if (startInfo.RedirectStandardOutput)
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                        {
                            AppendOutput(args.Data);
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                        {
                            AppendOutput("脚本错误: " + args.Data);
                        }
                    };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(scriptName + " 执行失败，退出码 " + process.ExitCode + "。");
                }
            }
        }

        private bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private string QuoteProcessArgument(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void ConfigureWifi()
        {
            var ssid = ssidText.Text.Trim();
            var password = GetWifiPassword();
            if (ssid.Length == 0)
            {
                ShowError("SSID 不能为空。");
                return;
            }
            if (ssid.Contains(":") || password.Contains(":"))
            {
                ShowError("当前固件命令格式不支持 SSID 或密码包含冒号。");
                return;
            }

            if (rememberPasswordCheckBox.IsChecked == true)
            {
                SaveRememberedWifiSettings(ssid, password);
            }
            else
            {
                ClearRememberedWifiSettings();
            }

            SendManagerCommand("wifi:set:" + ssid + ":" + password, true);
        }

        private void ReadWifiStatusIntoForm()
        {
            RunBackground("读取 Wi-Fi 状态", delegate
            {
                return SendToService("wifi:status", true);
            }, delegate(string response)
            {
                ApplyWifiStatusToInputs(response);
                AppendOutput(FormatCommandResponse(response));
            });
        }

        private void ApplyWifiStatusToInputs(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return;
            }

            var values = ParseResponse(response);
            var ssid = GetValue(values, "ssid", string.Empty);
            if (ssid.Length == 0)
            {
                return;
            }

            ssidText.Text = ssid;

            string savedSsid;
            string savedPassword;
            if (TryLoadRememberedWifiSettings(out savedSsid, out savedPassword) &&
                string.Equals(savedSsid, ssid, StringComparison.Ordinal) &&
                savedPassword.Length > 0)
            {
                SetWifiPassword(savedPassword);
                rememberPasswordCheckBox.IsChecked = true;
                AppendOutput("已回填 SSID 和本机保存的密码。");
            }
            else
            {
                AppendOutput("已回填 SSID；ESP32 不返回 Wi-Fi 密码。");
            }
        }

        private string GetWifiPassword()
        {
            return showPasswordCheckBox.IsChecked == true ? passwordPlainText.Text : passwordBox.Password;
        }

        private void SetWifiPassword(string password)
        {
            passwordBox.Password = password ?? string.Empty;
            passwordPlainText.Text = password ?? string.Empty;
        }

        private void SetPasswordVisible(bool visible)
        {
            if (visible)
            {
                passwordPlainText.Text = passwordBox.Password;
                passwordPlainText.Visibility = Visibility.Visible;
                passwordBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                passwordBox.Password = passwordPlainText.Text;
                passwordBox.Visibility = Visibility.Visible;
                passwordPlainText.Visibility = Visibility.Collapsed;
            }
        }

        private void SendManagerCommand(string command, bool readResponse)
        {
            command = (command ?? string.Empty).Trim();
            if (command.Length == 0)
            {
                ShowError("命令不能为空。");
                return;
            }

            RunBackground("发送命令", delegate
            {
                var response = SendToService(command, readResponse);
                return response.Length == 0 ? "已发送: " + RedactCommand(command) : FormatCommandResponse(response);
            });
        }

        private string SendToService(string command, bool readResponse)
        {
            var port = ParsePort(GetConfig("Listen", "8765"), "本机监听端口");
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(2500))
                {
                    throw new IOException("连接本机桥接服务超时。");
                }

                client.EndConnect(result);
                client.ReceiveTimeout = IsManagementCommand(command) ? 24000 : 2500;
                using (var stream = client.GetStream())
                {
                    var data = Encoding.UTF8.GetBytes(command + "\n");
                    stream.Write(data, 0, data.Length);
                    stream.Flush();

                    if (!readResponse)
                    {
                        return string.Empty;
                    }

                    return ReadLine(stream);
                }
            }
        }

        private string ReadLine(NetworkStream stream)
        {
            var response = new StringBuilder();
            var buffer = new byte[1];

            while (true)
            {
                int read;
                try
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                var c = (char)buffer[0];
                if (c == '\n' || c == '\r')
                {
                    if (response.Length > 0)
                    {
                        break;
                    }
                }
                else
                {
                    response.Append(c);
                }
            }

            return response.ToString().Trim();
        }

        private bool TestLocalBridge()
        {
            try
            {
                var port = ParsePort(GetConfig("Listen", "8765"), "本机监听端口");
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    if (!result.AsyncWaitHandle.WaitOne(900))
                    {
                        return false;
                    }
                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private string GetServiceStatus()
        {
            try
            {
                using (var service = new ServiceController(ServiceName))
                {
                    return FormatServiceStatus(service.Status);
                }
            }
            catch
            {
                return "未安装";
            }
        }

        private string FormatServiceStatus(ServiceControllerStatus status)
        {
            if (status == ServiceControllerStatus.Running) return "运行中";
            if (status == ServiceControllerStatus.Stopped) return "已停止";
            if (status == ServiceControllerStatus.StartPending) return "启动中";
            if (status == ServiceControllerStatus.StopPending) return "停止中";
            if (status == ServiceControllerStatus.Paused) return "已暂停";
            if (status == ServiceControllerStatus.PausePending) return "暂停中";
            if (status == ServiceControllerStatus.ContinuePending) return "恢复中";
            return Convert.ToString(status);
        }

        private void StartService()
        {
            using (var service = new ServiceController(ServiceName))
            {
                AppendOutput("服务启动: 当前状态 " + FormatServiceStatus(service.Status));
                if (service.Status == ServiceControllerStatus.Running)
                {
                    AppendOutput("服务启动: 服务已在运行");
                    return;
                }
                AppendOutput("服务启动: 发送启动请求");
                service.Start();
                AppendOutput("服务启动: 等待进入运行状态");
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                AppendOutput("服务启动: 已进入运行状态");
            }
        }

        private void StopService()
        {
            using (var service = new ServiceController(ServiceName))
            {
                AppendOutput("服务停止: 当前状态 " + FormatServiceStatus(service.Status));
                if (service.Status == ServiceControllerStatus.Stopped)
                {
                    AppendOutput("服务停止: 服务已停止");
                    return;
                }
                AppendOutput("服务停止: 发送停止请求");
                service.Stop();
                AppendOutput("服务停止: 等待进入停止状态");
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                AppendOutput("服务停止: 已进入停止状态");
            }
        }

        private void RestartService()
        {
            using (var service = new ServiceController(ServiceName))
            {
                AppendOutput("服务重启: 当前状态 " + FormatServiceStatus(service.Status));
                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    AppendOutput("服务重启: 停止服务");
                    service.Stop();
                    AppendOutput("服务重启: 等待停止完成");
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    AppendOutput("服务重启: 服务已停止");
                }
                AppendOutput("服务重启: 启动服务");
                service.Start();
                AppendOutput("服务重启: 等待进入运行状态");
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                AppendOutput("服务重启: 已进入运行状态");
            }
        }

        private Dictionary<string, string> ReadConfig()
        {
            var values = DefaultConfig();
            if (!File.Exists(configPath))
            {
                return values;
            }

            foreach (var rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }

            return values;
        }

        private void LoadRememberedWifiSettings()
        {
            try
            {
                string ssid;
                string password;
                if (!TryLoadRememberedWifiSettings(out ssid, out password))
                {
                    return;
                }

                if (ssid.Length > 0)
                {
                    ssidText.Text = ssid;
                }

                if (password.Length > 0)
                {
                    SetWifiPassword(password);
                    rememberPasswordCheckBox.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                AppendOutput("读取已保存 Wi-Fi 密码失败: " + ex.Message);
            }
        }

        private bool TryLoadRememberedWifiSettings(out string ssid, out string password)
        {
            ssid = string.Empty;
            password = string.Empty;

            if (!File.Exists(managerSettingsPath))
            {
                return false;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(managerSettingsPath, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }

            string savedSsid;
            if (values.TryGetValue("WifiSsid", out savedSsid))
            {
                ssid = savedSsid;
            }

            string protectedPassword;
            if (values.TryGetValue("WifiPasswordProtected", out protectedPassword))
            {
                password = UnprotectText(protectedPassword);
            }

            return ssid.Length > 0 || password.Length > 0;
        }

        private void SaveRememberedWifiSettings(string ssid, string password)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(managerSettingsPath));
            var builder = new StringBuilder();
            builder.Append("WifiSsid=").Append(ssid).Append("\r\n");
            builder.Append("WifiPasswordProtected=").Append(ProtectText(password)).Append("\r\n");
            File.WriteAllText(managerSettingsPath, builder.ToString(), new UTF8Encoding(false));
        }

        private void ClearRememberedWifiSettings()
        {
            try
            {
                if (File.Exists(managerSettingsPath))
                {
                    File.Delete(managerSettingsPath);
                }
            }
            catch (Exception ex)
            {
                AppendOutput("清理已保存 Wi-Fi 密码失败: " + ex.Message);
            }
        }

        private string ProtectText(string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedData);
        }

        private string UnprotectText(string text)
        {
            var protectedData = Convert.FromBase64String(text ?? string.Empty);
            var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }

        private Dictionary<string, string> DefaultConfig()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ServiceName", ServiceName },
                { "RepoRoot", repoRoot },
                { "Transport", "serial" },
                { "Serial", "COM4" },
                { "Baud", "9600" },
                { "Listen", "8765" },
                { "DeviceHost", "agent-light.local" },
                { "DevicePort", "8766" },
                { "Initial", "claude:idle" }
            };
        }

        private void WriteConfig(Dictionary<string, string> values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            var order = new[] { "ServiceName", "RepoRoot", "Transport", "Serial", "Baud", "Listen", "DeviceHost", "DevicePort", "Initial" };
            var builder = new StringBuilder();
            foreach (var key in order)
            {
                builder.Append(key).Append('=').Append(GetConfig(key, "")).Append("\r\n");
            }
            File.WriteAllText(configPath, builder.ToString(), new UTF8Encoding(false));
        }

        private void SaveConfigValue(string key, string value)
        {
            config[key] = value;
        }

        private string GetConfig(string key, string fallback)
        {
            string value;
            return config != null && config.TryGetValue(key, out value) && value.Length > 0 ? value : fallback;
        }

        private string ReadLogs()
        {
            var paths = new[]
            {
                Path.Combine(logDirectory, "bridge-service.out.log"),
                Path.Combine(logDirectory, "bridge-service.err.log")
            };

            var entries = new List<LogEntry>();
            var sequence = 0;
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                foreach (var line in Tail(path, 120))
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }
                    if (!ShouldShowServiceLogLine(line))
                    {
                        continue;
                    }

                    entries.Add(new LogEntry(ParseLogTimestamp(line), sequence, line));
                    sequence += 1;
                }
            }

            entries.Sort(delegate(LogEntry left, LogEntry right)
            {
                var timestampCompare = left.Timestamp.CompareTo(right.Timestamp);
                return timestampCompare != 0 ? timestampCompare : left.Sequence.CompareTo(right.Sequence);
            });

            var output = new StringBuilder();
            var start = Math.Max(0, entries.Count - 160);
            for (var index = start; index < entries.Count; index += 1)
            {
                output.AppendLine(entries[index].Line);
            }

            return output.Length > 0 ? output.ToString() : "暂无日志";
        }

        private bool ShouldShowServiceLogLine(string line)
        {
            var command = ExtractLoggedCommand(line, "ok");
            return command.Length == 0 || IsManagementCommand(command);
        }

        private string ExtractLoggedCommand(string line, string expectedResponse)
        {
            var value = line ?? string.Empty;
            var arrowIndex = value.IndexOf(" -> ", StringComparison.Ordinal);
            var responseIndex = value.LastIndexOf(" <- ", StringComparison.Ordinal);
            if (arrowIndex < 0 || responseIndex <= arrowIndex)
            {
                return string.Empty;
            }

            var response = value.Substring(responseIndex + 4).Trim();
            if (!string.Equals(response, expectedResponse, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value.Substring(arrowIndex + 4, responseIndex - arrowIndex - 4).Trim();
        }

        private void RefreshLogs()
        {
            if (logRefreshInProgress)
            {
                return;
            }

            logRefreshInProgress = true;
            Task.Factory.StartNew(delegate
            {
                return ReadLogs();
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).ContinueWith(delegate(Task<string> task)
            {
                logRefreshInProgress = false;
                if (task.Exception != null)
                {
                    SetLogText("读取服务日志失败: " + task.Exception.GetBaseException().Message);
                    return;
                }

                if (!string.Equals(currentLogText, task.Result, StringComparison.Ordinal))
                {
                    SetLogText(task.Result);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void StartRealtimeLogRefresh()
        {
            if (logRefreshTimer != null)
            {
                return;
            }

            InitializeLightEventLogPosition();
            logRefreshTimer = new DispatcherTimer();
            logRefreshTimer.Interval = TimeSpan.FromSeconds(2);
            logRefreshTimer.Tick += delegate
            {
                RefreshLogs();
                RefreshLightEvents();
            };
            logRefreshTimer.Start();
        }

        private void StopRealtimeLogRefresh()
        {
            if (logRefreshTimer == null)
            {
                return;
            }

            logRefreshTimer.Stop();
            logRefreshTimer = null;
        }

        private void InitializeLightEventLogPosition()
        {
            try
            {
                lightEventLogPosition = File.Exists(lightEventLogPath) ? new FileInfo(lightEventLogPath).Length : 0;
                lightEventLogPositionInitialized = true;
            }
            catch
            {
                lightEventLogPosition = 0;
                lightEventLogPositionInitialized = true;
            }
        }

        private void RefreshLightEvents()
        {
            if (!lightEventLogPositionInitialized)
            {
                InitializeLightEventLogPosition();
            }

            if (!File.Exists(lightEventLogPath))
            {
                lightEventLogPosition = 0;
                return;
            }

            try
            {
                using (var stream = new FileStream(lightEventLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (lightEventLogPosition > stream.Length)
                    {
                        lightEventLogPosition = 0;
                    }

                    stream.Position = lightEventLogPosition;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var message = FormatLightEventLine(line);
                            if (message.Length > 0)
                            {
                                AppendOutput(message);
                            }
                        }

                        lightEventLogPosition = stream.Position;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput("读取状态灯事件失败: " + ex.Message);
            }
        }

        private string FormatLightEventLine(string line)
        {
            var value = StripLogTimestamp(line);
            return value.Length == 0 ? string.Empty : "状态灯: " + value;
        }

        private string StripLogTimestamp(string line)
        {
            var value = (line ?? string.Empty).Trim();
            DateTime timestamp;
            if (value.Length >= 20 && DateTime.TryParse(value.Substring(0, 19), out timestamp))
            {
                return value.Substring(20).Trim();
            }

            return value;
        }

        private void SetLogText(string text)
        {
            currentLogText = text ?? string.Empty;
            SetLogTextNow(currentLogText);
        }

        private void SetLogTextNow(string text)
        {
            if (logText == null)
            {
                return;
            }

            logText.Text = text ?? string.Empty;
            ScrollTextBoxToEnd(logText);
        }

        private IEnumerable<string> Tail(string path, int count)
        {
            var lines = new Queue<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Enqueue(line);
                    while (lines.Count > count)
                    {
                        lines.Dequeue();
                    }
                }
            }

            foreach (var line in lines)
            {
                yield return line;
            }
        }

        private DateTime ParseLogTimestamp(string line)
        {
            DateTime timestamp;
            if (line != null && line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out timestamp))
            {
                return timestamp;
            }

            return DateTime.MinValue;
        }

        private void RunBackground(string action, Func<string> work)
        {
            RunBackground(action, work, AppendOutput);
        }

        private void RunBackground(string action, Func<string> work, Action<string> onSuccess)
        {
            RunBackground(action, work, onSuccess, true);
        }

        private void RunBackground(string action, Func<string> work, Action<string> onSuccess, bool refreshLogsAfter)
        {
            RunBackground(action, work, onSuccess, refreshLogsAfter, true, true);
        }

        private void RunBackground(string action, Func<string> work, Action<string> onSuccess, bool refreshLogsAfter, bool showProgress, bool affectBusy)
        {
            if (affectBusy)
            {
                SetBusy(true);
            }
            if (showProgress)
            {
                AppendOutput(action + "...");
            }

            Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).ContinueWith(delegate(Task<string> task)
            {
                if (affectBusy)
                {
                    SetBusy(false);
                }
                if (task.Exception != null)
                {
                    ShowError(task.Exception.GetBaseException().Message);
                    return;
                }

                var result = task.Result ?? string.Empty;
                if (onSuccess != null)
                {
                    onSuccess(result);
                }
                if (refreshLogsAfter)
                {
                    RefreshLogs();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
            foreach (var button in busyButtons)
            {
                button.IsEnabled = !busy;
            }
        }

        private void AppendOutput(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(delegate { AppendOutput(text); }), DispatcherPriority.Background);
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var line = DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
            outputHistoryText.Append(line);
            TrimTextBuffer(outputHistoryText, 30000);
            AppendOutputText(line);
        }

        private void AppendOutputText(string text)
        {
            if (outputText == null)
            {
                return;
            }

            outputText.AppendText(text);
            ScrollTextBoxToEnd(outputText);
        }

        private void ScrollSelectedOutputTabToEnd()
        {
            if (outputTabs == null)
            {
                return;
            }

            if (object.ReferenceEquals(outputTabs.SelectedItem, outputTab))
            {
                ScrollTextBoxToEnd(outputText);
            }
            else if (object.ReferenceEquals(outputTabs.SelectedItem, logTab))
            {
                ScrollTextBoxToEnd(logText);
            }
        }

        private void ScrollTextBoxToEnd(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            textBox.Dispatcher.BeginInvoke(new Action(delegate
            {
                textBox.CaretIndex = textBox.Text == null ? 0 : textBox.Text.Length;
                textBox.ScrollToEnd();
            }), DispatcherPriority.ContextIdle);
        }

        private void TrimTextBuffer(StringBuilder buffer, int maxLength)
        {
            if (buffer.Length <= maxLength)
            {
                return;
            }

            buffer.Remove(0, buffer.Length - maxLength);
        }

        private void ApplyStatusSnapshot(StatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            var deviceStatus = FormatDeviceSummary(snapshot.DeviceResponse);
            serviceStatusText.Text = snapshot.ServiceStatus;
            bridgeStatusText.Text = snapshot.BridgeStatus;
            deviceStatusText.Text = deviceStatus;
            transportStatusText.Text = snapshot.TransportStatus;
            SetLogText(snapshot.Logs);
        }

        private string FormatCommandResponse(string response)
        {
            var value = (response ?? string.Empty).Trim();
            if (string.Equals(value, "pong", StringComparison.OrdinalIgnoreCase))
            {
                return "设备在线";
            }

            if (string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return "命令已执行";
            }

            return response;
        }

        private void ShowError(string message)
        {
            AppendOutput("错误: " + message);
            MessageBox.Show(this, message, "Agent Light", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private string FormatDeviceSummary(string response)
        {
            var value = (response ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return "无响应";
            }

            if (value.StartsWith("sys:info;", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "pong", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return "在线";
            }

            if (value.StartsWith("WiFi ", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Connecting to WiFi", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Bluetooth ", StringComparison.OrdinalIgnoreCase))
            {
                return "启动中";
            }

            return "响应异常";
        }

        private string FormatTransportSummary()
        {
            var transport = GetConfig("Transport", "serial");
            if (IsTcpTransport(transport))
            {
                return "Wi-Fi TCP " + GetConfig("DeviceHost", "agent-light.local") + ":" + GetConfig("DevicePort", "8766");
            }

            return "串口 " + GetConfig("Serial", "COM4") + " / " + GetConfig("Baud", "9600") + " baud";
        }

        private Dictionary<string, string> ParseResponse(string response)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = (response ?? string.Empty).Split(';');
            foreach (var part in parts)
            {
                var equals = part.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }
                values[part.Substring(0, equals)] = part.Substring(equals + 1);
            }
            return values;
        }

        private string GetValue(Dictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) && value.Length > 0 ? value : fallback;
        }

        private int ParsePort(string value, string label)
        {
            int port;
            if (!int.TryParse((value ?? string.Empty).Trim(), out port) || port <= 0 || port > 65535)
            {
                throw new InvalidOperationException(label + "无效。");
            }
            return port;
        }

        private bool IsTcpTransport(string value)
        {
            return string.Equals(value, "tcp", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "wifi", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsManagementCommand(string command)
        {
            var value = (command ?? string.Empty).Trim().ToLowerInvariant();
            if (value.StartsWith(SilentCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(SilentCommandPrefix.Length).Trim();
            }
            return value.StartsWith("sys:", StringComparison.Ordinal) || value.StartsWith("wifi:", StringComparison.Ordinal) || value == "reboot";
        }

        private string RedactCommand(string command)
        {
            if (command == null || !command.StartsWith("wifi:set:", StringComparison.OrdinalIgnoreCase))
            {
                return command ?? string.Empty;
            }

            var payload = command.Substring("wifi:set:".Length);
            var split = payload.IndexOf(':');
            if (split <= 0)
            {
                return "wifi:set:<invalid>";
            }

            return "wifi:set:" + payload.Substring(0, split) + ":<redacted>";
        }

        private void UsePreset(string command)
        {
            commandText.Text = command;
            SendManagerCommand(command, true);
        }

        private GroupBox CreateGroup(string header)
        {
            return new GroupBox
            {
                Header = header,
                Padding = new Thickness(8),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 210, 218))
            };
        }

        private Grid CreateFormGrid(int rows)
        {
            var grid = new Grid { Margin = new Thickness(12, 6, 12, 6) };
            for (var index = 0; index < rows; index += 1)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            }
            return grid;
        }

        private TabItem CreateTab(string header, object content)
        {
            return new TabItem { Header = header, Content = content };
        }

        private TextBlock CreateLabel(string text, bool bold)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private TextBlock CreateValueText(string text)
        {
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private TextBox CreateLogTextBox()
        {
            return new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(Color.FromRgb(250, 251, 253)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 210, 218))
            };
        }

        private Button CreateButton(string text, RoutedEventHandler handler)
        {
            return CreateButton(text, handler, true);
        }

        private Button CreateButton(string text, RoutedEventHandler handler, bool affectBusy)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 92,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 0, 10, 0)
            };
            button.Click += handler;
            if (affectBusy)
            {
                busyButtons.Add(button);
            }
            return button;
        }

        private TextBlock AddField(Grid grid, string label, Control editor, int row, int column)
        {
            var labelText = CreateLabel(label, false);
            grid.Children.Add(labelText);
            Grid.SetRow(labelText, row);
            Grid.SetColumn(labelText, 0);

            editor.Margin = new Thickness(0, 4, 8, 4);
            editor.VerticalContentAlignment = VerticalAlignment.Center;
            grid.Children.Add(editor);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, column);
            return labelText;
        }

        private void SetVisible(UIElement element, bool visible)
        {
            if (element != null)
            {
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void AddText(Grid grid, string text, int row, int column, bool bold)
        {
            var label = CreateLabel(text, bold);
            grid.Children.Add(label);
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
        }

        private void AddCommandButton(Grid grid, string text, string command, int row, int column)
        {
            var button = CreateButton(text, delegate { SendManagerCommand(command, false); });
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Margin = new Thickness(0, 2, 8, 2);
            grid.Children.Add(button);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
        }

        private sealed class StatusSnapshot
        {
            public string ServiceStatus;
            public bool BridgeReachable;
            public string BridgeStatus;
            public string DeviceResponse;
            public string TransportStatus;
            public string Logs;
        }

        private sealed class LogEntry
        {
            public readonly DateTime Timestamp;
            public readonly int Sequence;
            public readonly string Line;

            public LogEntry(DateTime timestamp, int sequence, string line)
            {
                Timestamp = timestamp;
                Sequence = sequence;
                Line = line;
            }
        }

        private sealed class SerialPortOption
        {
            public readonly string PortName;
            public readonly string Kind;

            public SerialPortOption(string portName, string kind)
            {
                PortName = portName;
                Kind = kind;
            }

            public override string ToString()
            {
                return PortName + " - " + Kind;
            }
        }

        private static int ComparePortNames(SerialPortOption left, SerialPortOption right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            return GetPortNumber(left.PortName).CompareTo(GetPortNumber(right.PortName));
        }

        private static int GetPortNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return int.MaxValue;
            }

            var digits = new StringBuilder();
            foreach (var c in value)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                }
            }

            int number;
            return int.TryParse(digits.ToString(), out number) ? number : int.MaxValue;
        }
    }
}
