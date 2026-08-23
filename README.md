# SpigenAudioCTRL

An unofficial Windows desktop companion app for the **Spigen SA-HP P10** over-ear headphones. Built natively with C#, WPF, and WinRT Bluetooth LE GATT — no Electron, no middleware.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?logo=windows&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green)

---

## Features

**Noise Control**
- ANC modes: Deep, Adaptive, Commuting, Anti-Wind, Transparency, Off
- Low-latency Gaming Mode toggle

**Equalizer**
- 10-band hardware DSP EQ via RCSP protocol
- Interactive Bézier frequency response curve
- Bands: 60Hz, 220Hz, 500Hz, 1kHz, 2kHz, 2.5kHz, 5kHz, 7.5kHz, 12kHz, 16kHz
- Harman Audiophile Target reference overlay
- Built-in presets (Flat, Bass Boost, Pop, Rock, Classical, Vocal, Gaming)
- Custom preset slots

**Button Remapping**
- Remap Multi-Function Button: Single, Double, Triple Click
- Remap Volume keys: Single Click, Long Press
- Available actions: Play/Pause, Next/Prev Track, Volume, Voice Assistant, Gaming Mode, ANC Cycle
- Changes are written directly to onboard headphone memory

**Connectivity**
- BLE advertisement scanning — no hardcoded device address required
- Automatic reconnect on disconnect
- Battery level from both BLE Battery Service (0x180F) and RCSP TLV

---

## Requirements

- Windows 10 (Build 19041 or later) or Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Build

**Standalone executable:**
```cmd
build.bat
```

Or manually:
```powershell
dotnet publish SpigenAudioCTRL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/
```

Output: `dist\SpigenAudioCTRL.exe` (~78 MB, self-contained, no runtime installation required)

**Run from source:**
```powershell
dotnet run
```

---

## Architecture

| File | Role |
|------|------|
| `BluetoothService.cs` | BLE GATT connection, scanning, characteristic management |
| `RcspProtocol.cs` | RCSP packet encoding (ANC, EQ, Gaming Mode, Key Mapping, hardware queries) |
| `MainWindow.xaml.cs` | UI state, EQ canvas rendering, button remapping grid |
| `App.xaml.cs` | Global unhandled exception handlers |

---

## Protocol

The SA-HP P10 communicates over BLE GATT using a proprietary RCSP framing:

```
FE DC BA [flags] [opcode] [len_hi] [len_lo] [sn] [payload...] EF
```

- **Write characteristic**: UUID contains `ae01` or `0001`
- **Notify characteristic**: UUID contains `ae02` or `0002`
- **Battery**: Standard BLE Battery Service — UUID `0x180F`, Characteristic `0x2A19`

---

## Disclaimer

This is an independent, unofficial project. Not affiliated with or endorsed by Spigen Global Co., Ltd.

---

## License

[MIT](LICENSE) — Copyright (c) 2026 Safan Sulfikar
