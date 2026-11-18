# DualSense Studio

A Windows app for driving the PS5 DualSense controller's vibration, adaptive triggers and lights.

- **Presets**: 18 looping patterns with live intensity, speed and balance controls.
- **Recorder**: play the triggers like an instrument and record a loop. There are four mappings (Pressure, Dual channel, Velocity accents, Zones), plus face-button hits, a count-in, overdub, and a fixed or free take length.
- **Editor**: draw on the waveform, trim, time-stretch to a new loop length, quantize to BPM, smooth, change gain or normalize, crossfade the loop seam, reverse, and swap sides. Undo is available.
- **Trigger Lab**: adaptive trigger resistance, slope, click/break and vibration effects.
- **Live Play**: control vibration with the touchpad as an XY pad, with motion (tilt and shake), or with audio playing on the PC.
- **Lights**: the lightbar can follow the vibration, show a static colour, cycle a rainbow or show battery level. The player LEDs can act as an intensity meter.
- **Safety**: a maximum-intensity cap, an auto-stop timer, `Ctrl+Alt+S` from anywhere, and the controller's mute button as a panic stop.
- **Auto-update**: uses Velopack and GitHub Releases.

## Layout

```
src/DualSenseStudio.Core   Platform-neutral: HID protocol, engine (100 Hz), patterns, recorder, live sources
src/DualSenseStudio.App    WPF (.NET 10) UI, settings, audio capture, updater
scripts/release.ps1        Build + package + publish a release
```

## Develop

```bash
dotnet build DualSenseStudio.sln
```

```bash
dotnet run --project src/DualSenseStudio.App
```

Dev builds run normally, but auto-update only works in installed copies.

## Release a test build

```powershell
.\scripts\release.ps1
```

This bumps the patch version in `version.txt`, publishes a self-contained build, and uploads it to GitHub Releases. Installed copies check for updates on start and every 20 minutes, download new versions in the background, and show a **Restart & update** banner.

To install the first time, run `DualSenseStudio-win-Setup.exe` from the latest release.

Because the release repo is private, the app needs a GitHub token to see updates. On a PC with the GitHub CLI logged in (`gh auth login`), it uses that token. Otherwise, paste a read-only token under **Settings → Update source**. The token is stored encrypted with Windows DPAPI.

## Notes

- Steam Input, DS4Windows and DSX can take over the controller. The app warns when they are running.
- Rumble uses the DualSense's compatible-vibration mode, which works over both USB and Bluetooth. The firmware decides between v1 and v2 automatically, and you can override this in Settings.
- Pattern files are stored in `%AppData%\DualSenseStudio\patterns` as `*.dspattern.json`.
