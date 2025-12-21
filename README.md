# Random Discord Hintergrund — README
Dies ist mein Discord. Bei Problemen meldet euch dort -> https://discord.gg/jw9B5BP7
Deutsch
-------

Kurzbeschreibung

Dieses kleine Tool wählt zufällig ein Hintergrundbild aus einem Quellordner aus, kopiert es in den Vencord-Theme-Ordner, aktualisiert die `Translucence.theme.css`-Datei (Variable `--app-bg`) und passt die Accent-Farben anhand einer ermittelten "vibranten" Farbe automatisch an.

Installation & Start

1. Entpacke das Programm aus der .zip-Datei und und starte die `exe`
2. Beim ersten Start wirst du interaktiv nach folgenden Angaben gefragt:
   - Pfad zum Ordner mit Hintergrundbildern (`SourceDirectory`)
   - Pfad zum Vencord-Ordner (`VencordDirectory`) — typischerweise `%APPDATA%\\Vencord`
   - Sprache (Deutsch/Englisch)
   - Ob das Programm beim Windows-Start automatisch ausgeführt werden soll (Autostart)
3. Diese Angaben werden in `config.json` gespeichert (neben der EXE). Du kannst die Datei später manuell anpassen.

Wichtige Dateien/Ordner

- `config.json` — enthält Einstellungen (`SourceDirectory`, `VencordDirectory`, `AutoRun`, ...).
- `lang/` — enthält die Sprachdateien (`de.json`, `en.json`).
- `themes/Translucence.theme.css` — die Theme-Datei in deinem Vencord-Ordner, wird ggf. heruntergeladen und angepasst.
- Zielordner: `...\Vencord\themes\Hintergrundbild` — dort wird das ausgewählte Bild abgelegt.
- Logs: `logs/run_YYYYMMDD_HHMMSS.log` neben der EXE.

Autostart

Wenn du Autostart akzeptierst, wird ein Eintrag unter `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` erstellt. Entferne den Eintrag mit dem Registrierungseditor oder per Kommandozeile, z.B.:

```
reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v RandomDiscordHintergrund /f
```

Hinweise

- Sprachdateien müssen in UTF-8 gespeichert sein. Das Projekt liefert `lang/de.json` und `lang/en.json` im UTF-8-Format.
- Wenn Umlaute nicht richtig angezeigt werden, benutze PowerShell oder Windows Terminal und eine konsolenkompatible Schriftart (z. B. Consolas).
- Wenn das Programm beim ersten Start die Konfiguration speichert, pausiert das Fenster damit du die Meldungen sehen kannst.

Fehlerbehebung

- Falls das Tool das Theme nicht findet, wird versucht, die `Translucence.theme.css` automatisch von GitHub herunterzuladen.
- Logs geben Hinweise auf Fehler — siehe `logs/`.


Lizenz / Hinweise
- Dies ist ein Hilfsprogramm. Erstelle immer Backups deiner Theme-Dateien.
- Du kannst den Quellcode für den persönlichen Gebrauch frei anpassen. Eine Weiterverbreitung oder Reupload ist ohne Erlaubnis nicht gestattet.

English
-------
This is my Discord, chat with me over there if you have any issues -> https://discord.gg/jw9B5BP7
Short description

This small tool selects a random wallpaper from a source folder, copies it into the Vencord theme folder, updates `Translucence.theme.css` (the `--app-bg` variable) and adjusts accent colors automatically based on a detected "vibrant" color.

Installation & usage

1. Extract the .zip-file and launch the `exe`.
2. On first run the application asks interactively for:
   - Path to the folder with wallpapers (`SourceDirectory`)
   - Path to your Vencord folder (`VencordDirectory`) — commonly `%APPDATA%\\Vencord`
   - Language (German/English)
   - Whether to enable autorun at Windows startup
3. These settings are saved in `config.json` next to the executable. You can edit that file manually later.

Important files/folders

- `config.json` — stores settings (`SourceDirectory`, `VencordDirectory`, `AutoRun`, ...).
- `lang/` — language files (`de.json`, `en.json`).
- `themes/Translucence.theme.css` — theme file in your Vencord folder; the tool will download and edit it if missing.
- Destination folder: `...\\Vencord\\themes\\Hintergrundbild` — the chosen image is copied here.
- Logs: `logs/run_YYYYMMDD_HHMMSS.log` next to the EXE.

Autorun

If you enable autorun, the tool creates a registry entry under `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`. Remove it with regedit or the command line:

```
reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v RandomDiscordHintergrund /f
```

Notes

- Language files should be saved in UTF-8. The project includes `lang/de.json` and `lang/en.json` encoded as UTF-8.
- If umlauts show incorrectly, run the EXE in PowerShell or Windows Terminal and use a console font that supports Unicode (e.g., Consolas).
- On first successful save the program pauses so you can read the messages.

Troubleshooting

- If the theme file is not found the tool attempts to download `Translucence.theme.css` from GitHub.
- Check `logs/` for details if something fails.

License / Notes

- This is a helper utility. Always keep backups of your theme files.
- Modify the source freely for personal use. Reupload or redistribution is not allowed without permission.

