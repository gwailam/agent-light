using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace AgentLightService
{
    public sealed class AgentLightBridgeService : ServiceBase
    {
        public const string DefaultServiceName = "AgentLightBridge";
        private const string SilentCommandPrefix = "__agent_light_silent__:";

        private readonly object syncRoot = new object();
        private ServiceConfig config;
        private SerialPort serialPort;
        private TcpListener listener;
        private Thread listenerThread;
        private Timer restartTimer;
        private bool stopping;
        private bool bridgeStarted;

        public AgentLightBridgeService()
        {
            ServiceName = LoadServiceName();
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        public static void Main(string[] args)
        {
            var service = new AgentLightBridgeService();

            if (args.Length > 0 && string.Equals(args[0], "--console", StringComparison.OrdinalIgnoreCase))
            {
                service.StartConsole(args);
                Console.WriteLine("Agent Light Bridge service is running. Press Enter to stop.");
                Console.ReadLine();
                service.StopConsole();
                return;
            }

            ServiceBase.Run(service);
        }

        public void StartConsole(string[] args)
        {
            OnStart(args);
        }

        public void StopConsole()
        {
            OnStop();
        }

        protected override void OnStart(string[] args)
        {
            stopping = false;
            config = ServiceConfig.Load();
            Directory.CreateDirectory(config.LogDirectory);
            LogService("service starting");
            StartBridge();
        }

        protected override void OnStop()
        {
            stopping = true;
            LogService("service stopping");
            SendIdleCommands();
            StopBridge();
            DisposeRestartTimer();
            LogService("service stopped");
        }

        protected override void OnShutdown()
        {
            OnStop();
        }

        private static string LoadServiceName()
        {
            try
            {
                return ServiceConfig.Load().WindowsServiceName;
            }
            catch
            {
                return DefaultServiceName;
            }
        }

        private void StartBridge()
        {
            lock (syncRoot)
            {
                if (stopping || bridgeStarted)
                {
                    return;
                }

                try
                {
                    config = ServiceConfig.Load();
                    Directory.CreateDirectory(config.LogDirectory);

                    if (config.IsSerialTransport)
                    {
                        OpenSerialLocked();
                    }
                    StartListenerLocked();

                    bridgeStarted = true;
                    LogService("direct bridge started transport=" + config.Transport + " target=" + config.TargetLabel + " listen=" + config.Listen);
                    AppendLine(config.BridgeOutLogPath, "Agent light direct bridge target " + config.TargetLabel);
                    AppendLine(config.BridgeOutLogPath, "Listening on 127.0.0.1:" + config.Listen);
                    SendCommand(config.Initial);
                }
                catch (Exception ex)
                {
                    LogService("failed to start direct bridge: " + ex.Message);
                    AppendLine(config.BridgeErrLogPath, "Failed to start direct bridge: " + ex.Message);
                    StopBridgeLocked();
                    ScheduleRestart();
                }
            }
        }

        private void StopBridge()
        {
            lock (syncRoot)
            {
                StopBridgeLocked();
            }
        }

        private void StopBridgeLocked()
        {
            bridgeStarted = false;

            try
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
            catch
            {
            }
            finally
            {
                listener = null;
            }

            try
            {
                if (listenerThread != null && listenerThread.IsAlive && Thread.CurrentThread != listenerThread)
                {
                    listenerThread.Join(1000);
                }
            }
            catch
            {
            }
            finally
            {
                listenerThread = null;
            }

            CloseSerialLocked();
        }

        private void OpenSerialLocked()
        {
            CloseSerialLocked();

            var port = new SerialPort(config.Serial, config.Baud, Parity.None, 8, StopBits.One);
            port.NewLine = "\n";
            port.WriteTimeout = 5000;
            port.ReadTimeout = 5000;
            port.Open();

            serialPort = port;
        }

        private void CloseSerialLocked()
        {
            var port = serialPort;
            serialPort = null;

            if (port == null)
            {
                return;
            }

            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
            catch
            {
            }
            finally
            {
                port.Dispose();
            }
        }

        private void StartListenerLocked()
        {
            listener = new TcpListener(IPAddress.Loopback, config.Listen);
            listener.Start();

            listenerThread = new Thread(ListenerLoop);
            listenerThread.IsBackground = true;
            listenerThread.Name = "AgentLightBridgeListener";
            listenerThread.Start();
        }

        private void ListenerLoop()
        {
            while (!stopping)
            {
                TcpClient client = null;

                try
                {
                    var currentListener = listener;
                    if (currentListener == null)
                    {
                        return;
                    }

                    client = currentListener.AcceptTcpClient();
                    var acceptedClient = client;
                    client = null;
                    ThreadPool.QueueUserWorkItem(delegate { HandleClient(acceptedClient); });
                }
                catch (SocketException ex)
                {
                    if (!stopping)
                    {
                        LogService("listener socket error: " + ex.Message);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!stopping)
                    {
                        LogService("listener error: " + ex.Message);
                    }
                }
                finally
                {
                    if (client != null)
                    {
                        client.Close();
                    }
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 2000;
                    using (var stream = client.GetStream())
                    {
                        var buffer = new byte[512];
                        var command = new StringBuilder();

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

                            var text = Encoding.UTF8.GetString(buffer, 0, read);
                            foreach (var c in text)
                            {
                                if (c == '\n' || c == '\r')
                                {
                                    FlushClientCommand(command, stream);
                                }
                                else
                                {
                                    command.Append(c);
                                }
                            }
                        }

                        FlushClientCommand(command, stream);
                    }
                }
                catch (Exception ex)
                {
                    LogService("client handling error: " + ex.Message);
                }
            }
        }

        private void FlushClientCommand(StringBuilder command, NetworkStream responseStream)
        {
            if (command.Length == 0)
            {
                return;
            }

            var rawCommand = command.ToString();
            string response;
            try
            {
                response = SendCommand(rawCommand);
            }
            catch (Exception ex)
            {
                if (!IsSilentRawCommand(rawCommand))
                {
                    throw;
                }
                response = "error:" + ex.Message;
            }
            try
            {
                var data = Encoding.UTF8.GetBytes((response ?? string.Empty).Trim() + "\n");
                responseStream.Write(data, 0, data.Length);
                responseStream.Flush();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            command.Length = 0;
        }

        private void SendIdleCommands()
        {
            try
            {
                SendCommand("claude:idle");
                SendCommand("codex:idle");
            }
            catch (Exception ex)
            {
                LogService("failed to send idle command: " + ex.Message);
            }
        }

        private string SendCommand(string rawCommand)
        {
            var command = (rawCommand ?? string.Empty).Trim();
            if (command.Length == 0)
            {
                return string.Empty;
            }
            var suppressCommandLog = false;
            if (command.StartsWith(SilentCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                command = command.Substring(SilentCommandPrefix.Length).Trim();
                suppressCommandLog = IsSilentLogAllowed(command);
            }

            if (command.Length == 0)
            {
                return string.Empty;
            }

            lock (syncRoot)
            {
                try
                {
                    string response;
                    if (config.IsTcpTransport)
                    {
                        response = SendTcpCommandLocked(command);
                    }
                    else
                    {
                        response = SendSerialCommandLocked(command);
                    }
                    if (IsSuccessfulLightCommand(command, response))
                    {
                        AppendLightEvent(command, response);
                    }
                    else if (!suppressCommandLog)
                    {
                        AppendLine(config.BridgeOutLogPath, DateTime.Now.ToString("HH:mm:ss") + " -> " + FormatCommandForLog(command) + (string.IsNullOrEmpty(response) ? string.Empty : " <- " + response));
                    }
                    return response;
                }
                catch (Exception ex)
                {
                    if (!suppressCommandLog)
                    {
                        AppendLine(config.BridgeErrLogPath, "Command write failed: " + ex.Message);
                    }
                    if (config.IsSerialTransport)
                    {
                        CloseSerialLocked();
                    }
                    throw;
                }
            }
        }

        private string SendSerialCommandLocked(string command)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                OpenSerialLocked();
            }

            serialPort.DiscardInBuffer();
            serialPort.Write(command + "\n");
            return ReadSerialResponseLocked(command);
        }

        private string SendTcpCommandLocked(string command)
        {
            using (var client = new TcpClient())
            {
                var result = client.BeginConnect(config.DeviceHost, config.DevicePort, null, null);
                if (!result.AsyncWaitHandle.WaitOne(2000))
                {
                    throw new IOException("TCP connect timed out: " + config.DeviceHost + ":" + config.DevicePort);
                }

                client.EndConnect(result);
                client.ReceiveTimeout = GetResponseTimeout(command);
                var data = Encoding.UTF8.GetBytes(command + "\n");
                using (var stream = client.GetStream())
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                    return ReadNetworkResponse(stream);
                }
            }
        }

        private string ReadSerialResponseLocked(string command)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(GetResponseTimeout(command));

            while (DateTime.UtcNow < deadline)
            {
                var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0)
                {
                    break;
                }

                try
                {
                    serialPort.ReadTimeout = Math.Min(remaining, 500);
                    var response = (serialPort.ReadLine() ?? string.Empty).Trim();
                    if (IsExpectedResponse(command, response))
                    {
                        return response;
                    }
                }
                catch (System.TimeoutException)
                {
                }
            }

            return string.Empty;
        }

        private static string ReadNetworkResponse(NetworkStream stream)
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

        private static int GetResponseTimeout(string command)
        {
            var value = (command ?? string.Empty).Trim().ToLowerInvariant();
            if (IsManagementCommand(value))
            {
                return 22000;
            }

            return 1500;
        }

        private static bool IsExpectedResponse(string command, string response)
        {
            var value = (response ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return false;
            }

            if (value.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var request = (command ?? string.Empty).Trim().ToLowerInvariant();
            if (request == "sys:ping")
            {
                return string.Equals(value, "pong", StringComparison.OrdinalIgnoreCase);
            }

            if (request == "sys:info")
            {
                return value.StartsWith("sys:info;", StringComparison.OrdinalIgnoreCase);
            }

            if (request == "wifi:status")
            {
                return value.StartsWith("wifi:status;", StringComparison.OrdinalIgnoreCase);
            }

            if (request == "wifi:scan")
            {
                return value.StartsWith("wifi:scan;", StringComparison.OrdinalIgnoreCase);
            }

            if (request.StartsWith("wifi:set:", StringComparison.Ordinal))
            {
                return value.StartsWith("ok:wifi:set;", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase);
            }

            if (request == "wifi:clear")
            {
                return value.StartsWith("ok:wifi:clear;", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase);
            }

            if (request == "reboot")
            {
                return string.Equals(value, "ok:rebooting", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSilentRawCommand(string rawCommand)
        {
            var value = (rawCommand ?? string.Empty).Trim();
            if (!value.StartsWith(SilentCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return IsSilentLogAllowed(value.Substring(SilentCommandPrefix.Length));
        }

        private static bool IsSilentLogAllowed(string command)
        {
            var value = (command ?? string.Empty).Trim().ToLowerInvariant();
            return value == "sys:info" || value == "sys:ping" || value == "wifi:status";
        }

        private static bool IsSuccessfulLightCommand(string command, string response)
        {
            return !IsManagementCommand(command) &&
                string.Equals((response ?? string.Empty).Trim(), "ok", StringComparison.OrdinalIgnoreCase);
        }

        private void AppendLightEvent(string command, string response)
        {
            try
            {
                AppendLine(config.LightEventLogPath, FormatCommandForLog(command) + " <- " + (response ?? string.Empty).Trim());
            }
            catch
            {
            }
        }

        private static bool IsManagementCommand(string command)
        {
            var value = (command ?? string.Empty).Trim().ToLowerInvariant();
            return value.StartsWith("sys:", StringComparison.Ordinal) ||
                value.StartsWith("wifi:", StringComparison.Ordinal) ||
                value == "reboot";
        }

        private static string FormatCommandForLog(string command)
        {
            var value = (command ?? string.Empty).Trim();
            if (!value.StartsWith("wifi:set:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var payload = value.Substring("wifi:set:".Length);
            var split = payload.IndexOf(':');
            if (split <= 0)
            {
                return "wifi:set:<invalid>";
            }

            return "wifi:set:" + payload.Substring(0, split) + ":<redacted>";
        }

        private void ScheduleRestart()
        {
            if (stopping)
            {
                return;
            }

            DisposeRestartTimer();
            restartTimer = new Timer(delegate { StartBridge(); }, null, 5000, Timeout.Infinite);
            LogService("direct bridge restart scheduled in 5 seconds");
        }

        private void DisposeRestartTimer()
        {
            var timer = restartTimer;
            restartTimer = null;

            if (timer != null)
            {
                timer.Dispose();
            }
        }

        private void LogService(string message)
        {
            try
            {
                var current = config ?? ServiceConfig.Load();
                Directory.CreateDirectory(current.LogDirectory);
                AppendLine(current.ServiceLogPath, message);
            }
            catch
            {
            }
        }

        private static void AppendLine(string path, string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine;
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    internal sealed class ServiceConfig
    {
        public string WindowsServiceName;
        public string RepoRoot;
        public string Transport;
        public string Serial;
        public int Baud;
        public int Listen;
        public string DeviceHost;
        public int DevicePort;
        public string Initial;
        public string LogDirectory;
        public string ServiceLogPath;
        public string BridgeOutLogPath;
        public string BridgeErrLogPath;
        public string LightEventLogPath;

        public bool IsTcpTransport
        {
            get { return string.Equals(Transport, "tcp", StringComparison.OrdinalIgnoreCase) || string.Equals(Transport, "wifi", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsSerialTransport
        {
            get { return !IsTcpTransport; }
        }

        public string TargetLabel
        {
            get { return IsTcpTransport ? DeviceHost + ":" + DevicePort : Serial + " at " + Baud + " baud"; }
        }

        public static ServiceConfig Load()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(baseDirectory, ".."));
            var logDirectory = Path.Combine(baseDirectory, "logs");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var configPath = Path.Combine(baseDirectory, "agent-light-service.ini");
            if (File.Exists(configPath))
            {
                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    var equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, equalsIndex).Trim();
                    var value = line.Substring(equalsIndex + 1).Trim();
                    values[key] = Environment.ExpandEnvironmentVariables(value);
                }
            }

            var config = new ServiceConfig();
            config.WindowsServiceName = GetValue(values, "ServiceName", AgentLightBridgeService.DefaultServiceName);
            config.RepoRoot = Path.GetFullPath(GetValue(values, "RepoRoot", repoRoot));
            config.Transport = GetValue(values, "Transport", "serial");
            config.Serial = GetValue(values, "Serial", GetEnvironment("AGENT_LIGHT_SERIAL", "COM3"));
            config.Baud = GetInt(values, "Baud", 9600);
            config.Listen = GetInt(values, "Listen", 8765);
            config.DeviceHost = GetValue(values, "DeviceHost", "agent-light.local");
            config.DevicePort = GetInt(values, "DevicePort", 8766);
            config.Initial = GetValue(values, "Initial", "claude:idle");
            config.LogDirectory = Path.GetFullPath(GetValue(values, "LogDirectory", logDirectory));
            config.ServiceLogPath = Path.Combine(config.LogDirectory, "agent-light-service.log");
            config.BridgeOutLogPath = Path.Combine(config.LogDirectory, "bridge-service.out.log");
            config.BridgeErrLogPath = Path.Combine(config.LogDirectory, "bridge-service.err.log");
            config.LightEventLogPath = Path.Combine(config.LogDirectory, "light-events.log");
            return config;
        }

        private static string GetValue(Dictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) && value.Length > 0 ? value : fallback;
        }

        private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        {
            string value;
            int parsed;
            return values.TryGetValue(key, out value) && int.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static string GetEnvironment(string key, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
