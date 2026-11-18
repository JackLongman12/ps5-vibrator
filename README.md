# DualSense Studio

[![CI](https://github.com/JackLongman12/ps5-vibrator/actions/workflows/ci.yml/badge.svg)](https://github.com/JackLongman12/ps5-vibrator/actions/workflows/ci.yml)

A Windows app (with an [Android version](#android)) for controlling the PS5 DualSense controller's vibration, adaptive triggers and lights. Play looping vibration presets, record your own patterns by playing the triggers like an instrument, edit them on a timeline, and drive the controller live from the touchpad, motion sensors or your PC's audio.

![Presets](docs/presets.png)

## Install

1. Download **`DualSenseStudio-win-Setup.exe`** from the [latest release](https://github.com/JackLongman12/ps5-vibrator/releases/latest).
2. Run it. The build isn't code-signed yet, so Windows SmartScreen may ask you to confirm with **More info → Run anyway**.
3. Connect your DualSense by USB or Bluetooth (pair it in Windows Bluetooth settings: hold **Create + PS** until the light flashes).

The app updates itself. It checks GitHub on start and every 20 minutes, downloads new versions in the background, and shows a **Restart & update** banner.

## Features

| | |
|---|---|
| **Presets** | 18 looping patterns (Heartbeat, Wave, Engine Rev, Thunder, SOS…), with live intensity, speed and left/right balance. |
| **Recorder** | Record a loop from L2/R2. Four mappings decide how presses turn into vibration (Pressure, Dual channel, Velocity accents, Zones). Face buttons add hits. You can use a count-in, overdub, and a fixed (2–60 s) or free take length. You feel the take live while recording. |
| **Editor** | Draw on the waveform. Trim, change the loop length (time-stretch), quantize to a BPM, smooth, adjust gain or normalize, crossfade the loop seam, reverse and swap sides. Undo is available. |
| **Trigger Lab** | Adaptive trigger resistance, slope, click/break and trigger vibration, plus one-click presets. Settings are remembered. |
| **Live Play** | Use the touchpad as an XY pad, tilt and shake (motion), or audio-reactive mode (bass drives the left side, treble the right). |
| **Lights** | The lightbar can follow the vibration, show a static colour, cycle a rainbow or show battery level. The player LEDs can act as an intensity meter. |
| **Safety** | A maximum-intensity cap and an auto-stop timer. Stop everything with **Esc**, **Ctrl+Alt+S** from any app, or the controller's **Mute** button. |
| **Controller shortcuts** | Options plays/stops, D-pad ◀ ▶ changes preset, ▲ ▼ changes intensity, Create records. |

<p>
  <img src="docs/recorder.png" width="49%" alt="Recorder">
  <img src="docs/trigger-lab.png" width="49%" alt="Trigger Lab">
</p>

## Android

`src/DualSenseStudio.Android` is a .NET for Android app built on the same Core library: the same engine, presets, recorder, editor tools and pattern format. Its UI is written in plain Android views, not MAUI, because the app needs direct access to `UsbManager`, the controller's `InputDevice` vibrators and lights, and raw gamepad events. It has tabs for Presets, Recorder, Library, Triggers, Live, Lights and Device, plus a player bar with intensity, speed and balance controls and a Stop button.

### Install the APK

1. Download `io.github.jacklongman12.dualsensestudio-Signed.apk` from the [latest release](https://github.com/JackLongman12/ps5-vibrator/releases/latest) when one includes it, or build it yourself (see [Build from source](#build-from-source)).
2. On the phone, allow installing apps from your browser or file manager, then open the APK. Android 8.0 or newer is required, and Android 12 or newer for Bluetooth rumble and lights.
3. Or install from a PC with `adb install -r io.github.jacklongman12.dualsensestudio-Signed.apk`.

The APK is signed with the .NET debug key. Uninstall it before installing a build signed with a different key.

### Connecting, and what works

| | USB (OTG cable) | Bluetooth |
|---|---|---|
| Rumble (presets, recorder, live) | ✅ | ✅ Android 12+ |
| Adaptive triggers (Trigger Lab, trigger vibration) | ✅ | ❌ |
| Lightbar colour | ✅ | ✅ Android 12+ |
| Player LEDs, LED brightness, mute LED | ✅ | ❌ |
| Sticks, buttons, L2/R2 pressure (recorder) | ✅ | ✅ |
| Touchpad live play | ✅ | ❌ |
| Motion live play | ✅ | ⚠️ experimental, only if Android exposes the controller's sensors |
| Audio reactive | ❌ not on Android | ❌ |

- **USB** gives full support. Plug the DualSense in with a USB-C OTG cable or adapter and accept the *Allow access* prompt. The app claims the HID interface and sends the same raw reports as Windows. While the app holds the controller, Android's own gamepad driver can't use it.
- **Bluetooth**: Android does not let apps open Bluetooth HID devices raw. The app falls back to Android's built-in DualSense driver: `InputDevice.VibratorManager` for rumble, `LightsManager` for the lightbar, and key/motion events for input. The app labels the features this doesn't cover. Pair the controller in Android's Bluetooth settings (hold Create + PS until it flashes).
- Patterns are stored in the app's private storage as `*.dspattern.json`, the same format as Windows. Use **Library → Import / Export** to move them between devices.

## Troubleshooting

- **No vibration or triggers while Steam is open:** Steam Input can take over the controller. Turn off *PlayStation Controller Support* in Steam → Settings → Controller, or close Steam. DS4Windows and DSX can conflict in the same way. The app warns you when any of them are running.
- **Vibration is weak or missing:** try the other *Rumble mode* in Settings (Modern v2 / Legacy v1).
- **Your pattern files** are in `%AppData%\DualSenseStudio\patterns`, saved as `*.dspattern.json`. They can be shared and imported.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
dotnet build DualSenseStudio.sln
```

```bash
dotnet test DualSenseStudio.sln
```

```bash
dotnet run --project src/DualSenseStudio.App
```

| Path | |
|---|---|
| `src/DualSenseStudio.Core` | Platform-neutral code: DualSense HID protocol (USB + Bluetooth with CRC32), the 100 Hz haptics engine, patterns, the recorder, and live sources |
| `src/DualSenseStudio.App` | WPF (.NET 10) UI, settings, audio capture, and the Velopack updater |
| `src/DualSenseStudio.Android` | .NET for Android app (`net10.0-android`) built on the same Core |
| `tests/DualSenseStudio.Core.Tests` | Protocol, encoding, pattern-tool, storage-hardening and recorder tests |
| `scripts/release.ps1` | Test → build → package → publish a GitHub release (maintainers) |

### Android app


```bash
dotnet workload install android
```

```powershell
dotnet build src/DualSenseStudio.Android -t:InstallAndroidDependencies -f net10.0-android "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk" -p:AcceptAndroidSDKLicenses=True
```

```bash
dotnet publish src/DualSenseStudio.Android -f net10.0-android -c Release -o artifacts/android
```

The second command downloads the Android SDK and a JDK into `%LOCALAPPDATA%\Android`. The project finds that JDK automatically. Building `DualSenseStudio.sln` includes the Android project, so it needs the `android` workload. Without it, build `src/DualSenseStudio.App` directly.


To release (maintainers only, needs `gh auth login`):

```powershell
.\scripts\release.ps1 -Notes "What changed"
```

## Security

See [SECURITY.md](SECURITY.md) for how updates, tokens and imported files are handled, and how to report a problem.

## Credits

The DualSense report layouts follow the Linux `hid-playstation` driver. The adaptive-trigger effect encodings follow community documentation of the DualSense trigger format. This project isn't affiliated with or endorsed by Sony Interactive Entertainment. "PlayStation" and "DualSense" are trademarks of their respective owners.

## License

[MIT](LICENSE)
