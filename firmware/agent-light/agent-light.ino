#include "BluetoothSerial.h"
#include <ESPmDNS.h>
#include <Preferences.h>
#include <WiFi.h>

#if __has_include("secrets.h")
#include "secrets.h"
#endif

#ifndef WIFI_SSID
#define WIFI_SSID ""
#endif

#ifndef WIFI_PASSWORD
#define WIFI_PASSWORD ""
#endif

const int MAIN_GREEN = 25;
const int MAIN_YELLOW = 26;
const int MAIN_RED = 27;

const int CODEX_GREEN = 19;
const int CODEX_YELLOW = 18;
const int CODEX_RED = 17;

const char *HOSTNAME = "agent-light";
const char *BLUETOOTH_NAME = "AgentLight";
const uint16_t COMMAND_PORT = 8766;
const unsigned long WIFI_CONNECT_TIMEOUT_MS = 15000;
const unsigned long WIFI_RETRY_INTERVAL_MS = 10000;

BluetoothSerial SerialBT;
Preferences preferences;
WiFiServer commandServer(COMMAND_PORT);
WiFiClient commandClient;

struct LightState {
  int pin;
  bool active;
  bool blink;
  unsigned long interval;
  unsigned long lastToggle;
  bool output;
};

struct LightGroup {
  LightState green;
  LightState yellow;
  LightState red;
};

LightGroup mainLight = {
  {MAIN_GREEN, true, false, 500, 0, true},
  {MAIN_YELLOW, false, true, 500, 0, false},
  {MAIN_RED, false, true, 500, 0, false},
};

LightGroup codexLight = {
  {CODEX_GREEN, true, false, 500, 0, true},
  {CODEX_YELLOW, false, true, 500, 0, false},
  {CODEX_RED, false, true, 500, 0, false},
};

String serialInput = "";
String bluetoothInput = "";
String networkInput = "";
String wifiSsid = "";
String wifiPassword = "";
unsigned long lastWiFiAttempt = 0;
bool bluetoothStarted = false;
bool commandServerStarted = false;
bool mdnsStarted = false;
bool rebootPending = false;
unsigned long rebootAt = 0;

void setup() {
  Serial.begin(9600);

  setupGroup(mainLight);
  setupGroup(codexLight);

  setOnlyGreen(mainLight);
  setOnlyGreen(codexLight);

  startBluetooth();
  loadWiFiConfig();
  connectWiFi();
}

void loop() {
  maintainWiFi();
  readSerialCommand();
  readBluetoothCommand();
  readNetworkCommand();
  updateGroup(mainLight);
  updateGroup(codexLight);
  handlePendingReboot();
}

void setupGroup(LightGroup &group) {
  pinMode(group.green.pin, OUTPUT);
  pinMode(group.yellow.pin, OUTPUT);
  pinMode(group.red.pin, OUTPUT);
}

void startBluetooth() {
  bluetoothStarted = SerialBT.begin(BLUETOOTH_NAME);
  if (bluetoothStarted) {
    Serial.print("Bluetooth SPP started: ");
    Serial.println(BLUETOOTH_NAME);
  } else {
    Serial.println("Bluetooth SPP start failed; USB serial and WiFi remain available.");
  }
}

void loadWiFiConfig() {
  preferences.begin("agent-light", true);
  wifiSsid = preferences.getString("ssid", WIFI_SSID);
  wifiPassword = preferences.getString("password", WIFI_PASSWORD);
  preferences.end();
}

void saveWiFiConfig(String ssid, String password) {
  preferences.begin("agent-light", false);
  preferences.putString("ssid", ssid);
  preferences.putString("password", password);
  preferences.end();

  wifiSsid = ssid;
  wifiPassword = password;
}

void clearSavedWiFiConfig() {
  preferences.begin("agent-light", false);
  preferences.remove("ssid");
  preferences.remove("password");
  preferences.end();

  loadWiFiConfig();
}

