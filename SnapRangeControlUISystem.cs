using System;
using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.Tools;
using Game.UI;

namespace SnapRangeControl
{
    /// <summary>
    /// Exposes the settings to the tool options UI and handles the key bindings.
    /// Both are only active while the road tool (NetToolSystem with a road prefab) is selected.
    /// </summary>
    public partial class SnapRangeControlUISystem : UISystemBase
    {
        public const string kGroup = "snapRangeControl";

        /// <summary>Seconds without further changes before the settings file is written (clicks and key presses are batched).</summary>
        private const float kSaveDelay = 0.5f;

        private static bool s_SavePending;
        private static float s_SaveDue;

        private ToolSystem m_ToolSystem;
        private NetToolSystem m_NetToolSystem;

        private ValueBinding<bool> m_VisibleBinding;
        private ValueBinding<bool> m_EnabledBinding;
        private ValueBinding<int> m_ReachBinding;

        private ProxyAction m_IncreaseAction;
        private ProxyAction m_DecreaseAction;
        private ProxyAction m_ToggleAction;

        public override GameMode gameMode => GameMode.Game;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_NetToolSystem = World.GetOrCreateSystemManaged<NetToolSystem>();

            Setting settings = Mod.Settings;
            AddBinding(m_VisibleBinding = new ValueBinding<bool>(kGroup, "visible", false));
            AddBinding(m_EnabledBinding = new ValueBinding<bool>(kGroup, "enabled", settings?.Enabled ?? true));
            AddBinding(m_ReachBinding = new ValueBinding<int>(kGroup, "reach", settings?.MaxGuideReach ?? Setting.kVanillaGuideReach));
            AddBinding(new ValueBinding<int>(kGroup, "minReach", Setting.kMinGuideReach));
            AddBinding(new ValueBinding<int>(kGroup, "maxReach", Setting.kVanillaGuideReach));

            AddBinding(new TriggerBinding(kGroup, "increase", () => StepReach(+1)));
            AddBinding(new TriggerBinding(kGroup, "decrease", () => StepReach(-1)));
            AddBinding(new TriggerBinding(kGroup, "toggle", ToggleEnabled));

            if (settings != null)
            {
                m_IncreaseAction = settings.GetAction(Setting.kIncreaseReachAction);
                m_DecreaseAction = settings.GetAction(Setting.kDecreaseReachAction);
                m_ToggleAction = settings.GetAction(Setting.kToggleEnabledAction);
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            Setting settings = Mod.Settings;
            bool roadTool = settings != null
                && m_ToolSystem.activeTool == m_NetToolSystem
                && GuideReachPatch.AppliesTo(m_NetToolSystem);

            // Only listen to the keys while the road tool is in use, so they never clash with other bindings elsewhere.
            SetActionEnabled(m_IncreaseAction, roadTool);
            SetActionEnabled(m_DecreaseAction, roadTool);
            SetActionEnabled(m_ToggleAction, roadTool);

            if (roadTool)
            {
                if (m_IncreaseAction != null && m_IncreaseAction.WasPerformedThisFrame())
                    StepReach(+1);
                if (m_DecreaseAction != null && m_DecreaseAction.WasPerformedThisFrame())
                    StepReach(-1);
                if (m_ToggleAction != null && m_ToggleAction.WasPerformedThisFrame())
                    ToggleEnabled();
            }

            if (s_SavePending && UnityEngine.Time.realtimeSinceStartup >= s_SaveDue)
                FlushPendingSave();

            m_VisibleBinding.Update(roadTool);
            if (settings != null)
            {
                m_EnabledBinding.Update(settings.Enabled);
                m_ReachBinding.Update(settings.MaxGuideReach);
            }
        }

        protected override void OnStopRunning()
        {
            FlushPendingSave();
            base.OnStopRunning();
        }

        protected override void OnDestroy()
        {
            FlushPendingSave();
            base.OnDestroy();
        }

        private static void SetActionEnabled(ProxyAction action, bool enabled)
        {
            if (action != null && action.shouldBeEnabled != enabled)
                action.shouldBeEnabled = enabled;
        }

        // The lambdas live in their own methods, so a closure is allocated only when a key or button is actually used,
        // not on every OnUpdate call.
        private static void StepReach(int direction) => Run(settings => settings.StepReach(direction));

        private static void ToggleEnabled() => Run(settings =>
        {
            settings.ToggleEnabled();
            return true;
        });

        /// <summary>Runs a change on the settings in memory and schedules a save. The function returns true when something changed.</summary>
        private static void Run(Func<Setting, bool> change)
        {
            try
            {
                Setting settings = Mod.Settings;
                if (settings == null || !change(settings))
                    return;

                s_SavePending = true;
                s_SaveDue = UnityEngine.Time.realtimeSinceStartup + kSaveDelay;
                if (settings.DebugLogging)
                    Mod.log.Info($"Settings changed from UI/keys: Enabled={settings.Enabled}, MaxGuideReach={settings.MaxGuideReach}");
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Failed to change settings from UI/keys.");
            }
        }

        /// <summary>Writes the settings file if a change is waiting. Called after a short idle time, and on shutdown.</summary>
        public static void FlushPendingSave()
        {
            if (!s_SavePending)
                return;
            s_SavePending = false;
            try
            {
                Mod.Settings?.Save();
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Failed to save settings.");
            }
        }
    }
}
