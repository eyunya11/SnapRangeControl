using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace SnapRangeControl
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger(nameof(SnapRangeControl)).SetShowsErrorsInUI(false);

        /// <summary>Current settings, read by the Harmony patch and the UI system. Null before OnLoad / after OnDispose.</summary>
        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            var setting = new Setting(this);
            setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(setting));
            GameManager.instance.localizationManager.AddSource("ja-JP", new LocaleJA(setting));
            setting.RegisterKeyBindings();
            AssetDatabase.global.LoadSettings(nameof(SnapRangeControl), setting, new Setting(this));
            setting.NormalizeReach();
            Settings = setting;

            log.Info($"Settings: Enabled={setting.Enabled}, MaxGuideReach={setting.MaxGuideReach}, DebugLogging={setting.DebugLogging}");

            GuideReachPatch.TryApply();

            updateSystem.UpdateAt<SnapRangeControlUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            GuideReachPatch.Remove();
            SnapRangeControlUISystem.FlushPendingSave();
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
