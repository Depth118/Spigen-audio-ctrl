using System.Collections.Generic;
using System.Linq;

namespace SpigenAudioCTRL.Device
{
    public sealed record AncMode(string Name, byte Mode, byte Scene, bool HasLevels, byte FixedLevel)
    {
        public AncSetting ToSetting(byte level) => new(Mode, Scene, HasLevels ? level : FixedLevel);
    }

    // Noise control modes from Spigen's configuration for this headset.
    // Deep, Commuting and Indoor take a level 0-2; the vendor default is 2.
    public static class AncModes
    {
        public const byte DefaultLevel = 2;
        public const byte MaxLevel = 2;

        public static readonly AncMode Adaptive = new("Adaptive", 1, 5, false, 0);
        public static readonly AncMode Deep = new("Deep", 1, 1, true, 0);
        public static readonly AncMode Commuting = new("Commuting", 1, 2, true, 0);
        public static readonly AncMode Indoor = new("Indoor", 1, 3, true, 0);
        public static readonly AncMode AntiWind = new("Anti-Wind", 1, 4, false, 0);
        public static readonly AncMode Transparency = new("Transparency", 3, 1, false, 4);
        public static readonly AncMode Off = new("Off", 2, 0, false, 0);

        public static IReadOnlyList<AncMode> All { get; } =
            new[] { Adaptive, Deep, Commuting, Indoor, AntiWind, Transparency, Off };

        public static AncMode? Find(AncSetting setting) => setting.Mode switch
        {
            2 => Off,
            3 => Transparency,
            1 => All.FirstOrDefault(m => m.Mode == 1 && m.Scene == setting.Scene),
            _ => null
        };
    }
}
