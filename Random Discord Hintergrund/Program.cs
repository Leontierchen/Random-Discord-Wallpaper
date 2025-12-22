using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Random_Discord_Hintergrund
{
    // Hauptprogramm: wählt ein zufälliges Hintergrundbild aus einem Quellordner,
    // kopiert es in den Vencord-Theme-Ordner, aktualisiert die Theme-Datei
    // und passt die Accent-Farben anhand einer ermittelten, "vibranten" Farbe an.
    class Program
    {
        // Zufallsgenerator für die Auswahl des Bildes
        private static readonly Random _rng = new();

        // Logging helpers
        private static StringWriter? _logBuffer;
        private static TextWriter? _originalOut;
        private static TextWriter? _originalErr;

        [SupportedOSPlatform("windows6.1")]
        static void Main(string[] _)
        {
            // Ensure console uses UTF-8 so Umlauts (Ä Ö Ü etc.) are displayed correctly
            // Also switch the Windows console code page to UTF-8 (65001) to avoid � characters
            try
            {
                // Set Windows console code page to UTF-8
                NativeMethods.SetConsoleOutputCP(65001);
                NativeMethods.SetConsoleCP(65001);
            }
            catch { }

            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Setup logging (capture all Console output)
            _originalOut = Console.Out;
            _originalErr = Console.Error;
            _logBuffer = new StringWriter();
            var tee = new TeeTextWriter(_originalOut, _logBuffer);
            Console.SetOut(tee);
            Console.SetError(tee);

            // -------------------- Konfiguration aus Datei (oder interaktiv) --------------------
            var config = LoadConfig();
            if (config == null)
            {
                Console.WriteLine("Keine Konfigurationsdatei gefunden — wechsle in interaktiven Modus.");
                config = new Config();
            }

            // Sprache auswählen, falls noch nicht gesetzt
            if (!config.LanguageSet)
            {
                Console.WriteLine("Choose language / Sprache wählen: [de/en]");
                while (true)
                {
                    Console.Write("Language (de/en): ");
                    var lang = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(lang)) continue;
                    if (lang == "de" || lang == "en")
                    {
                        config.Language = lang;
                        config.LanguageSet = true;
                        try { SaveConfig(config, GetConfigFilePath()); } catch { }
                        break;
                    }
                    Console.WriteLine("Ungültige Eingabe. Bitte 'de' oder 'en' eingeben.");
                }
            }

            // Initialisiere Localization (lädt lang Dateien aus dem 'lang' Ordner)
            Localization.Init(config.Language ?? "en");

            // Prüfe SourceDirectory und frage den Benutzer, falls nicht vorhanden
            if (string.IsNullOrEmpty(config.SourceDirectory) || !Directory.Exists(config.SourceDirectory))
            {
                config.SourceDirectory = AskSourceDirectory(config.SourceDirectory);
                if (string.IsNullOrEmpty(config.SourceDirectory))
                {
                    Console.WriteLine(Localization.T("source.invalid"));
                    WriteLogAndExit(1);
                    return;
                }
            }

            // Versuche Vencord-Pfad automatisch zu ermitteln oder frage den Benutzer
            if (string.IsNullOrEmpty(config.VencordDirectory) || !Directory.Exists(config.VencordDirectory))
            {
                config.VencordDirectory = VencordDirectory(config.VencordDirectory);
                if (string.IsNullOrEmpty(config.VencordDirectory))
                {
                    Console.WriteLine(Localization.T("vencord.invalid"));
                    WriteLogAndExit(1);
                    return;
                }
            }

            // Speichere die (ggf. neuen) Pfade automatisch in die config.json
            try
            {
                var cfgPath = GetConfigFilePath();

                // Falls der Benutzer noch nicht gefragt wurde, frage ob Autostart gewünscht ist
                if (!config.AutoRunSet)
                {
                    Console.WriteLine(Localization.T("autorun.ask"));
                    while (true)
                    {
                        Console.Write(Localization.T("autorun.prompt"));
                        var answer = Console.ReadLine();
                        if (string.IsNullOrWhiteSpace(answer)) continue;
                        answer = answer.Trim().ToLowerInvariant();
                        if (answer == "j" || answer == "y")
                        {
                            config.AutoRun = true;
                            config.AutoRunSet = true;
                            break;
                        }
                        if (answer == "n")
                        {
                            config.AutoRun = false;
                            config.AutoRunSet = true;
                            break;
                        }
                        Console.WriteLine(Localization.T("invalid.input"));
                    }
                }

                SaveConfig(config, cfgPath);
                Console.WriteLine(string.Format(Localization.T("config.saved"), cfgPath));

                // Registrierung für Autostart (sofern gewünscht)
                try
                {
                    EnsureStartupRegistered(config);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format(Localization.T("autorun.warn"), ex.Message));
                }

                // Wenn das Programm zum ersten Mal ausgeführt wird, pausiere die Konsole
                if (!config.HasRunBefore)
                {
                    Console.WriteLine(Localization.T("first.run.pause"));
                    Console.ReadLine();
                    config.HasRunBefore = true;
                    // Aktualisiere die Config mit dem neuen Flag
                    try { SaveConfig(config, cfgPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Localization.T("config.save.error"), ex.Message));
            }

            // Quelle mit möglichen Hintergrundbildern
            string sourcedirectory = config.SourceDirectory;
            // Basisordner von Vencord: der Rest (themes/Hintergrundbild, Theme-Datei) wird abgeleitet
            string vencordBase = config.VencordDirectory;

            // Zielordner in dem Vencord das Hintergrundbild erwartet (wird erstellt, falls nicht vorhanden)
            string destinationdirectory = Path.Combine(vencordBase, "themes", "Hintergrundbild");
            Directory.CreateDirectory(destinationdirectory);

            // Pfad zur Theme-CSS-Datei, die angepasst werden soll (fester Name, innerhalb von themes)
            string themefile = Path.Combine(vencordBase, "themes", "Translucence.theme.css");

            // Wenn die Theme-Datei nicht existiert: versuche sie automatisch von GitHub (raw) herunterzuladen
            if (!File.Exists(themefile))
            {
                Console.WriteLine(Localization.T("theme.download.start"));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(themefile) ?? Path.GetDirectoryName(Path.GetFullPath(themefile))!);
                    using var http = new HttpClient();
                    var url = "https://raw.githubusercontent.com/CapnKitten/Translucence/master/Translucence.theme.css";
                    var content = http.GetStringAsync(url).GetAwaiter().GetResult();
                    File.WriteAllText(themefile, content);
                    Console.WriteLine(Localization.T("theme.downloaded"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format(Localization.T("theme.download.error"), ex.Message));
                    WriteLogAndExit(1);
                    return;
                }
            }

            // Lese Inhalt der Theme-Datei (frühzeitig, damit wir die --app-bg-Zeile parsen können)
            string themecontent;
            try
            {
                themecontent = File.ReadAllText(themefile);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Localization.T("theme.read.error"), ex.Message));
                WriteLogAndExit(1);
                return; // unreachable, aber kompiliert sauber
            }

            // -------------------- Altes Bild ermitteln --------------------
            // Liste der Dateien im Zielordner (kann leer sein beim ersten Start)
            string[] filenameolds = Directory.GetFiles(destinationdirectory, "*");

            string? filenameold = null;
            if (filenameolds.Length > 0)
            {
                // Dateiname (ohne Pfad) des aktuell vorhandenen Bildes
                filenameold = Path.GetFileName(filenameolds[0]);
            }
            else
            {
                // Kein lokales Bild vorhanden -> überprüfe, ob die Theme-Datei bereits auf ein local-vencord URL verweist
                var appBgLine = themecontent.Split('\n').FirstOrDefault(l => l.Contains("--app-bg:"));
                if (appBgLine != null)
                {
                    // Beispiel: --app-bg: url("vencord:///themes/Hintergrundbild/filename.jpg");
                    var match = Regex.Match(appBgLine, "vencord:///themes/Hintergrundbild/(?<name>[^\")]+)");
                    if (match.Success)
                        filenameold = match.Groups["name"].Value;
                }
            }

            Console.WriteLine(string.Format(Localization.T("searching.random"), (filenameold ?? "<keines>")));

            // -------------------- Neues zufälliges Bild wählen --------------------
            string? randomimage = GetRandomFile(sourcedirectory, filenameold);
            if (string.IsNullOrEmpty(randomimage))
            {
                Console.WriteLine(Localization.T("no.new.image"));
                WriteLogAndExit(1);
                return;
            }

            string filename = Path.GetFileName(randomimage);
            Console.WriteLine(string.Format(Localization.T("random.found"), randomimage));

            // Kopieren des neuen Bildes in den Zielordner (überschreibt falls nötig)
            try
            {
                File.Copy(randomimage, Path.Combine(destinationdirectory, filename), true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Localization.T("copy.error"), ex.Message));
                WriteLogAndExit(1);
                return;
            }

            Console.WriteLine(Localization.T("copied.update.theme"));

            // Ersetze ggf. die --app-bg Zeile vollständig mit der lokalen vencord URL
            var lines = themecontent.Split('\n').ToList();
            int idx = lines.FindIndex(l => l.Contains("--app-bg:"));
            string newAppBgLine = $"    --app-bg: url(\"vencord:///themes/Hintergrundbild/{filename}\");";
            if (idx >= 0)
            {
                lines[idx] = newAppBgLine;
                themecontent = string.Join('\n', lines);
            }
            else
            {
                // Falls die Variable nicht existiert, füge sie am Anfang der Datei hinzu
                themecontent = newAppBgLine + '\n' + themecontent;
            }

            // Falls wir vorher einen lokalen Dateinamen hatten, ersetze weiterhin vorkommende Referenzen am besten sicherheitshalber
            if (!string.IsNullOrEmpty(filenameold))
            {
                themecontent = themecontent.Replace(filenameold, filename);
            }

            // Kommentarbeispiel in der CSS-Datei (Referenz):
            //--accent-hue: 60;
            //--accent-saturation: 100 %;
            //--accent-lightness: 99 %;

            // -------------------- Bestimme "vibrante" Farbe des Bildes --------------------
            // Liefert H_S_L als String (z.B. "210.0_0.75_0.45")
            string vibrantColor = ImageColorHelper.GetVibrantColorHSL(randomimage);
            // Stelle sicher, dass Dezimaltrennzeichen ein Punkt ist (für ToString()-Konsistenz)

            var vibrant = vibrantColor.Split('_');

            // S und L sind als Werte 0..1 zurückgegeben -> in Prozent umrechnen
            vibrant[0] = float.Parse(vibrant[0]).ToString("F0").Replace(',', '.');
            vibrant[1] = (float.Parse(vibrant[1]) * 100).ToString("F0").Replace(',', '.');
            vibrant[2] = (float.Parse(vibrant[2]) * 100).ToString("F0").Replace(',', '.');

            Console.WriteLine(string.Format(Localization.T("vibrant.color"), vibrant[0], vibrant[1], vibrant[2]));

            // Finde die Zeilen in der CSS-Datei, die die Accent-Variablen enthalten
            var line1 = themecontent.Split('\n').FirstOrDefault(l => l.Contains("--accent-hue:"));
            var line2 = themecontent.Split('\n').FirstOrDefault(l => l.Contains("--accent-saturation:"));
            var line3 = themecontent.Split('\n').FirstOrDefault(l => l.Contains("--accent-lightness:"));

            Console.WriteLine($"{line1}\n{line2}\n{line3}");

            // Ersetze diese Zeilen, falls vorhanden, mit den neuen Werten
            if (line1 != null)
                themecontent = themecontent.Replace(line1, $"    --accent-hue: {vibrant[0]};");
            if (line2 != null)
                themecontent = themecontent.Replace(line2, $"    --accent-saturation: {vibrant[1]}%;");
            if (line3 != null)
                themecontent = themecontent.Replace(line3, $"    --accent-lightness: {vibrant[2]}%;");

            // Schreibe die geänderte Theme-Datei zurück
            try
            {
                File.WriteAllText(themefile, themecontent);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Localization.T("writing.theme.error"), ex.Message));
                WriteLogAndExit(1);
                return;
            }

            // Versuche das alte Bild zu löschen (Warnung falls nicht möglich) — nur wenn es ursprünglich lokal vorhanden war
            if (filenameolds.Length > 0)
            {
                try
                {
                    File.Delete(filenameolds[0]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format(Localization.T("delete.old.warn"), ex.Message));
                }
            }

            Console.WriteLine(Localization.T("done"));

            if (!config.AutoRun)
            {
                Console.WriteLine(Localization.T("program.exit.pause"));
                Console.ReadLine();
            }

            WriteLogAndExit(0);
        }

        /// <summary>
        /// Wählt zufällig eine Datei aus dem Quellverzeichnis aus, die nicht dem übergebenen alten Dateinamen entspricht.
        /// Gibt null zurück wenn kein passendes Ergebnis gefunden wird.
        /// </summary>
        /// <param name="sourceDirectory">Quellordner mit Bildern</param>
        /// <param name="filenameOld">Dateiname, der ausgeschlossen werden soll</param>
        /// <returns>Pfad zur ausgewählten Datei oder null</returns>
        public static string? GetRandomFile(string sourceDirectory, string? filenameOld)
        {
            if (!Directory.Exists(sourceDirectory))
                return null;

            var files = Directory.GetFiles(sourceDirectory, "*");
            if (files.Length == 0)
                return null;

            // Ausschluss des aktuell verwendeten Bildes (nur wenn filenameOld != null)
            var candidates = string.IsNullOrEmpty(filenameOld)
                ? files
                : [.. files.Where(f => Path.GetFileName(f) != filenameOld)];

            if (candidates.Length == 0)
                return null;

            // Zufällige Auswahl
            return candidates[_rng.Next(candidates.Length)];
        }

        /// <summary>
        /// Lädt die Konfiguration aus einer JSON-Datei. Sucht in einigen typischen Orten.
        /// Erwartetes Format: { "SourceDirectory":"...", "VencordDirectory":"..." }
        /// </summary>
        private static Config? LoadConfig()
        {
            var possible = new[] {
                Path.Combine(AppContext.BaseDirectory, "config.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "config.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config.json")
            };

            string? found = possible.FirstOrDefault(File.Exists);
            if (found == null) return null;

            try
            {
                var json = File.ReadAllText(found);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cfg = JsonSerializer.Deserialize<Config>(json, options);

                if (cfg == null || string.IsNullOrEmpty(cfg.SourceDirectory) || string.IsNullOrEmpty(cfg.VencordDirectory))
                    return null;

                return cfg;
            }
            catch
            {
                return null;
            }
        }

        // Ermittelt den Pfad zur config.json: existierende Datei falls vorhanden, sonst Standard im BaseDirectory
        private static string GetConfigFilePath()
        {
            var possible = new[] {
                Path.Combine(AppContext.BaseDirectory, "config.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "config.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config.json")
            };

            var found = possible.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(found)) return found!;

            // Standardpfad neben der EXE
            return Path.Combine(AppContext.BaseDirectory, "config.json");
        }

        // Speichert die Konfiguration als prettified JSON an dem angegebenen Pfad
        private static void SaveConfig(Config cfg, string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(cfg, options);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        // Registriert das Programm im Autostart (HKCU Run) wenn gewünscht
        [SupportedOSPlatform("windows")]
        private static void EnsureStartupRegistered(Config cfg)
        {
            if (!cfg.AutoRun) return;

            try
            {
                // Prefer EntryAssembly location
                string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
                string? runCommand = null;

                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    // If assembly path points to a DLL (framework-dependent), try to prefer an .exe next to it
                    if (assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var exeCandidate = Path.ChangeExtension(assemblyPath, ".exe");
                        if (File.Exists(exeCandidate))
                        {
                            runCommand = "\"" + exeCandidate + "\"";
                        }
                        else
                        {
                            // Fall back to host process (usually dotnet) with dll as argument
                            var host = Process.GetCurrentProcess().MainModule?.FileName;
                            if (!string.IsNullOrEmpty(host))
                            {
                                runCommand = "\"" + host + "\" \"" + assemblyPath + "\"";
                            }
                            else
                            {
                                // Last resort: quote the dll path (may prompt the user on startup)
                                runCommand = "\"" + assemblyPath + "\"";
                            }
                        }
                    }
                    else
                    {
                        // assembly is an exe
                        runCommand = "\"" + assemblyPath + "\"";
                    }
                }

                // If still unknown, try current process main module
                if (string.IsNullOrEmpty(runCommand))
                {
                    var proc = Process.GetCurrentProcess();
                    var procExe = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(procExe))
                    {
                        runCommand = "\"" + procExe + "\"";
                    }
                }

                if (string.IsNullOrEmpty(runCommand))
                    return;

                using var key = Registry.CurrentUser.OpenSubKey(@"Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (key == null) return;
                key.SetValue("RandomDiscordHintergrund", runCommand);
                Console.WriteLine(Localization.T("autorun.registered") + " -> " + runCommand);
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format(Localization.T("autorun.error"), ex.Message));
            }
        }

        

        // Versucht, den Vencord-Ordner automatisch zu ermitteln, andernfalls fragt den Benutzer.
        // Gibt "" zurück, wenn abgebrochen.
        private static string VencordDirectory(string? current)
        {
            // Versuche %APPDATA%/Vencord
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    var candidate = Path.Combine(appData, "Vencord");
                    if (Directory.Exists(candidate))
                    {
                        Console.WriteLine(string.Format(Localization.T("vencord.found"), candidate));
                        return candidate;
                    }
                }
            }
            catch
            {
                // ignore
            }

            // Falls aktueller Wert valide ist, verwenden
            if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            {
                Console.WriteLine(string.Format(Localization.T("vencord.using.config"), current));
                return current;
            }

            // Interaktive Abfrage
            Console.WriteLine(Localization.T("vencord.notfound"));
            Console.WriteLine(Localization.T("vencord.instruction"));
            while (true)
            {
                Console.Write(Localization.T("vencord.prompt"));
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("cancel", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                if (Directory.Exists(input))
                    return input.Trim();

                Console.WriteLine(Localization.T("vencord.path.notexist"));
            }
        }

        private static void WriteLogAndExit(int exitCode)
        {
            try
            {
                // Flush Console writers
                Console.Out.Flush();

                string logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logsDir);
                string filename = $"run_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                string path = Path.Combine(logsDir, filename);

                // Write buffer content to file
                var content = _logBuffer?.ToString() ?? string.Empty;
                // Append a footer with exit code and timestamp
                content = $"Timestamp: {DateTime.Now:O}\nExitCode: {exitCode}\n\n" + content;
                File.WriteAllText(path, content, Encoding.UTF8);

                // Restore original console and notify
                if (_originalOut != null)
                    Console.SetOut(_originalOut);
                if (_originalErr != null)
                    Console.SetError(_originalErr);

                Console.WriteLine(string.Format(Localization.T("log.written"), path));
            }
            catch
            {
                // best effort: ignore logging errors
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        private class TeeTextWriter(TextWriter first, TextWriter second) : TextWriter
        {
            private readonly TextWriter _first = first;
            private readonly TextWriter _second = second;

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                // write via native console if possible to ensure Unicode glyphs
                NativeConsole.Write(value.ToString());
                _second.Write(value);
            }

            public override void Write(string? value)
            {
                if (value == null) return;
                NativeConsole.Write(value);
                _second.Write(value);
            }

            public override void WriteLine(string? value)
            {
                value ??= string.Empty;
                NativeConsole.Write(value + "\n");
                _second.WriteLine(value);
            }

            public override void Flush()
            {
                _second.Flush();
            }
        }

        private static class NativeConsole
        {
            private const int STD_OUTPUT_HANDLE = -11;

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool WriteConsoleW(IntPtr hConsoleOutput, string lpBuffer, uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten, IntPtr lpReserved);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr GetStdHandle(int nStdHandle);

            [DllImport("kernel32.dll")]
            private static extern uint GetFileType(IntPtr hFile);

            private const uint FILE_TYPE_CHAR = 0x0002;

            private static bool IsConsoleAttached()
            {
                try
                {
                    var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                    if (handle == IntPtr.Zero) return false;
                    var ft = GetFileType(handle);
                    return (ft & FILE_TYPE_CHAR) == FILE_TYPE_CHAR;
                }
                catch
                {
                    return false;
                }
            }

            public static void Write(string s)
            {
                if (s == null) return;

                if (IsConsoleAttached())
                {
                    try
                    {
                        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                        WriteConsoleW(handle, s, (uint)s.Length, out _, IntPtr.Zero);
                        return;
                    }
                    catch
                    {
                        // fallback
                    }
                }

                // fallback to managed Console
                try { Console.Out.Write(s); } catch { }
            }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleOutputCP(uint wCodePageID);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool SetConsoleCP(uint wCodePageID);
        }

        private static string AskSourceDirectory(string? current)
        {
            Console.WriteLine(Localization.T("source.prompt"));
            if (!string.IsNullOrEmpty(current))
                Console.WriteLine(string.Format(Localization.T("source.current"), current));
            Console.WriteLine(Localization.T("source.cancel.info"));

            while (true)
            {
                Console.Write(Localization.T("source.input"));
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("cancel", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                if (Directory.Exists(input))
                    return input.Trim();

                Console.WriteLine(Localization.T("source.notexist"));
            }
        }

        public static class ImageColorHelper
        {
            [SupportedOSPlatform("windows6.1")]
            public static string GetVibrantColorHSL(string imagePath)
            {
                using Bitmap bmp = new(imagePath);

                float bestScore = 0f;
                float bestH = 0f, bestS = 0f, bestL = 0f;

                // Schrittweite reduzieren für große Bilder, um Laufzeit zu verringern
                int step = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 100);

                for (int y = 0; y < bmp.Height; y += step)
                {
                    for (int x = 0; x < bmp.Width; x += step)
                    {
                        Color c = bmp.GetPixel(x, y);
                        // Ignoriere fast transparente Pixel
                        if (c.A < 200) continue;

                        // Konvertiere RGB -> HSL
                        RgbToHsl(c, out float h, out float s, out float l);

                        // Vibrant-Bewertung:
                        // hohe Sättigung + Lightness nahe 0.5 (nicht zu dunkel/hell) ist gewünscht
                        float lightnessWeight = 1f - Math.Abs(l - 0.5f);
                        float score = s * lightnessWeight;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestH = h;
                            bestS = s;
                            bestL = l;
                        }
                    }
                }

                // Rückgabe als H_S_L (H 0–360, S/L 0–1)
                return $"{bestH:F1}_{bestS:F2}_{bestL:F2}";
            }

            /// <summary>
            /// Konvertiert eine System.Drawing.Color (RGB) in HSL (Hue, Saturation, Lightness).
            /// </summary>
            private static void RgbToHsl(Color color, out float h, out float s, out float l)
            {
                float r = color.R / 255f;
                float g = color.G / 255f;
                float b = color.B / 255f;

                float max = Math.Max(r, Math.Max(g, b));
                float min = Math.Min(r, Math.Min(g, b));
                float delta = max - min;

                // Lightness berechnen
                l = (max + min) / 2f;

                // Wenn delta 0 ist, ist es ein Grauton -> keine Sättigung oder Hue
                if (delta == 0)
                {
                    s = 0;
                    h = 0;
                    return;
                }

                // Saturation nach HSL-Formel
                s = delta / (1f - Math.Abs(2f * l - 1f));

                // Hue berechnen abhängig von der größten Komponente
                if (max == r)
                    h = 60f * (((g - b) / delta) % 6f);
                else if (max == g)
                    h = 60f * (((b - r) / delta) + 2f);
                else
                    h = 60f * (((r - g) / delta) + 4f);

                if (h < 0) h += 360f;
            }
        }

        // Kleine POCO-Klasse für die Konfiguration
        internal class Config
        {
            public string SourceDirectory { get; set; } = string.Empty;
            public string VencordDirectory { get; set; } = string.Empty;
            public bool AutoRun { get; set; } = false; // wenn true, wird ein Autostart-Eintrag gesetzt
            public bool AutoRunSet { get; set; } = false; // wurde der Benutzer bereits gefragt?
            public bool HasRunBefore { get; set; } = false; // wird nach dem ersten Durchlauf gesetzt
            public string? Language { get; set; } = "de"; // default deutsch
            public bool LanguageSet { get; set; } = false;
        }

        // Einfache Lokalisierungs-Hilfe: lädt JSON-Dateien aus dem 'lang' Ordner
        internal static class Localization
        {
            private static Dictionary<string, string> _map = [];
            private static string _lang = "en";

            public static void Init(string lang)
            {
                _lang = string.IsNullOrWhiteSpace(lang) ? "en" : lang;
                LoadLangFile(_lang);
            }

            private static void LoadLangFile(string lang)
            {
                try
                {
                    var baseDir = AppContext.BaseDirectory;
                    var langDir = Path.Combine(baseDir, "lang");
                    var file = Path.Combine(langDir, lang + ".json");
                    if (!File.Exists(file))
                    {
                        // try embedded fallback: english
                        file = Path.Combine(langDir, "en.json");
                        if (!File.Exists(file))
                        {
                            _map = new Dictionary<string, string>();
                            return;
                        }
                    }

                    // Read raw bytes and try multiple decodings to avoid mojibake if file saved in ANSI
                    var bytes = File.ReadAllBytes(file);
                    string json = TryDecode(bytes);

                    _map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch
                {
                    _map = new Dictionary<string, string>();
                }
            }

            private static string TryDecode(byte[] bytes)
            {
                // Try UTF8 first
                try
                {
                    var s = Encoding.UTF8.GetString(bytes);
                    if (!ContainsReplacementChar(s)) return s;
                }
                catch { }

                // Try system default (ANSI)
                try
                {
                    var s = Encoding.Default.GetString(bytes);
                    if (!ContainsReplacementChar(s)) return s;
                }
                catch { }

                // Try UTF16 (LE)
                try
                {
                    var s = Encoding.Unicode.GetString(bytes);
                    if (!ContainsReplacementChar(s)) return s;
                }
                catch { }

                // Fallback to UTF8 replacement
                return Encoding.UTF8.GetString(bytes);
            }

            private static bool ContainsReplacementChar(string s)
            {
                // Only consider the Unicode replacement character as decoding failure.
                return s.Contains('\uFFFD');
            }

            public static string T(string key)
            {
                if (_map != null && _map.TryGetValue(key, out var val))
                    return val;
                // fallback: return key
                return key;
            }
        }

    }
}
