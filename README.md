# 📸 Screenshot Director

[![Download v1.0.0](https://img.shields.io/badge/Download-Screenshot%20Director%20v1.0.0-7C3AED?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/diaba1445-glitch/Screenshot-Director/releases/download/v1.0.0/ScreenshotDirector-v1.0.0-win-x64.zip)
[![Release](https://img.shields.io/github/v/release/diaba1445-glitch/Screenshot-Director?style=for-the-badge&color=8B5CF6)](https://github.com/diaba1445-glitch/Screenshot-Director/releases/latest)
[![Tests](https://img.shields.io/badge/Tests-51%20Passing-10B981?style=for-the-badge)](https://github.com/diaba1445-glitch/Screenshot-Director)
[![License](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)](LICENSE)

**Screenshot Director** is a lightweight, native Windows desktop utility that streamlines and automates screenshot management. Instead of letting captures clutter your clipboard or generic `Pictures\Screenshots` folder, Screenshot Director intercepts screenshots and displays a compact, instant destination routing popup right near your cursor or screen anchor.

With a single keypress (`1`–`9`) or click, your screenshot is encoded as a lossless PNG, saved to your desired folder, and its absolute file path is placed onto the clipboard ready to paste into Slack, Discord, email, or your browser.

---

## ⚡ Quick Download & Run (Windows 10 / 11)

1. **[Download ScreenshotDirector-v1.0.0-win-x64.zip](https://github.com/diaba1445-glitch/Screenshot-Director/releases/download/v1.0.0/ScreenshotDirector-v1.0.0-win-x64.zip)** (or download `ScreenshotDirector.exe` directly from the [Latest Release](https://github.com/diaba1445-glitch/Screenshot-Director/releases/latest)).
2. Extract and run **`ScreenshotDirector.exe`**.
3. **No installation required**: It runs as a self-contained single file with the .NET 8 runtime bundled in.

---

## 🚀 Key Features

- **Modern Control Center (Sidebar Navigation)**:
  - Clean, dark violet theme matching modern productivity tools.
  - **Home**: Live listening status, quick popup test, and recent captures feed with direct `[ 📄 Open file ]` and `[ 📂 Open folder ]` actions.
  - **Presets**: Map destination folders to keys `1`–`9` with reordering, templates (`+ Screenshots`, `+ Desktop`, `+ Downloads`), and custom browse.
  - **Popup & Routing**: Configure screen anchor positions (*BottomRight*, *TopRight*, *BottomCenter*, *CursorAdjacent*), auto-dismiss timeout, and Session Default folder.
  - **General**: Per-user Windows startup toggle (`HKCU` Run key, no admin rights required), system tray minimization, and single-instance mutex enforcement.
  - **About**: Shortcut cheat sheet and release information.

- **Instant Routing Popup**:
  - Automatically triggered upon taking a screenshot with `Win + Shift + S`, Windows Snipping Tool, or `PrtScn`.
  - Keys `1`–`9` instantly save to preset destinations.
  - Key `S` opens **Select Custom Path** to route to any unlisted folder with an option to add it to presets.
  - Key `R` focuses the **Quick Rename** box and automatically pauses the dismiss timer.
  - **Session Default Folder**: Option to remember choice so future screenshots auto-route with a quick 3-second collapsed toast without interrupting your flow. Press `E` on the toast to expand for exceptions, or click `Clear Default`.

- **Lossless PNG Engine**:
  - Thread-safe, non-blocking asynchronous PNG encoding.
  - Collision avoidance with automatic numbered suffixes (`Screenshot_2026-10-25_15-30-00 (1).png`).
  - Path traversal and invalid character sanitation.
  - Self-clipboard suppression to prevent infinite feedback loops.

---

## ⌨️ Keyboard Shortcuts Reference

| Shortcut | Action in Routing Popup |
| :--- | :--- |
| **`1` – `9`** | Instant save to corresponding preset destination folder |
| **`S`** | Select custom destination folder |
| **`R`** | Focus rename field and pause auto-dismiss timer |
| **`E`** | Expand session default collapsed toast to full tile grid |
| **`Enter`** | Confirm custom path or rename |
| **`Escape`** | Dismiss popup without saving |

---

## 🛠 Technology Stack

- **Platform**: Windows 10 / Windows 11 (x64)
- **Framework**: .NET 8 (WPF + Windows Forms NotifyIcon)
- **Architecture**: Model-View-ViewModel (MVVM)
- **Native APIs**: Win32 User32 P/Invoke (`AddClipboardFormatListener`, `GetCursorPos`, `MonitorFromPoint`, `GetMonitorInfo`)
- **Distribution**: Portable self-contained single-file executable (`publish\win-x64\ScreenshotDirector.exe`)

---

## 🧪 Building from Source

```powershell
# Clone the repository
git clone https://github.com/diaba1445-glitch/Screenshot-Director.git
cd Screenshot-Director

# Run automated tests (51 unit & integration tests)
dotnet test .\ScreenshotDirector.sln

# Build and publish single-file release executable
dotnet publish .\src\ScreenshotDirector\ScreenshotDirector.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x64
```

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
