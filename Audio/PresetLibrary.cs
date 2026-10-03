using System;
using System.Collections.Generic;
using System.Linq;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL.Audio
{
    // Built-in presets plus the user's saved ones; shared by the equalizer view and the tray menu.
    public sealed class PresetLibrary
    {
        public const string PersonalName = "Personal";

        private readonly AppSettings _settings;
        private List<EqPreset> _all = new();

        public IReadOnlyList<EqPreset> All => _all;

        public event Action? Changed;

        public PresetLibrary(AppSettings settings)
        {
            _settings = settings;
            Rebuild();
        }

        public EqPreset? Find(string name) => _all.FirstOrDefault(p => p.Name == name);

        public EqPreset? Match(IReadOnlyList<EqBand> bands) => EqPresets.FindMatch(_all, bands);

        // Saves under the given name (replacing a saved preset of that name) or the next "Custom N".
        public EqPreset Save(IEnumerable<EqBand> bands, string? name = null)
        {
            if (name == null)
            {
                int n = 1;
                do { name = $"Custom {n++}"; } while (_all.Any(p => p.Name == name));
            }

            _settings.CustomPresets.RemoveAll(p => p.Name == name);
            _settings.CustomPresets.Add(new SavedPreset { Name = name, Bands = bands.Select(b => b.Clone()).ToList() });
            _settings.Save();
            Rebuild();
            return Find(name)!;
        }

        public void Delete(EqPreset preset)
        {
            if (!preset.IsCustom) return;
            _settings.CustomPresets.RemoveAll(p => p.Name == preset.Name);
            _settings.Save();
            Rebuild();
        }

        private void Rebuild()
        {
            var all = new List<EqPreset>(EqPresets.BuiltIn);
            foreach (var saved in _settings.CustomPresets)
            {
                if (saved.Bands.Count != EqPresets.Frequencies.Length) continue;
                string description = saved.Name == PersonalName
                    ? "Your curve from the Find your sound comparisons."
                    : "Your saved preset.";
                all.Add(new EqPreset(saved.Name, description, saved.Bands.Select(EqMath.Clamp).ToArray(), isCustom: true));
            }
            _all = all;
            Changed?.Invoke();
        }
    }
}