void connectWiFi() {
  lastWiFiAttempt = millis();
  commandServerStarted = false;
  mdnsStarted = false;

  if (wifiSsid.length() == 0) {
    Serial.println("WiFi SSID is empty; USB serial and Bluetooth commands remain available.");
    return;
  }

  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);
  WiFi.setTxPower(WIFI_POWER_19_5dBm);
  WiFi.setHostname(HOSTNAME);
  WiFi.begin(wifiSsid.c_str(), wifiPassword.c_str());

  Serial.print("Connecting to WiFi SSID: ");
  Serial.println(wifiSsid);

  unsigned long startedAt = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - startedAt < WIFI_CONNECT_TIMEOUT_MS) {
    delay(250);
  }

  if (WiFi.status() == WL_CONNECTED) {
    startNetworkServices();
  } else {
    Serial.println("WiFi connect timeout; USB serial commands remain available.");
  }
}

void maintainWiFi() {
  if (WiFi.status() == WL_CONNECTED) {
    if (!commandServerStarted) {
      startNetworkServices();
    }
    return;
  }

  commandServerStarted = false;
  mdnsStarted = false;

  if (millis() - lastWiFiAttempt >= WIFI_RETRY_INTERVAL_MS) {
    connectWiFi();
  }
}

void startNetworkServices() {
  if (!commandServerStarted) {
    commandServer.begin();
    commandServerStarted = true;
  }

  if (!mdnsStarted) {
    mdnsStarted = MDNS.begin(HOSTNAME);
    if (mdnsStarted) {
      MDNS.addService("agent-light", "tcp", COMMAND_PORT);
    }
  }

  Serial.print("WiFi connected. IP: ");
  Serial.println(WiFi.localIP());
  Serial.print("Command TCP port: ");
  Serial.println(COMMAND_PORT);
  Serial.print("mDNS name: ");
  Serial.print(HOSTNAME);
  Serial.println(".local");
}

void readSerialCommand() {
  while (Serial.available() > 0) {
    char c = Serial.read();

    if (c == '\n' || c == '\r') {
      if (serialInput.length() > 0) {
        writeSerialResponse(handleCommand(serialInput));
        serialInput = "";
      }
    } else {
      serialInput += c;
    }
  }
}

void readBluetoothCommand() {
  if (!bluetoothStarted) {
    return;
  }

  while (SerialBT.available() > 0) {
    char c = SerialBT.read();

    if (c == '\n' || c == '\r') {
      if (bluetoothInput.length() > 0) {
        writeBluetoothResponse(handleCommand(bluetoothInput));
        bluetoothInput = "";
      }
    } else {
      bluetoothInput += c;
    }
  }
}

void readNetworkCommand() {
  if (WiFi.status() != WL_CONNECTED || !commandServerStarted) {
    return;
  }

  WiFiClient incomingClient = commandServer.available();
  if (incomingClient) {
    if (commandClient) commandClient.stop();
    commandClient = incomingClient;
    networkInput = "";
  }

  while (commandClient.available() > 0) {
    char c = commandClient.read();

    if (c == '\n' || c == '\r') {
      if (networkInput.length() > 0) {
        writeNetworkResponse(handleCommand(networkInput));
        networkInput = "";
      }
    } else {
      networkInput += c;
    }
  }

  if (commandClient && !commandClient.connected()) {
    if (networkInput.length() > 0) {
      writeNetworkResponse(handleCommand(networkInput));
      networkInput = "";
    }
    commandClient.stop();
  }
}

void writeSerialResponse(String response) {
  if (response.length() > 0) {
    Serial.println(response);
  }
}

void writeBluetoothResponse(String response) {
  if (bluetoothStarted && response.length() > 0) {
    SerialBT.println(response);
  }
}

void writeNetworkResponse(String response) {
  if (commandClient && response.length() > 0) {
    commandClient.println(response);
  }
}

