English | [한국어](README.ko.md)

# MouseSwitch

Use a single mouse (plus the keyboard, if you like) to switch back and forth between a laptop and a desktop.
Press a side button on the mouse once and control moves to the other PC.

- It only works on the same Wi-Fi / LAN (behind the same router). It does not connect over the internet.
- The first connection is authenticated with a 6-digit pairing code. After that, the two PCs reconnect automatically over an encrypted connection.
- It can be set to start automatically when Windows starts.
- Both PCs are assumed to run Windows 10/11 (64-bit).

> Note: the app's on-screen labels are in Korean. This guide gives the Korean label in parentheses next to its English meaning so you can find each option.

---

## 1. Installation (on both PCs)

There is no installer. Just copy the whole folder.

1. Copy the `MouseSwitch` folder to both PCs. (Example: `C:\MouseSwitch`)
2. On each PC, double-click `build.bat` → this creates `MouseSwitch.exe`.
   - It uses the compiler that already ships with Windows, so there is nothing extra to install.
   - If an error occurs, copy the message shown in the window as-is and report it.
3. Right-click `설치_방화벽+자동시작.bat` (install firewall rules + autostart) → Run as administrator.
   - It opens the firewall ports (24800/24801) and registers the app to start automatically at login with administrator rights.
   - Administrator rights are needed for the app to work properly over game windows as well. If you skip this step, button/key input may not work in some programs.

---

## 2. Connecting (one time only)

### The PC being controlled (e.g. the laptop)
1. Run `MouseSwitch.exe` → the settings window opens.
2. Select "PC that receives control" (`조종을 받는 PC`) → [Save] (`저장`)
3. Click [Create pairing code] (`페어링 코드 만들기`) → a large 6-digit number appears on the screen. (Valid for 3 minutes.)

### The PC the mouse is plugged into (e.g. the desktop)
1. Run `MouseSwitch.exe` → settings window.
2. Select "PC with the mouse and keyboard plugged in" (`마우스·키보드가 꽂혀 있는 PC`).
3. Click [Search] (`검색`) → laptops on the same network appear in the list. Select one and its IP is filled in automatically.
   - If it does not show up, enter the laptop's IP manually. (On the laptop: `Win+R` → `cmd` → `ipconfig`)
4. Switch button: choose either the forward or the back side button of the mouse, whichever you prefer. (Wheel click or a hotkey only also works.)
5. Switch the keyboard too (`키보드도 함께 넘기기`): when checked, the mouse and keyboard switch together. When unchecked, only the mouse switches and the keyboard stays on the desktop.
6. [Save] (`저장`) → [Start pairing] (`페어링 시작`) → enter the 6-digit code shown on the laptop.
7. When "Pairing complete" (`페어링 완료`) appears, you are done.

From then on, both PCs reconnect automatically whenever they are turned on.

---

## 3. Usage

- Press the mouse side button once → control moves to the other PC. Press it again to switch back.
- Ctrl + Alt + S → does the same thing (an emergency hotkey for when the mouse button does not work).
- Ctrl + Alt + K → turns keyboard switching on/off. You hear two beeps (high = on, low = off).
  You can also change it from the tray icon's right-click menu or the checkbox in the settings window. It takes effect and is saved immediately.
- A short sound plays when you switch. (It can be turned off in the settings.)
- Tray icon colors: gray = not connected / green = connected (controlling this PC) / orange = controlling the other PC
- Hover over the tray icon to see the current status and the latency (ms).

### Safety features
- If the connection drops, control automatically returns to the original PC. You will never end up without a mouse.
- If the connection drops while a key is held down on the PC that was being controlled, all keys are released automatically. (This prevents the "character keeps walking" problem in games.)
