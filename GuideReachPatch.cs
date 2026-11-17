using System;
using System.Reflection;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace SnapRangeControl
{
    /// <summary>
    /// Limits how far guide lines of existing roads can snap the road tool.
    ///
    /// Vanilla (game 1.6.4f1): NetToolSystem.SnapJob (Burst compiled, so it cannot be patched) searches
    /// existing roads within PlaceableNetData.m_SnapDistance * 64 (512 m for roads) and extends guide lines
    /// of that length from their ends. The cursor snaps to a guide line when it is within m_SnapDistance (8 m).
    ///
    /// This patch wraps the managed, non-Burst NetToolSystem.SnapControlPoints(JobHandle, bool) method,
    /// which schedules SnapJob. Its result is consumed in the same frame by FixControlPoints and the
    /// definition jobs, so fixing the snap result here needs no preview repair.
    /// After SnapJob finishes, if the snapped point lies on a guide line farther than the configured reach
    /// from that guide line's origin (the road end), the snap is recomputed once with guide lines disabled.
    ///
    /// If any target member is missing (e.g. after a game update), nothing is patched and the mod stays inactive.
    /// </summary>
    internal static class GuideReachPatch
    {
        private const string kHarmonyId = "com.snaprangecontrol.guidereach";

        /// <summary>Max distance (m, XZ) between the snapped point and a guide line to treat the point as lying on it.</summary>
        private const float kOnLineTolerance = 0.05f;

        private static Harmony s_Harmony;
        private static AccessTools.FieldRef<ToolBaseSystem, Snap> s_SnapOnMask;
        private static AccessTools.FieldRef<ToolBaseSystem, Snap> s_SnapOffMask;
        private static Func<NetToolSystem, JobHandle, bool, JobHandle> s_SnapControlPoints;

        private static bool s_InRerun;
        private static bool s_HasRawPoint;
        private static ControlPoint s_RawPoint;

        private static float s_LastDebugLogTime;
        private static float s_LastDebugReach = -1f;

        public static bool IsActive { get; private set; }

        public static void TryApply()
        {
            if (IsActive)
                return;

            try
            {
                MethodInfo target = AccessTools.Method(typeof(NetToolSystem), "SnapControlPoints", new[] { typeof(JobHandle), typeof(bool) });
                FieldInfo onMask = AccessTools.Field(typeof(ToolBaseSystem), "m_SnapOnMask");
                FieldInfo offMask = AccessTools.Field(typeof(ToolBaseSystem), "m_SnapOffMask");
                FieldInfo controlPoints = AccessTools.Field(typeof(NetToolSystem), "m_ControlPoints");
                MethodInfo getSnapLines = AccessTools.Method(typeof(NetToolSystem), nameof(NetToolSystem.GetSnapLines));

                string missing = null;
                if (target == null || target.ReturnType != typeof(JobHandle))
                    missing = "NetToolSystem.SnapControlPoints(JobHandle, bool) : JobHandle";
                else if (onMask == null || onMask.FieldType != typeof(Snap) || offMask == null || offMask.FieldType != typeof(Snap))
                    missing = "ToolBaseSystem.m_SnapOnMask / m_SnapOffMask : Snap";
                else if (controlPoints == null || controlPoints.FieldType != typeof(NativeList<ControlPoint>))
                    missing = "NetToolSystem.m_ControlPoints : NativeList<ControlPoint>";
                else if (getSnapLines == null || getSnapLines.ReturnType != typeof(NativeList<SnapLine>))
                    missing = "NetToolSystem.GetSnapLines : NativeList<SnapLine>";

                if (missing != null)
                {
                    Mod.log.Error($"Patch target not found: {missing}. The game may have been updated. SnapRangeControl is disabled; the game runs unmodified.");
                    return;
                }

                s_SnapOnMask = AccessTools.FieldRefAccess<ToolBaseSystem, Snap>(onMask);
                s_SnapOffMask = AccessTools.FieldRefAccess<ToolBaseSystem, Snap>(offMask);
                s_SnapControlPoints = AccessTools.MethodDelegate<Func<NetToolSystem, JobHandle, bool, JobHandle>>(target);

                s_Harmony = new Harmony(kHarmonyId);
                s_Harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(GuideReachPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(GuideReachPatch), nameof(Postfix)));

                IsActive = true;
                Mod.log.Info("Patched NetToolSystem.SnapControlPoints.");
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Failed to apply patch. SnapRangeControl is disabled; the game runs unmodified.");
                Remove();
            }
        }

        public static void Remove()
        {
            try
            {
                s_Harmony?.UnpatchAll(kHarmonyId);
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Failed to remove patch.");
            }
            s_Harmony = null;
            IsActive = false;
            s_HasRawPoint = false;
            s_InRerun = false;
        }

        private static void Disable(Exception e)
        {
            Mod.log.Error(e, "Unexpected error in guide line reach patch. SnapRangeControl disabled itself for this session.");
            Remove();
        }

        private static bool ShouldHandle(NetToolSystem tool)
        {
            Setting settings = Mod.Settings;
            if (!IsActive || settings == null || !(settings.IsLimiting || settings.DebugLogging))
                return false;
            if (tool.actualMode == NetToolSystem.Mode.Replace)
                return false;
            if (!(tool.prefab is RoadPrefab))
                return false;
            Snap actualSnap = ToolBaseSystem.GetActualSnap(tool.selectedSnap, s_SnapOnMask(tool), s_SnapOffMask(tool));
            return (actualSnap & Snap.GuideLines) != Snap.None;
        }

        private static void Prefix(NetToolSystem __instance, JobHandle inputDeps)
        {
            s_HasRawPoint = false;
            if (s_InRerun)
                return;

            try
            {
                if (!ShouldHandle(__instance))
                    return;

                // Make sure nothing is still writing the control points, then remember the raw (unsnapped) point.
                inputDeps.Complete();
                NativeList<ControlPoint> controlPoints = __instance.GetControlPoints(out JobHandle deps);
                deps.Complete();
                if (!controlPoints.IsCreated || controlPoints.Length == 0)
                    return;

                s_RawPoint = controlPoints[controlPoints.Length - 1];
                s_HasRawPoint = true;
            }
            catch (Exception e)
            {
                Disable(e);
            }
        }

        private static void Postfix(NetToolSystem __instance, bool removeUpgrade, ref JobHandle __result)
        {
            if (s_InRerun || !s_HasRawPoint)
                return;
            s_HasRawPoint = false;

            try
            {
                Setting settings = Mod.Settings;
                if (settings == null)
                    return;

                __result.Complete();
                NativeList<ControlPoint> controlPoints = __instance.GetControlPoints(out JobHandle cpDeps);
                NativeList<SnapLine> snapLines = __instance.GetSnapLines(out JobHandle slDeps);
                JobHandle.CombineDependencies(cpDeps, slDeps).Complete();
                if (!controlPoints.IsCreated || controlPoints.Length == 0 || !snapLines.IsCreated)
                    return;

                ControlPoint snapped = controlPoints[controlPoints.Length - 1];
                float reach = GetGuideLineReach(snapped, snapLines);
                if (reach < 0f)
                    return;

                bool reject = settings.IsLimiting && reach > settings.MaxGuideReach;
                if (settings.DebugLogging)
                    DebugLog(reach, reject, settings.MaxGuideReach);
                if (!reject)
                    return;

                // Recompute the snap once from the raw point with guide lines disabled.
                controlPoints[controlPoints.Length - 1] = s_RawPoint;
                ref Snap onMask = ref s_SnapOnMask(__instance);
                Snap savedOnMask = onMask;
                onMask &= ~Snap.GuideLines;
                s_InRerun = true;
                try
                {
                    // The snap mask is copied into the job when it is scheduled, so it can be restored right after.
                    __result = s_SnapControlPoints(__instance, __result, removeUpgrade);
                }
                finally
                {
                    s_InRerun = false;
                    onMask = savedOnMask;
                }
            }
            catch (Exception e)
            {
                Disable(e);
            }
        }

        /// <summary>
        /// Returns the largest distance from a guide line origin (road end) to the snapped point among the guide lines
        /// the point lies on, or -1 when the point is not on any guide line.
        /// </summary>
        private static float GetGuideLineReach(ControlPoint snapped, NativeList<SnapLine> snapLines)
        {
            float2 p = snapped.m_Position.xz;
            float best = -1f;
            for (int i = 0; i < snapLines.Length; i++)
            {
                SnapLine line = snapLines[i];
                if ((line.m_Flags & SnapLineFlags.GuideLine) == 0)
                    continue;

                // Guide lines are created with NetUtils.StraightCurve(origin, end): curve.a = origin, curve.d = end.
                var segment = new Line2.Segment(line.m_Curve.a.xz, line.m_Curve.d.xz);
                if (MathUtils.Distance(segment, p, out float _) > kOnLineTolerance)
                    continue;

                best = math.max(best, math.distance(segment.a, p));
            }
            return best;
        }

        private static void DebugLog(float reach, bool rejected, int limit)
        {
            float now = Time.realtimeSinceStartup;
            if (!rejected && math.abs(reach - s_LastDebugReach) < 1f && now - s_LastDebugLogTime < 0.5f)
                return;
            s_LastDebugLogTime = now;
            s_LastDebugReach = reach;
            Mod.log.Info($"Guide line snap: reach={reach:F1} m, limit={limit} m, {(rejected ? "REJECTED" : "kept")}");
        }
    }
}