String handleCommand(String cmd) {
  cmd.trim();
  if (cmd.length() == 0) return "";

  String lower = cmd;
  lower.toLowerCase();

  if (lower.startsWith("sys:") || lower.startsWith("wifi:") || lower == "reboot") {
    return handleSystemCommand(cmd);
  }

  LightGroup *target = &mainLight;
  int targetColon = cmd.indexOf(':');

  if (targetColon > 0) {
    String prefix = cmd.substring(0, targetColon);
    prefix.toLowerCase();

    if (prefix == "codex" || prefix == "c") {
      target = &codexLight;
      cmd = cmd.substring(targetColon + 1);
      cmd.trim();
    } else if (prefix == "claude" || prefix == "main" || prefix == "default" || prefix == "agent") {
      target = &mainLight;
      cmd = cmd.substring(targetColon + 1);
      cmd.trim();
    }
  }

  return handleLightCommand(*target, cmd) ? "ok" : "error:unknown-command";
}

String handleSystemCommand(String cmd) {
  String lower = cmd;
  lower.toLowerCase();

  if (lower == "sys:ping") {
    return "pong";
  }

  if (lower == "sys:info") {
    String response = "sys:info";
    response += ";name=" + String(HOSTNAME);
    response += ";bt=" + String(BLUETOOTH_NAME);
    response += ";tcp_port=" + String(COMMAND_PORT);
    response += ";wifi=" + wifiStatusLabel();
    response += ";ssid=" + wifiSsid;
    response += ";ip=" + localIPString();
    response += ";rssi=" + String(WiFi.status() == WL_CONNECTED ? WiFi.RSSI() : 0);
    response += ";heap=" + String(ESP.getFreeHeap());
    return response;
  }

  if (lower == "wifi:status") {
    String response = "wifi:status";
    response += ";state=" + wifiStatusLabel();
    response += ";ssid=" + wifiSsid;
    response += ";ip=" + localIPString();
    response += ";rssi=" + String(WiFi.status() == WL_CONNECTED ? WiFi.RSSI() : 0);
    return response;
  }

  if (lower == "wifi:scan") {
    return scanWiFiNetworks();
  }

  if (lower.startsWith("wifi:set:")) {
    String payload = cmd.substring(9);
    int split = payload.indexOf(':');
    if (split <= 0 || split >= payload.length() - 1) {
      return "error:usage:wifi:set:<ssid>:<password>";
    }

    String ssid = payload.substring(0, split);
    String password = payload.substring(split + 1);
    ssid.trim();
    password.trim();

    if (ssid.length() == 0) {
      return "error:ssid-empty";
    }

    saveWiFiConfig(ssid, password);
    WiFi.disconnect();
    delay(100);
    connectWiFi();

    return "ok:wifi:set;state=" + wifiStatusLabel() + ";ssid=" + wifiSsid + ";ip=" + localIPString();
  }

  if (lower == "wifi:clear") {
    clearSavedWiFiConfig();
    WiFi.disconnect();
    delay(100);
    connectWiFi();
    return "ok:wifi:clear;state=" + wifiStatusLabel() + ";ssid=" + wifiSsid + ";ip=" + localIPString();
  }

  if (lower == "reboot") {
    rebootPending = true;
    rebootAt = millis() + 500;
    return "ok:rebooting";
  }

  return "error:unknown-system-command";
}

String wifiStatusLabel() {
  wl_status_t status = WiFi.status();
  if (status == WL_CONNECTED) return "connected";
  if (status == WL_NO_SSID_AVAIL) return "no-ssid";
  if (status == WL_CONNECT_FAILED) return "connect-failed";
  if (status == WL_CONNECTION_LOST) return "connection-lost";
  if (status == WL_DISCONNECTED) return "disconnected";
  if (status == WL_IDLE_STATUS) return "idle";
  return "unknown";
}

String localIPString() {
  if (WiFi.status() != WL_CONNECTED) {
    return "0.0.0.0";
  }

  return WiFi.localIP().toString();
}

