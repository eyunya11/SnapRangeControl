using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Unity.Mathematics;

namespace SnapRangeControl
{
    /// <summary>
    /// Mod settings. Stored only in the mod's own settings file, never in save games.
    /// </summary>
    [FileLocation(nameof(SnapRangeControl))]
    [SettingsUIGroupOrder(kGeneralGroup, kKeyBindingGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kKeyBindingGroup, kDebugGroup)]
    [SettingsUIKeyboardAction(kIncreaseReachAction, ActionType.Button, usages: new[] { Usages.kToolUsage }, interactions: new[] { "Press" })]
    [SettingsUIKeyboardAction(kDecreaseReachAction, ActionType.Button, usages: new[] { Usages.kToolUsage }, interactions: new[] { "Press" })]
    [SettingsUIKeyboardAction(kToggleEnabledAction, ActionType.Button, usages: new[] { Usages.kToolUsage }, interactions: new[] { "Press" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kKeyBindingGroup = "KeyBindings";
        public const string kDebugGroup = "Debug";

        public const string kIncreaseReachAction = "IncreaseReach";
        public const string kDecreaseReachAction = "DecreaseReach";
        public const string kToggleEnabledAction = "ToggleEnabled";

        /// <summary>Vanilla guide line reach for roads: PlaceableNetData.m_SnapDistance (8 m) * 64.</summary>
        public const int kVanillaGuideReach = 512;
        public const int kReachStep = 32;
        public const int kMinGuideReach = kReachStep;

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool Enabled { get; set; }

        [SettingsUISlider(min = kMinGuideReach, max = kVanillaGuideReach, step = kReachStep, scalarMultiplier = 1, unit = Unit.kLength)]
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

        [SettingsUIKeyboardBinding(BindingKeyboard.None, kIncreaseReachAction)]
        [SettingsUISection(kSection, kKeyBindingGroup)]
        public ProxyBinding IncreaseReachBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.None, kDecreaseReachAction)]
        [SettingsUISection(kSection, kKeyBindingGroup)]
        public ProxyBinding DecreaseReachBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.None, kToggleEnabledAction)]
        [SettingsUISection(kSection, kKeyBindingGroup)]
        public ProxyBinding ToggleEnabledBinding { get; set; }

        [SettingsUIButton]
        [SettingsUISection(kSection, kKeyBindingGroup)]
        public bool ResetBindings
        {
            set
            {
                ResetKeyBindings();
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

        /// <summary>Keeps the reach within range and on the 32 m grid (older versions allowed 16 m and 8 m steps).</summary>
        public void NormalizeReach()
        {
            int clamped = math.clamp(MaxGuideReach, kMinGuideReach, kVanillaGuideReach);
            int snapped = (int)math.round(clamped / (float)kReachStep) * kReachStep;
            MaxGuideReach = math.clamp(snapped, kMinGuideReach, kVanillaGuideReach);
        }

        /// <summary>Changes the reach by one step (direction +1 or -1) and saves. Does nothing while the mod is turned off.</summary>
        public void StepReach(int direction)
        {
            if (!Enabled)
                return;
            NormalizeReach();
            int next = math.clamp(MaxGuideReach + direction * kReachStep, kMinGuideReach, kVanillaGuideReach);
            if (next == MaxGuideReach)
                return;
            MaxGuideReach = next;
            ApplyAndSave();
        }

        public void ToggleEnabled()
        {
            Enabled = !Enabled;
            ApplyAndSave();
        }
    }
}
