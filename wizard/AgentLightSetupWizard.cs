using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Management;
using System.Net.Sockets;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentLightSetupWizard
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new WizardWindow());
        }
    }

    internal sealed class WizardWindow : Window
    {
        private const string ServiceName = "AgentLightBridge";
        private const string DriverKindCp210x = "CP210x（当前硬件）";
        private const string DriverKindCh340 = "CH340/CH341（未内置）";
        private const string DriverKindUnknown = "不确定";

        private readonly string repoRoot;
        private readonly string configPath;
        private readonly StringBuilder outputBuffer;

        private Grid root;
        private TextBlock stepText;
        private TextBlock titleText;
        private TextBlock summaryText;
        private ContentControl contentHost;
        private TextBox outputText;
        private Button backButton;
        private Button actionButton;
        private Button nextButton;

        private ComboBox driverKindCombo;
        private ComboBox serialCombo;
        private RadioButton usbRadio;
        private RadioButton bluetoothRadio;
        private RadioButton wifiRadio;

        private WizardStep step;
        private bool busy;
        private bool verified;
        private string selectedSerial;

        public WizardWindow()
        {
            repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            configPath = Path.Combine(repoRoot, "service", "agent-light-service.ini");
            outputBuffer = new StringBuilder();

            Title = "Agent Light 安装向导";
            Width = 720;
            Height = 640;
            MinWidth = 720;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 251));

            BuildLayout();
            Loaded += delegate
            {
                AppendOutput("安装向导已启动");
                AppendOutput("工作目录: " + repoRoot);
                if (!IsAdministrator())
                {
                    AppendOutput("警告: 当前不是管理员权限，驱动和服务安装会失败");
                }
                Navigate(WizardStep.Welcome);
            };
        }

        private void BuildLayout()
        {
            root = new Grid();
            root.Margin = new Thickness(18);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(148) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var header = new StackPanel();
            header.Margin = new Thickness(0, 0, 0, 12);
            stepText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(92, 105, 122)),
                FontSize = 12
            };
            titleText = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 4)
            };
            summaryText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 72, 88))
            };
            header.Children.Add(stepText);
            header.Children.Add(titleText);
            header.Children.Add(summaryText);
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            contentHost = new ContentControl();
            contentHost.Margin = new Thickness(0, 0, 0, 10);
            root.Children.Add(contentHost);
            Grid.SetRow(contentHost, 1);

            outputText = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(252, 253, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 212, 222))
            };
            root.Children.Add(outputText);
            Grid.SetRow(outputText, 2);

            var buttons = new Grid();
            buttons.Margin = new Thickness(0, 12, 0, 0);
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            backButton = CreateButton("上一步", delegate { OnBack(); });
            actionButton = CreateButton("刷新", delegate { OnAction(); });
            nextButton = CreateButton("下一步", delegate { OnNext(); });
            backButton.Width = 94;
            actionButton.Width = 130;
            nextButton.Width = 120;

            buttons.Children.Add(backButton);
            Grid.SetColumn(backButton, 0);
            buttons.Children.Add(actionButton);
            Grid.SetColumn(actionButton, 2);
            buttons.Children.Add(nextButton);
            Grid.SetColumn(nextButton, 3);

            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);
        }

        private Button CreateButton(string text, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Content = text,
                Height = 32,
                MinWidth = 92,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0)
            };
            button.Click += handler;
            return button;
        }

        private void Navigate(WizardStep nextStep)
        {
            step = nextStep;
            if (step == WizardStep.Welcome)
            {
                BuildWelcomePage();
            }
            else if (step == WizardStep.Driver)
            {
                BuildDriverPage();
            }
            else if (step == WizardStep.Port)
            {
                BuildPortPage();
            }
            else if (step == WizardStep.Service)
            {
                BuildServicePage();
            }
            else if (step == WizardStep.Connection)
            {
                BuildConnectionPage();
            }
            else if (step == WizardStep.Verify)
            {
                BuildVerifyPage();
            }
            else
            {
                BuildDonePage();
            }

            UpdateButtons();
        }

        private void BuildWelcomePage()
        {
            SetHeader("步骤 1 / 7", "准备安装", "向导会检查 CP210x 驱动、选择 ESP32 串口、安装桥接服务，并验证状态灯连接。");
            var panel = CreatePanel();
            panel.Children.Add(CreateParagraph("请先把 ESP32 开发板通过 USB 连接到电脑。"));
            panel.Children.Add(CreateParagraph("驱动和 Windows 服务安装需要管理员权限；本向导已配置为启动时申请管理员权限。"));
            panel.Children.Add(CreateKeyValue("当前权限", IsAdministrator() ? "管理员" : "非管理员"));
            panel.Children.Add(CreateKeyValue("服务状态", GetServiceStatusText()));
            panel.Children.Add(CreateKeyValue("项目目录", repoRoot));
            contentHost.Content = panel;
        }

        private void BuildDriverPage()
        {
            SetHeader("步骤 2 / 7", "检查串口驱动", "选择当前开发板的串口芯片型号。当前完整包内置 CP210x 驱动。");
            var panel = CreatePanel();
            panel.Children.Add(CreateParagraph("如果 Windows 已经识别出 CP210x 串口，可以直接进入下一步。"));

            var form = CreateFormGrid(2);
            AddLabel(form, "芯片型号", 0);
            driverKindCombo = new ComboBox { Height = 28 };
            driverKindCombo.Items.Add(DriverKindCp210x);
            driverKindCombo.Items.Add(DriverKindCh340);
            driverKindCombo.Items.Add(DriverKindUnknown);
            driverKindCombo.SelectedIndex = 0;
            driverKindCombo.SelectionChanged += delegate { UpdateButtons(); };
            form.Children.Add(driverKindCombo);
            Grid.SetRow(driverKindCombo, 0);
            Grid.SetColumn(driverKindCombo, 1);

            AddLabel(form, "检测结果", 1);
            form.Children.Add(CreateSerialSummary());
            Grid.SetRow(form.Children[form.Children.Count - 1], 1);
            Grid.SetColumn(form.Children[form.Children.Count - 1], 1);
            panel.Children.Add(form);

            contentHost.Content = panel;
        }

        private void BuildPortPage()
        {
            SetHeader("步骤 3 / 7", "选择 USB 串口", "选择 ESP32 对应的 COM 口。CP210x 设备会优先排在前面。");
            var panel = CreatePanel();
            var form = CreateFormGrid(2);
            AddLabel(form, "设备端口", 0);
            serialCombo = new ComboBox { Height = 28 };
            serialCombo.SelectionChanged += delegate
            {
                var option = serialCombo.SelectedItem as SerialPortOption;
                selectedSerial = option == null ? string.Empty : option.PortName;
                UpdateButtons();
            };
            form.Children.Add(serialCombo);
            Grid.SetRow(serialCombo, 0);
            Grid.SetColumn(serialCombo, 1);

            AddLabel(form, "提示", 1);
            var hint = CreateParagraph("如果列表为空，请重新插拔开发板后点击“刷新串口”。");
            form.Children.Add(hint);
            Grid.SetRow(hint, 1);
            Grid.SetColumn(hint, 1);
            panel.Children.Add(form);

            contentHost.Content = panel;
            RefreshPorts();
        }

        private void BuildServicePage()
        {
            SetHeader("步骤 4 / 7", "安装桥接服务", "桥接服务会开机自启，并把 Codex/Claude 的本机命令转发到 ESP32。");
            var panel = CreatePanel();
            panel.Children.Add(CreateKeyValue("选择端口", string.IsNullOrEmpty(selectedSerial) ? "未选择" : selectedSerial));
            panel.Children.Add(CreateKeyValue("服务名称", ServiceName));
            panel.Children.Add(CreateKeyValue("当前状态", GetServiceStatusText()));
            panel.Children.Add(CreateParagraph("点击“安装并继续”后，向导会写入串口配置并启动服务。"));
            contentHost.Content = panel;
        }

        private void BuildConnectionPage()
        {
            SetHeader("步骤 5 / 7", "选择连接方式", "第一版先跑通 USB 串口主路径；蓝牙和 Wi-Fi 后续接入独立配置页。");
            var panel = CreatePanel();
            usbRadio = new RadioButton
            {
                Content = "USB 串口：" + (string.IsNullOrEmpty(selectedSerial) ? "未选择" : selectedSerial),
                IsChecked = true,
                Margin = new Thickness(0, 6, 0, 8)
            };
            bluetoothRadio = new RadioButton
            {
                Content = "蓝牙虚拟串口（后续版本接入）",
                IsEnabled = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            wifiRadio = new RadioButton
            {
                Content = "Wi-Fi TCP（后续版本接入）",
                IsEnabled = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(usbRadio);
            panel.Children.Add(bluetoothRadio);
            panel.Children.Add(wifiRadio);
            panel.Children.Add(CreateParagraph("当前会使用 USB 串口完成首次安装和硬件验证。安装完成后可在 Manager 里切换蓝牙或 Wi-Fi。"));
            contentHost.Content = panel;
        }

        private void BuildVerifyPage()
        {
            SetHeader("步骤 6 / 7", "验证硬件连接", "向导会检查本机桥接服务、ESP32 响应，并发送状态灯测试命令。");
            var panel = CreatePanel();
            panel.Children.Add(CreateKeyValue("本机桥接", "127.0.0.1:" + GetListenPort()));
            panel.Children.Add(CreateKeyValue("设备端口", string.IsNullOrEmpty(selectedSerial) ? "未选择" : selectedSerial));
            panel.Children.Add(CreateParagraph("点击“开始验证”后，请观察状态灯是否短暂切换到思考状态，然后恢复空闲。"));
            contentHost.Content = panel;
        }

        private void BuildDonePage()
        {
            SetHeader("步骤 7 / 7", "安装完成", "桥接服务已安装，USB 串口主路径已验证。");
            var panel = CreatePanel();
            panel.Children.Add(CreateKeyValue("服务状态", GetServiceStatusText()));
            panel.Children.Add(CreateKeyValue("当前连接", "USB 串口 " + selectedSerial));
            panel.Children.Add(CreateParagraph("后续如需切换蓝牙、Wi-Fi、重新测试灯光或查看日志，请打开 Agent Light Manager。"));
            contentHost.Content = panel;
        }

        private StackPanel CreatePanel()
        {
            return new StackPanel
            {
                Margin = new Thickness(2, 2, 2, 2)
            };
        }

        private Grid CreateFormGrid(int rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < rows; index += 1)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            return grid;
        }

        private void AddLabel(Grid grid, string text, int row)
        {
            var label = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 14, 5)
            };
            grid.Children.Add(label);
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
        }

        private TextBlock CreateParagraph(string text)
        {
            return new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(48, 61, 76))
            };
        }

        private UIElement CreateKeyValue(string key, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyText = new TextBlock
            {
                Text = key,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(44, 55, 70))
            };
            var valueText = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(65, 78, 95))
            };
            grid.Children.Add(keyText);
            Grid.SetColumn(keyText, 0);
            grid.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);
            return grid;
        }

        private TextBlock CreateSerialSummary()
        {
            var ports = GetSerialPortOptions();
            var cp210xCount = 0;
            foreach (var port in ports)
            {
                if (port.IsCp210x)
                {
                    cp210xCount += 1;
                }
            }
            var text = ports.Count == 0
                ? "未发现 COM 口"
                : "发现 " + ports.Count + " 个 COM 口，其中 CP210x " + cp210xCount + " 个";
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 5)
            };
        }

        private void SetHeader(string stepValue, string title, string summary)
        {
            stepText.Text = stepValue;
            titleText.Text = title;
            summaryText.Text = summary;
        }

        private void UpdateButtons()
        {
            if (backButton == null)
            {
                return;
            }

            backButton.IsEnabled = !busy && step != WizardStep.Welcome && step != WizardStep.Done;
            actionButton.IsEnabled = !busy;
            nextButton.IsEnabled = !busy;
            actionButton.Visibility = Visibility.Visible;

            if (step == WizardStep.Welcome)
            {
                actionButton.Visibility = Visibility.Collapsed;
                nextButton.Content = "开始";
            }
            else if (step == WizardStep.Driver)
            {
                actionButton.Content = "安装 CP210x 驱动";
                nextButton.Content = "下一步";
                var selected = Convert.ToString(driverKindCombo == null ? null : driverKindCombo.SelectedItem);
                actionButton.IsEnabled = !busy && string.Equals(selected, DriverKindCp210x, StringComparison.Ordinal);
            }
            else if (step == WizardStep.Port)
            {
                actionButton.Content = "刷新串口";
                nextButton.Content = "下一步";
                nextButton.IsEnabled = !busy && !string.IsNullOrEmpty(selectedSerial);
            }
            else if (step == WizardStep.Service)
            {
                actionButton.Visibility = Visibility.Collapsed;
                nextButton.Content = "安装并继续";
                nextButton.IsEnabled = !busy && !string.IsNullOrEmpty(selectedSerial);
            }
            else if (step == WizardStep.Connection)
            {
                actionButton.Visibility = Visibility.Collapsed;
                nextButton.Content = "下一步";
            }
            else if (step == WizardStep.Verify)
            {
                actionButton.Visibility = Visibility.Collapsed;
                nextButton.Content = verified ? "下一步" : "开始验证";
            }
            else
            {
                actionButton.Visibility = Visibility.Collapsed;
                nextButton.Content = "打开 Manager";
            }
        }

        private void OnBack()
        {
            if (busy)
            {
                return;
            }

            if (step == WizardStep.Driver) Navigate(WizardStep.Welcome);
            else if (step == WizardStep.Port) Navigate(WizardStep.Driver);
            else if (step == WizardStep.Service) Navigate(WizardStep.Port);
            else if (step == WizardStep.Connection) Navigate(WizardStep.Service);
            else if (step == WizardStep.Verify) Navigate(WizardStep.Connection);
        }

        private void OnAction()
        {
            if (busy)
            {
                return;
            }

            if (step == WizardStep.Driver)
            {
                InstallCp210xDriver();
            }
            else if (step == WizardStep.Port)
            {
                RefreshPorts();
            }
        }

        private void OnNext()
        {
            if (busy)
            {
                return;
            }

            if (step == WizardStep.Welcome)
            {
                Navigate(WizardStep.Driver);
            }
            else if (step == WizardStep.Driver)
            {
                Navigate(WizardStep.Port);
            }
            else if (step == WizardStep.Port)
            {
                Navigate(WizardStep.Service);
            }
            else if (step == WizardStep.Service)
            {
                InstallServiceAndContinue();
            }
            else if (step == WizardStep.Connection)
            {
                Navigate(WizardStep.Verify);
            }
            else if (step == WizardStep.Verify)
            {
                if (verified)
                {
                    Navigate(WizardStep.Done);
                }
                else
                {
                    VerifyConnection();
                }
            }
            else
            {
                OpenManager();
            }
        }

        private void RefreshPorts()
        {
            var previous = selectedSerial;
            var ports = GetSerialPortOptions();
            if (serialCombo != null)
            {
                serialCombo.Items.Clear();
                foreach (var port in ports)
                {
                    serialCombo.Items.Add(port);
                }

                var selected = false;
                foreach (var item in serialCombo.Items)
                {
                    var option = item as SerialPortOption;
                    if (option != null && string.Equals(option.PortName, previous, StringComparison.OrdinalIgnoreCase))
                    {
                        serialCombo.SelectedItem = item;
                        selected = true;
                        break;
                    }
                }

                if (!selected && serialCombo.Items.Count > 0)
                {
                    serialCombo.SelectedIndex = 0;
                }
            }

            AppendOutput("串口扫描完成: " + ports.Count + " 个端口");
            UpdateButtons();
        }

        private void InstallCp210xDriver()
        {
            var driverScript = Path.Combine(repoRoot, "scripts", "install-cp210x-driver.ps1");
            if (!File.Exists(driverScript))
            {
                ShowError("找不到 CP210x 驱动安装脚本: " + driverScript);
                return;
            }

            RunBackground("安装 CP210x 驱动", delegate
            {
                return RunProcess("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Quote(driverScript));
            }, delegate(string output)
            {
                AppendOutput(output);
                AppendOutput("CP210x 驱动安装流程完成，请重新插拔 ESP32 后刷新串口。");
                BuildDriverPage();
            });
        }

        private void InstallServiceAndContinue()
        {
            if (string.IsNullOrEmpty(selectedSerial))
            {
                ShowError("请先选择 ESP32 对应的 COM 口。");
                return;
            }

            var script = Path.Combine(repoRoot, "scripts", "install-windows-service.ps1");
            if (!File.Exists(script))
            {
                ShowError("找不到服务安装脚本: " + script);
                return;
            }

            RunBackground("安装桥接服务", delegate
            {
                var args = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) +
                    " -Transport serial -Serial " + Quote(selectedSerial) +
                    " -Baud 9600 -Listen 8765 -Initial " + Quote("claude:idle");
                var output = RunProcess("powershell.exe", args);
                WaitForServiceRunning();
                return output;
            }, delegate(string output)
            {
                AppendOutput(output);
                AppendOutput("桥接服务安装完成");
                Navigate(WizardStep.Connection);
            });
        }

        private void VerifyConnection()
        {
            RunBackground("验证硬件连接", delegate
            {
                WaitForLocalBridge();
                var ping = SendTcpCommand("127.0.0.1", GetListenPort(), "sys:ping", 6000);
                if (!string.Equals(ping, "pong", StringComparison.OrdinalIgnoreCase))
                {
                    var info = SendTcpCommand("127.0.0.1", GetListenPort(), "sys:info", 22000);
                    if (string.IsNullOrEmpty(info))
                    {
                        throw new InvalidOperationException("ESP32 没有响应 sys:ping/sys:info。");
                    }
                }

                SendTcpCommand("127.0.0.1", GetListenPort(), "codex:thinking", 3000);
                Thread.Sleep(600);
                SendTcpCommand("127.0.0.1", GetListenPort(), "codex:idle", 3000);
                SendTcpCommand("127.0.0.1", GetListenPort(), "claude:idle", 3000);
                verified = true;
                return "ESP32 响应正常，状态灯测试命令已发送。";
            }, delegate(string output)
            {
                AppendOutput(output);
                Navigate(WizardStep.Done);
            });
        }

        private void OpenManager()
        {
            var manager = Path.Combine(repoRoot, "manager", "AgentLightManager.exe");
            if (!File.Exists(manager))
            {
                ShowError("找不到 Agent Light Manager: " + manager);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = manager,
                    WorkingDirectory = repoRoot,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                Close();
            }
            catch (Exception ex)
            {
                ShowError("打开 Manager 失败: " + ex.Message);
            }
        }

        private List<SerialPortOption> GetSerialPortOptions()
        {
            var ports = new List<SerialPortOption>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, Description, PNPDeviceID FROM Win32_SerialPort"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        var deviceId = Convert.ToString(item["DeviceID"]);
                        if (string.IsNullOrEmpty(deviceId))
                        {
                            continue;
                        }

                        var name = Convert.ToString(item["Name"]);
                        var description = Convert.ToString(item["Description"]);
                        var pnpDeviceId = Convert.ToString(item["PNPDeviceID"]);
                        AddPort(ports, new SerialPortOption(deviceId.ToUpperInvariant(), name, description, pnpDeviceId));
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput("WMI 串口扫描失败: " + ex.Message);
            }

            foreach (var portName in SerialPort.GetPortNames())
            {
                AddPort(ports, new SerialPortOption(portName.ToUpperInvariant(), "串口", string.Empty, string.Empty));
            }

            ports.Sort(ComparePorts);
            return ports;
        }

        private static void AddPort(List<SerialPortOption> ports, SerialPortOption option)
        {
            foreach (var existing in ports)
            {
                if (string.Equals(existing.PortName, option.PortName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            ports.Add(option);
        }

        private static int ComparePorts(SerialPortOption left, SerialPortOption right)
        {
            if (left.IsCp210x && !right.IsCp210x) return -1;
            if (!left.IsCp210x && right.IsCp210x) return 1;
            return GetPortNumber(left.PortName).CompareTo(GetPortNumber(right.PortName));
        }

        private static int GetPortNumber(string portName)
        {
            var digits = new StringBuilder();
            foreach (var c in portName ?? string.Empty)
            {
                if (char.IsDigit(c))
                {
                    digits.Append(c);
                }
            }

            int number;
            return int.TryParse(digits.ToString(), out number) ? number : int.MaxValue;
        }

        private void WaitForServiceRunning()
        {
            using (var service = new ServiceController(ServiceName))
            {
                service.Refresh();
                if (service.Status != ServiceControllerStatus.Running)
                {
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                }
            }
        }

        private string GetServiceStatusText()
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
            return Convert.ToString(status);
        }

        private int GetListenPort()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    return 8765;
                }

                foreach (var rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (!line.StartsWith("Listen=", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int port;
                    if (int.TryParse(line.Substring("Listen=".Length), out port) && port > 0)
                    {
                        return port;
                    }
                }
            }
            catch
            {
            }

            return 8765;
        }

        private void WaitForLocalBridge()
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            Exception lastError = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    TestTcpConnect("127.0.0.1", GetListenPort(), 800);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Thread.Sleep(500);
                }
            }

            throw new InvalidOperationException("无法连接本机桥接服务: " + (lastError == null ? "timeout" : lastError.Message));
        }

        private void TestTcpConnect(string host, int port, int timeoutMs)
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(host, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    throw new IOException("TCP 连接超时: " + host + ":" + port);
                }

                client.EndConnect(result);
            }
        }

        private string SendTcpCommand(string host, int port, string command, int timeoutMs)
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(host, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    throw new IOException("TCP 连接超时: " + host + ":" + port);
                }

                client.EndConnect(result);
                client.ReceiveTimeout = timeoutMs;
                using (var stream = client.GetStream())
                {
                    if (!string.IsNullOrEmpty(command))
                    {
                        var data = Encoding.UTF8.GetBytes(command + "\n");
                        stream.Write(data, 0, data.Length);
                        stream.Flush();
                    }

                    return ReadNetworkLine(stream);
                }
            }
        }

        private static string ReadNetworkLine(NetworkStream stream)
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

        private string RunProcess(string fileName, string arguments)
        {
            var output = new StringBuilder();
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                process.Start();
                output.Append(process.StandardOutput.ReadToEnd());
                output.Append(process.StandardError.ReadToEnd());
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(output.ToString().Trim().Length == 0
                        ? fileName + " failed with exit code " + process.ExitCode
                        : output.ToString().Trim());
                }
            }

            return output.ToString().Trim();
        }

        private void RunBackground(string action, Func<string> work, Action<string> onSuccess)
        {
            SetBusy(true);
            AppendOutput(action + "...");
            Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default).ContinueWith(delegate(Task<string> task)
            {
                SetBusy(false);
                if (task.Exception != null)
                {
                    ShowError(task.Exception.GetBaseException().Message);
                    return;
                }

                if (onSuccess != null)
                {
                    onSuccess(task.Result ?? string.Empty);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SetBusy(bool value)
        {
            busy = value;
            Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
            UpdateButtons();
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
            outputBuffer.Append(line);
            if (outputBuffer.Length > 30000)
            {
                outputBuffer.Remove(0, outputBuffer.Length - 30000);
            }
            outputText.Text = outputBuffer.ToString();
            outputText.CaretIndex = outputText.Text.Length;
            outputText.ScrollToEnd();
        }

        private void ShowError(string message)
        {
            AppendOutput("错误: " + message);
            MessageBox.Show(this, message, "Agent Light 安装向导", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private enum WizardStep
        {
            Welcome,
            Driver,
            Port,
            Service,
            Connection,
            Verify,
            Done
        }

        private sealed class SerialPortOption
        {
            public readonly string PortName;
            private readonly string name;
            private readonly string description;
            private readonly string pnpDeviceId;

            public SerialPortOption(string portName, string name, string description, string pnpDeviceId)
            {
                PortName = portName ?? string.Empty;
                this.name = name ?? string.Empty;
                this.description = description ?? string.Empty;
                this.pnpDeviceId = pnpDeviceId ?? string.Empty;
            }

            public bool IsCp210x
            {
                get
                {
                    var value = (name + " " + description + " " + pnpDeviceId).ToUpperInvariant();
                    return value.IndexOf("VID_10C4", StringComparison.Ordinal) >= 0 ||
                        value.IndexOf("CP210", StringComparison.Ordinal) >= 0 ||
                        value.IndexOf("SILICON LABS", StringComparison.Ordinal) >= 0 ||
                        value.IndexOf("SLAB", StringComparison.Ordinal) >= 0;
                }
            }

            public override string ToString()
            {
                var label = name.Length > 0 ? name : description;
                if (label.Length == 0)
                {
                    label = IsCp210x ? "CP210x 串口" : "串口";
                }
                return PortName + " - " + label;
            }
        }
    }
}
