using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SpigenAudioCTRL.Audio;

namespace SpigenAudioCTRL.Infrastructure
{
    public sealed class SavedPreset
    {
        public string Name { get; set; } = "";
        public List<EqBand> Bands { get; set; } = new();
    }

    // Persisted to %APPDATA%\SpigenAudioCTRL\settings.json.
    public sealed class AppSettings
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpigenAudioCTRL", "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public ulong? LastDeviceAddress { get; set; }
        public bool ShowStockOverlay { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool TrayHintShown { get; set; }
        public List<SavedPreset> CustomPresets { get; set; } = new();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn("Settings", $"Load failed, using defaults: {ex.Message}");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Settings", $"Save failed: {ex.Message}");
            }
        }
    }
}
