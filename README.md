# AccentHold

A macOS-style press-and-hold accent picker for Windows.

Hold a letter, and a small menu of accented variants pops up next to your text
cursor. Pick one with a number key, the arrows, or the mouse. Windows never
shipped this the way macOS does — AccentHold adds it, and everything is easy to
make your own: the menu size, the trigger delay and every single mapping live
in a plain config file.

![AccentHold in action](docs/screenshot.png)

## Features

- **macOS mapping** — the exact press-and-hold tables (e.g. `e` → è é ê ë ě ẽ ē ė ę), plus
  iOS-style sets for digits and punctuation (`$` → € £ ¥…, `?` → ¿, `-` → – — •).
- **Fully customizable** — every mapping is a plain line in `config.ini`: reorder the
  variants, add your own characters or symbols, change the menu size and delay; changes
  apply the instant you save.
- **Layout-aware** — the key is resolved from the active keyboard layout, so AZERTY, QWERTY, QWERTZ… all work.
- **Smart placement** — the menu opens above the caret so it never hides what you type, and stays on screen at the edges.
- **Stays out of the way** — it only appears when there is a real text caret, and never steals focus from the app you are typing in.
- **Translucent Windows 11 look** — a clean, rounded, semi-transparent flyout that follows your light/dark theme and accent color.
- **Runs in the background** — a tray icon, optional start-with-Windows, tiny footprint.

## Install

Download the latest `AccentHold-Setup.exe` from
[Releases](https://github.com/AdrienMasanet/AccentHold/releases) and run it.

The installer registers AccentHold in **Installed apps** (so you can remove it
like any program) and offers two options:

- **Start automatically when I sign in** — on by default. Without the option below it
  registers a plain Run entry, visible and toggleable in Task Manager's **Startup apps**.
- **Run with administrator privileges** — on by default, so accents also work in apps
  that themselves run elevated. This variant starts through a scheduled task (see Task
  Scheduler) because Windows cannot elevate regular startup entries.

Nothing else to do: the app starts immediately and lives in the system tray.

## Usage

1. Hold an accentable letter (e.g. `e`). It types once, then the menu opens.
2. Press the number under the variant you want, or move with `←`/`→` and press `Enter`; `Esc` cancels.
3. Any other key closes the menu and types normally — just like macOS.

## Configuration

Right-click the tray icon → **Settings…** to open `config.ini`
(`%APPDATA%\AccentHold\config.ini`). Changes apply the moment you save;
**Reset settings…** in the same menu restores the defaults.

```ini
[general]
hold_delay_ms = 180   ; delay before the menu appears (50-2000)
scale         = 1.0   ; menu size multiplier (0.7-2.5)

[accents]
; The full accent table, entirely yours to edit. One base character per line,
; variants separated by spaces, shown in that order. Uppercase menus are derived
; automatically. Any key that types a character works — letters, digits, punctuation.
e = è é ê ë ē ė ę
o = ô ö ò ó œ ø ō õ
; …one line per letter, plus ready-made commented sets like: ? = ¿  or  - = – — •
```

## How it works (and why it's safe)

AccentHold watches for a held key with a standard Windows keyboard hook, purely
to detect the press-and-hold gesture. **It does not record, store, or send
anything.** No file of keystrokes, no network access — the hook lives entirely in
this process and only ever asks "is an accentable key being held?". Everything is
in this repository, MIT-licensed, so you can read exactly what it does. The
optional administrator mode exists only so the accent menu can reach apps that
run elevated; it grants AccentHold no other special behavior.

## Limitations

- Apps running as administrator only receive accents if AccentHold was installed with the
  **administrator privileges** option enabled.
- Windows keeps its own shell surfaces (Start menu, Search) above every application
  window, so over those the menu appears just outside the panel instead of at the caret.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run  --project src\AccentHold                 # run it
dotnet run  --project src\AccentHold -- --demo       # show the menu for a quick visual check
powershell -File scripts\build-installer.ps1         # build the installer (needs Inno Setup 6)
```

## License

[MIT](LICENSE).
