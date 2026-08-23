<p align="center">
  <img src="Assets/logo.png" width="120" alt="Spigen Audio CTRL logo"/>
</p>

<h1 align="center">Spigen Audio CTRL</h1>

<p align="center">
  A high-performance, 100% native Windows companion app for the <strong>Spigen SA-HP P10</strong> over-ear headphones.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?logo=windows&logoColor=white" alt="Windows"/>
  <img src="https://img.shields.io/badge/Protocol-BLE%20GATT-00B4AB" alt="BLE GATT"/>
  <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT License"/>
</p>

---

Built with pure **C# / WPF / XAML (.NET 8)** and direct **WinRT Bluetooth LE GATT** — no Electron, no framework overhead. Instant startup (<20ms), ultra-low memory (~25MB), and a clean Scandinavian design aesthetic.

## ⚡ Features

### 〰️ Noise Control (ANC Modes)
- Deep Noise Cancellation
- Adaptive ANC
- Commuting / Transit
- Anti-Wind Cancellation
- Transparency / Ambient Mode
- Off (Passive Isolation)
- Low-Latency Gaming Mode Toggle

### 🎚️ 10-Band Hardware DSP Equalizer
- Real-time cubic Bézier response curve with 10 interactive drag handles
- Precision hardware faders: 60Hz, 220Hz, 500Hz, 1kHz, 2kHz, 2.5kHz, 5kHz, 7.5kHz, 12kHz, 16kHz
- Harman Audiophile Target reference ghost curve overlay
- Factory presets: *Harman Audiophile Target, Bass Boost, Pop, Rock, Classical, Vocal Enhance, Gaming Footstep & Spatial*
- Custom preset saving with auto-numbered slots

### 🔘 Button Remapping
- Remap Multi-Function Button (Single, Double, Triple Click)
- Remap Volume Up / Down (Single Click, Long Press)
- Actions: Play/Pause, Next/Prev Track, Volume, Voice Assistant, Gaming Mode, ANC Switch
- Instant live sync to onboard headphone memory via RCSP

### ✨ Native Windows Experience
- Windows 11 DWM dark mode title bar & rounded geometry
- Custom Scandinavian-style scrollbars and controls
- Bluetooth auto-reconnect with BLE advertisement scanning
- Live battery level display from both BLE Battery Service and RCSP TLV

---

## 🛠️ Build & Run

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Quick Build (Standalone `.exe`)

```cmd
build.bat
```

Or via the .NET CLI directly:

```powershell
dotnet publish SpigenAudioCTRL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/
```

The standalone executable `dist\SpigenAudioCTRL.exe` (~78MB, no runtime dependencies required) will be generated.

### Run from Source

```powershell
dotnet run
```

---

## 🏗️ Architecture

| File | Responsibility |
|------|---------------|
| `BluetoothService.cs` | WinRT BLE GATT connection, scanning, GATT session, notify/write characteristics |
| `RcspProtocol.cs` | RCSP packet encoding (ANC, EQ, Gaming Mode, Key Mapping, Hardware Query) |
| `MainWindow.xaml.cs` | UI logic: EQ engine, canvas drag-draw, button remapping grid, debounce timer |
| `App.xaml.cs` | Global unhandled exception handlers |

---

## 📡 Protocol Notes

The Spigen SA-HP P10 uses a proprietary **RCSP (Real-time Control Serial Protocol)** over BLE GATT with a custom vendor service. Key packet structure:

```
FE DC BA [flags] [opcode] [len_hi] [len_lo] [sn] [payload...] EF
```

- **Write Characteristic**: matches UUID containing `ae01` or `0001` (Write / WriteWithoutResponse)
- **Notify Characteristic**: matches UUID containing `ae02` or `0002`
- **Battery**: Standard BLE Battery Service (UUID `180F`, Characteristic `2A19`)

---

## ⚠️ Disclaimer

This project is an independent, unofficial third-party application. It is not affiliated with, endorsed by, or connected to Spigen Global Co., Ltd. in any way. Use at your own risk.

---

## 📄 License

[MIT](LICENSE) — © 2026 safan
