using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace SnapRangeControl
{
    /// <summary>
    /// Mod settings. Stored only in the mod settings file (ModsSettings), never in save games.
    /// </summary>
    [FileLocation(nameof(SnapRangeControl))]
    [SettingsUIGroupOrder(kGeneralGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kDebugGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kDebugGroup = "Debug";

        /// <summary>Vanilla guide line reach for roads: PlaceableNetData.m_SnapDistance (8 m) * 64.</summary>
        public const int kVanillaGuideReach = 512;
        public const int kMinGuideReach = 16;

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool Enabled { get; set; }

        [SettingsUISlider(min = kMinGuideReach, max = kVanillaGuideReach, step = 8, scalarMultiplier = 1, unit = Unit.kLength)]
        [SettingsUISection(kSection, kGeneralGroup)]
        public int MaxGuideReach { get; set; }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kGeneralGroup)]
        public bool ResetToDefaults
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        [SettingsUISection(kSection, kDebugGroup)]
        public bool DebugLogging { get; set; }

        /// <summary>True when the settings ask for any change from vanilla behaviour.</summary>
        public bool IsLimiting => Enabled && MaxGuideReach < kVanillaGuideReach;

        public override void SetDefaults()
        {
            Enabled = true;
            MaxGuideReach = kVanillaGuideReach;
            DebugLogging = false;
        }
    }
}
