const int GREEN = 5;
const int YELLOW = 6;
const int RED = 7;

struct LightState {
  int pin;
  bool active;
  bool blink;
  unsigned long interval;
  unsigned long lastToggle;
  bool output;
};

LightState green = {GREEN, true, false, 500, 0, true};
LightState yellow = {YELLOW, false, true, 500, 0, false};
LightState red = {RED, false, true, 500, 0, false};

String input = "";

void setup() {
  pinMode(GREEN, OUTPUT);
  pinMode(YELLOW, OUTPUT);
  pinMode(RED, OUTPUT);

  Serial.begin(9600);
  setOnlyGreen();
}

void loop() {
  readCommand();
  updateLight(green);
  updateLight(yellow);
  updateLight(red);
}

void readCommand() {
  while (Serial.available() > 0) {
    char c = Serial.read();

    if (c == '\n' || c == '\r') {
      if (input.length() > 0) {
        handleCommand(input);
        input = "";
      }
    } else {
      input += c;
    }
  }
}

void handleCommand(String cmd) {
  cmd.trim();
  char light = cmd.charAt(0);

  if (cmd == "G") {
    setMode(green, true, false, 500);
    yellow.active = false;
    red.active = false;
    return;
  }

  if (cmd == "Y") {
    setMode(yellow, true, true, 500);
    green.active = false;
    red.active = false;
    return;
  }

  if (cmd == "R") {
    setMode(red, true, true, 500);
    green.active = false;
    yellow.active = false;
    return;
  }

  int firstColon = cmd.indexOf(':');
  int secondColon = cmd.indexOf(':', firstColon + 1);

  if (firstColon < 0) return;

  String mode = cmd.substring(firstColon + 1, secondColon < 0 ? cmd.length() : secondColon);
  unsigned long interval = 500;

  if (secondColon > 0) {
    interval = cmd.substring(secondColon + 1).toInt();
    if (interval < 50) interval = 50;
  }

  LightState *target = getLight(light);
  if (target == NULL) return;

  green.active = false;
  yellow.active = false;
  red.active = false;

  if (mode == "on") {
    setMode(*target, true, false, interval);
  } else if (mode == "blink") {
    setMode(*target, true, true, interval);
  } else if (mode == "off") {
    setMode(*target, false, false, interval);
  }
}

LightState* getLight(char light) {
  if (light == 'G') return &green;
  if (light == 'Y') return &yellow;
  if (light == 'R') return &red;
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

void setOnlyGreen() {
  setMode(green, true, false, 500);
  yellow.active = false;
  red.active = false;
}
