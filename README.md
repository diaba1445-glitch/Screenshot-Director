# 📸 Screenshot Director

**Screenshot Director** is a lightweight, native Windows desktop utility that streamlines and automates screenshot management. Instead of letting captures clutter your clipboard or generic `Pictures\Screenshots` folder, Screenshot Director intercepts screenshots and displays a compact, instant destination routing popup right near your cursor.

With a single keypress (`1`–`9`) or click, your screenshot is encoded as a lossless PNG, saved to your desired folder, and its absolute file path is placed onto the clipboard ready to paste into Slack, Discord, email, or your browser.

---

## 🚀 Key Features

- **Native Windows Clipboard Listener**: Listens for native `WM_CLIPBOARDUPDATE` messages via `AddClipboardFormatListener` / `RemoveClipboardFormatListener`.
- **Preserves Native Muscle Memory**: Works seamlessly with `Win + Shift + S`, Windows Snipping Tool, `PrtScn`, and standard capture utilities.
- **Instant Keyboard Routing**: Destination folders are mapped to number keys `1`–`9`. Pressing `1` saves to destination 1, `2` to destination 2, etc.
- **Smart 7-Second Timeout**: The popup automatically fades after 7 seconds of inactivity without clearing or disturbing the Windows clipboard. Any user interaction (typing, mouse movement, or pressing a shortcut) immediately resets the countdown.
- **Screenshot Provenance Classification**:
  - *Conservative Mode (Default)*: Intelligently verifies capture markers and suppresses browser "Copy image" (`text/html`, `HTML Format`, URL) and Office embedding markers (`Embed Source`, RTF).
  - *Generic Image Fallback Mode*: Can be toggled in Settings to route any image copied to the clipboard.
- **Robust PNG Engine**:
  - Thread-safe, non-blocking asynchronous PNG encoding.
  - Safe atomic commit semantics (`FileMode.CreateNew` with temporary staging) to prevent filename race conditions.
  - Collision avoidance with automatic numbered suffixes: `Screenshot_2026-10-25_15-30-00.png`, `Screenshot_2026-10-25_15-30-00 (1).png`, etc.
  - Path traversal and invalid character sanitation.
- **Self-Clipboard Suppression**: Internal clipboard writes (such as copying the saved file path) are suppressed to prevent infinite popup trigger loops.
- **System Tray & Control Center**:
  - Launches into a full Control Center dashboard on manual launch to manage presets, permissions, and app status.
  - Minimizes to the Windows system tray when closed.
  - Right-click tray menu: *Open Control Center*, *Pause/Resume Detection*, *Manage Presets*, *Stop Program & Exit*.
  - User-driven preset configuration with template shortcuts (`Pictures\Screenshots`, `Desktop`, `Downloads`, or any custom folder) mapped to keys `1`–`9`.
  - Live pause/resume detection and program stop controls.
  - Per-user Windows startup toggle (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) requiring no administrative privileges.
- **Single-Instance Enforcement**: Named Mutex lock ensures only one active background instance runs; launching a second instance signals the primary instance to activate the Control Center.

---

## 🛠 Technology Stack

- **Platform**: Windows 10 / Windows 11 (x64)
- **Framework**: .NET 8 (WPF + Windows Forms NotifyIcon)
- **Architecture**: Model-View-ViewModel (MVVM)
- **Native APIs**: Win32 User32 P/Invoke (`AddClipboardFormatListener`, `GetCursorPos`, `MonitorFromPoint`, `GetMonitorInfo`)
- **Distribution**: Self-contained, single-file release executable (`publish\win-x64\ScreenshotDirector.exe`)

---

## 📂 Project Structure

```
ScreenshotDirector/
├── ScreenshotDirector.sln
├── src/
│   └── ScreenshotDirector/
│       ├── Core/
│       │   ├── Interfaces/        # IClipboardListener, IImageSaveService, etc.
│       │   └── Models/            # AppSettings, DestinationFolder, CaptureCandidate
│       ├── Infrastructure/        # AppLogger (bounded, rotated, PII-free)
│       ├── Platform/              # NativeMethods, SingleInstanceManager, StartupManager, TrayIcon
│       ├── Services/              # CaptureDetectionService, ImageCaptureService, ImageSaveService, SettingsService
│       ├── ViewModels/            # CapturePopupViewModel, MainControlViewModel, SettingsViewModel
│       └── Views/                 # MainWindow.xaml, CapturePopupView.xaml, SettingsWindow.xaml
└── tests/
    ├── ScreenshotDirector.UnitTests/         # 40 unit tests (Save, Settings, Detection, ControlCenter, Popups)
    └── ScreenshotDirector.IntegrationTests/  # 3 integration tests (End-to-end, 4K stress, Leaks)
```

---

## ⚙ Configuration & Storage

Settings and logs are stored strictly within the user's local application data profile:

- **Settings**: `%LOCALAPPDATA%\ScreenshotDirector\settings.json`
- **Corrupt Backup**: `%LOCALAPPDATA%\ScreenshotDirector\settings.json.corrupt.bak`
- **Application Logs**: `%LOCALAPPDATA%\ScreenshotDirector\Logs\screenshotdirector_{yyyy-MM-dd}.log`

*Note: Logs are rotated (max 7 files, 10MB limit) and never record image pixels, personal data, or clipboard content.*

---

## 🧪 Testing & Verification

Run the test suite using .NET 8:

```powershell
dotnet test .\ScreenshotDirector.sln
```

### Test Coverage Summary:
- **Unit Tests (40 passed)**:
  - Filename generation and invariant timestamp formatting
  - Path traversal rejection (`..`, `/`, `\`, illegal characters)
  - Filename collision resolution (`(1)`, `(2)`)
  - Atomic PNG creation and dimension preservation
  - Settings loading, saving, corruption recovery, and schema migration
  - Popup ViewModel keyboard routing (`1`–`9`), auto-dismiss timer, and dismiss behavior
  - Single instance mutex acquisition and second instance signaling
  - Startup registration query and toggling
- **Integration Tests (3 passed)**:
  - End-to-end capture save and collision avoidance workflow
  - Large 4K screenshot encoding and dimension integrity (3840x2160)
  - Repeated save loop testing for resource leak prevention

---

## 📦 Building the Release Executable

To publish the self-contained single-file executable:

```powershell
dotnet publish .\src\ScreenshotDirector\ScreenshotDirector.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

The output executable is generated at:
`publish\win-x64\ScreenshotDirector.exe`

---

## 📝 Provenance Detection & Known Limitations

1. **Clipboard Image Provenance**:
   - The Windows clipboard API does not supply a dedicated "origin" tag explicitly certifying that a bitmap originated from a screen snip.
   - Screenshot Director uses an evidence-based filter: Windows Snipping Tool and Shell screen snips register raw device-independent bitmaps (`CF_DIB`, `CF_DIBV5`) alongside Shell clipboard flags (such as `CanIncludeInClipboardHistory`), while excluding browser DOM formatting (`HTML Format`, `text/html`, `SourceURL`) and Office embedding tags (`Embed Source`).
   - For environments or third-party screenshot utilities that do not register shell formats, users can enable **Generic Image Fallback** in Settings to allow all copied bitmaps to trigger routing.
