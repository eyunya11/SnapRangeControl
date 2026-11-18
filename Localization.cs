using System.Collections.Generic;
using Colossal;

namespace SnapRangeControl
{
    // Japanese text was translated with the help of AI (noted in README / mod description).

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Snap Range Control" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "Guide line snapping (road tool)" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Enabled)), "Enable" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Enabled)), "When off, the road tool behaves exactly like the unmodded game." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaxGuideReach)), "Guide line reach" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaxGuideReach)), $"How far from the end of an existing road its guide lines can snap the road tool. The game default is {Setting.kVanillaGuideReach} m (no change). Lower values stop snapping to guide lines of distant roads. When that happens, guide lines are skipped entirely for that position, so a nearby guide line may not snap there either. Only affects roads, not other networks." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetToDefaults)), "Reset to defaults" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetToDefaults)), "Restore the default settings (same behaviour as the unmodded game)." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ResetToDefaults)), "Reset all Snap Range Control settings to their defaults?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)), "Debug logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)), "Write guide line snap distances to the mod log file (Logs/SnapRangeControl.log). For troubleshooting only." },
            };
        }

        public void Unload()
        {
        }
    }

    public class LocaleJA : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleJA(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Snap Range Control" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "メイン" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "ガイドラインへのスナップ（道路ツール）" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "デバッグ" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Enabled)), "有効にする" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Enabled)), "オフにすると、道路ツールはModなしのときと同じ動きになります。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaxGuideReach)), "ガイドラインの到達距離" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaxGuideReach)), $"既存の道路の端から、どこまでの距離でガイドラインへのスナップが効くかを決めます。ゲームの既定は {Setting.kVanillaGuideReach} m（変更なし）です。小さくすると、離れた道路のガイドラインにスナップしなくなります。その場合、その位置ではガイドライン自体を使わないため、近くのガイドラインにもスナップしないことがあります。道路にだけ効き、ほかのネットワークには影響しません。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetToDefaults)), "既定値に戻す" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetToDefaults)), "設定を既定値（Modなしと同じ動き）に戻します。" },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ResetToDefaults)), "Snap Range Control の設定をすべて既定値に戻しますか？" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)), "デバッグログ" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)), "ガイドラインへのスナップ距離を、Mod用のログ（Logs/SnapRangeControl.log）に書き出します。不具合調査用です。" },
            };
        }

        public void Unload()
        {
        }
    }
}
