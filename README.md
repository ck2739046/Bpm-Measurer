<div align="center">

<h1>
  <img src="icon/icon.svg" width="110" alt="logo">
  <br>BPM Measurer
</h1>

<h3>🎵 Viusal audio BPM measuring tool 🎵</h3>

<br>

A tool for measuring **BPM timing** of audio, with waveform/spectrogram visualization and a metronome.

<br>

![](https://img.shields.io/github/stars/ck2739046/Bpm-Measurer?label=Stars)
![](https://img.shields.io/github/downloads/ck2739046/Bpm-Measurer/total?label=Downloads)

🔗 [**GitHub Repo**](https://github.com/ck2739046/Bpm-Measurer)
&nbsp;•&nbsp;
📥︎ [**Download Release**](https://github.com/ck2739046/Bpm-Measurer/releases/latest)
&nbsp;•&nbsp;
▶️ [**Demo Video**](https://www.bilibili.com/video/BV1fD786hE3M)

</div>

> Run into issues, need a hand, want to report bugs, share suggestions, or talk development? Join our QQ group chat **`868888361`**.

<br>

## ✨ Highlights

- **Dual visualization**
    - Waveform and spectrogram views side by side, with synchronized zoom and pan for precise inspection.

- **BPM timing editor**
    - Add, remove, and modify timing points with full **undo/redo** support.
    - Import and export timing configs in a human-readable text format.

- **Built-in metronome**
    - Verify your timing edits by ear.

- **Multi-language**
    - Supports English and Chinese.

<br>

## Command-line Arguments

```
Bpm Measurer.exe [--language=<lang>] [--audio=<path>] [--notify=<path>] [--parse_config=<path>]
```

| Argument | Description |
|----------|-------------|
| `--audio=<path>` | Path to an audio file to load on startup |
| `--language=<lang>` | UI language:<br>`en-US` — English<br>`zh-CN` — Chinese<br>Defaults to `zh-CN`. |
| `--notify=<path>` | See **HachimiDX Integration** below. |
| `--parse_config=<path>` | See **HachimiDX Integration** below. |

## HachimiDX Integration

Both modes below are intended for the host **[HachimiDX](https://github.com/ck2739046/HachimiDX)** and write a JSON file to the `--notify=` path, communicating the result through the process exit code.

### Single `--notify=` (interactive embed mode)

```
Bpm Measurer.exe --audio=<song.wav> --notify=<manifest.json>
```

Launches the GUI so the user can edit timing. On a successful **Config Export**, writes a manifest `{ "config_path": ..., "audio_path": ... }` to the notify path and exits `0`. Closing without exporting exits `1`; a write failure exits `2`.

### `--notify=` + `--parse_config=` (headless export)

```
Bpm Measurer.exe --parse_config=<config.txt> --notify=<out.json>
```

Skips the GUI entirely: parses the config file and immediately writes `{ "global_offset": ..., "timing_points": [ { "beat_index": ..., "bpm": ..., "beats_per_bar": ... } ] }` to the notify path. If `--notify=` is missing, `--parse_config=` is silently ignored and the GUI starts normally.

Exit codes: `0` = parsed and written successfully · `1` = config read/parse failed · `2` = notify write failed · `3` = other unexpected error.

