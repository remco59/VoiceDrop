# VoiceDrop

Local push-to-talk dictation for Windows, inspired by FluidVoice (macOS).

Hold **Right Ctrl**, speak, release: the text is typed into whatever app has focus.

- Whisper `large-v3-turbo` (q5_0) via whisper.cpp, GPU-accelerated through Vulkan (works on AMD, NVIDIA, Intel)
- Automatic language detection (Dutch, English and more)
- Fully offline after the first-run model download (~600 MB, stored in `%LOCALAPPDATA%\VoiceDrop\models`)

## Run

```
dotnet run --project src/VoiceDrop
```

Needs the .NET SDK. The app lives in the system tray; right-click the icon to quit.

## Roadmap
- Overlay with live status, configurable hotkey, settings window
- Optional AI cleanup of the transcript (punctuation, filler words)