String scanWiFiNetworks() {
  int count = WiFi.scanNetworks();
  if (count < 0) {
    return "error:wifi-scan-failed";
  }

  String response = "wifi:scan;count=" + String(count);
  int limit = count > 10 ? 10 : count;
  for (int i = 0; i < limit; i += 1) {
    response += ";";
    response += WiFi.SSID(i);
    response += ",";
    response += String(WiFi.RSSI(i));
  }

  WiFi.scanDelete();
  return response;
}

void handlePendingReboot() {
  if (rebootPending && millis() >= rebootAt) {
    ESP.restart();
  }
}

bool handleLightCommand(LightGroup &group, String cmd) {
  if (cmd.length() == 0) return false;

  String state = cmd;
  state.toLowerCase();

  if (state == "idle" || state == "green") {
    setMode(group.green, true, false, 250);
    group.yellow.active = false;
    group.red.active = false;
    return true;
  }

  if (state == "thinking" || state == "think" || state == "yellow") {
    setMode(group.yellow, true, true, 250);
    group.green.active = false;
    group.red.active = false;
    return true;
  }

  if (state == "running" || state == "busy" || state == "red") {
    setMode(group.red, true, true, 250);
    group.green.active = false;
    group.yellow.active = false;
    return true;
  }

  char light = cmd.charAt(0);

  if (cmd == "G") {
    setMode(group.green, true, false, 500);
    group.yellow.active = false;
    group.red.active = false;
    return true;
  }

  if (cmd == "Y") {
    setMode(group.yellow, true, true, 500);
    group.green.active = false;
    group.red.active = false;
    return true;
  }

  if (cmd == "R") {
    setMode(group.red, true, true, 500);
    group.green.active = false;
    group.yellow.active = false;
    return true;
  }

  int firstColon = cmd.indexOf(':');
  int secondColon = cmd.indexOf(':', firstColon + 1);

  if (firstColon < 0) return false;

  String mode = cmd.substring(firstColon + 1, secondColon < 0 ? cmd.length() : secondColon);
  unsigned long interval = 500;

  if (secondColon > 0) {
    interval = cmd.substring(secondColon + 1).toInt();
    if (interval < 50) interval = 50;
  }

  LightState *target = getLight(group, light);
  if (target == NULL) return false;

  group.green.active = false;
  group.yellow.active = false;
  group.red.active = false;

  if (mode == "on") {
    setMode(*target, true, false, interval);
  } else if (mode == "blink") {
    setMode(*target, true, true, interval);
  } else if (mode == "off") {
    setMode(*target, false, false, interval);
  } else {
    return false;
  }

  return true;
}

LightState* getLight(LightGroup &group, char light) {
  if (light == 'G') return &group.green;
  if (light == 'Y') return &group.yellow;
  if (light == 'R') return &group.red;
  return NULL;
}

void setMode(LightState &light, bool active, bool blink, unsigned long interval) {
  light.active = active;
  light.blink = blink;
  light.interval = interval;
  light.lastToggle = millis();
  light.output = active;

  digitalWrite(light.pin, active ? HIGH : LOW);
}

void updateGroup(LightGroup &group) {
  updateLight(group.green);
  updateLight(group.yellow);
  updateLight(group.red);
}

void updateLight(LightState &light) {
  if (!light.active) {
    digitalWrite(light.pin, LOW);
    return;
  }

  if (!light.blink) {
    digitalWrite(light.pin, HIGH);
    return;
  }

  unsigned long now = millis();
  if (now - light.lastToggle >= light.interval) {
    light.lastToggle = now;
    light.output = !light.output;
    digitalWrite(light.pin, light.output ? HIGH : LOW);
  }
}

void setOnlyGreen(LightGroup &group) {
  setMode(group.green, true, false, 500);
  group.yellow.active = false;
  group.red.active = false;
}
