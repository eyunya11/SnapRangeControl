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

        /// <summary>|cos| of the angle between two guide lines treated as running along the same line (about 0.8 degrees).</summary>
        private const float kSameLineDot = 0.9999f;

        // Scratch buffers for GetGuideLineReach (main thread only).
        private static float2[] s_OnLineDirections = new float2[16];
        private static float[] s_OnLineReaches = new float[16];

        private static Harmony s_Harmony;
        private static AccessTools.FieldRef<ToolBaseSystem, Snap> s_SnapOnMask;
        private static AccessTools.FieldRef<ToolBaseSystem, Snap> s_SnapOffMask;
        private static Func<NetToolSystem, JobHandle, bool, JobHandle> s_SnapControlPoints;

        private static bool s_InRerun;
        private static bool s_Disabled;
        private static bool s_HasRawPoints;
        private static int s_RawPointCount;
        private static ControlPoint[] s_RawPoints = new ControlPoint[8];

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
            s_HasRawPoints = false;
            s_InRerun = false;
        }

        /// <summary>
        /// Stops the patch from doing anything for the rest of the session. The detour stays in place
        /// (unpatching from inside the patched call is unsafe); Prefix/Postfix become no-ops.
        /// </summary>
        private static void Disable(Exception e)
        {
            s_Disabled = true;
            s_HasRawPoints = false;
            Mod.log.Error(e, "Unexpected error in guide line reach patch. SnapRangeControl disabled itself for this session; the road tool behaves like vanilla.");
        }

        private static bool ShouldHandle(NetToolSystem tool)
        {
            Setting settings = Mod.Settings;
            if (settings == null || !(settings.IsLimiting || settings.DebugLogging))
                return false;
            return AppliesTo(tool);
        }

        /// <summary>
        /// True when the patch is working and the road tool is in a state where guide line snapping can happen:
        /// not Replace mode, a road prefab, and guide line snapping turned on. Also used by the UI to decide
        /// when its controls are shown, so both stay in step.
        /// </summary>
        internal static bool AppliesTo(NetToolSystem tool)
        {
            if (s_Disabled || !IsActive)
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
            s_HasRawPoints = false;
            if (s_InRerun || s_Disabled)
                return;

            try
            {
                if (!ShouldHandle(__instance))
                    return;

                // Make sure nothing is still writing the control points, then remember all of them as they are
                // before snapping. SnapJob writes the last point and, in some modes, the one before it.
                inputDeps.Complete();
                NativeList<ControlPoint> controlPoints = __instance.GetControlPoints(out JobHandle deps);
                deps.Complete();
                if (!controlPoints.IsCreated || controlPoints.Length == 0)
                    return;

                int count = controlPoints.Length;
                if (s_RawPoints.Length < count)
                    s_RawPoints = new ControlPoint[math.max(count, s_RawPoints.Length * 2)];
                for (int i = 0; i < count; i++)
                    s_RawPoints[i] = controlPoints[i];
                s_RawPointCount = count;
                s_HasRawPoints = true;
            }
            catch (Exception e)
            {
                Disable(e);
            }
        }

        private static void Postfix(NetToolSystem __instance, bool removeUpgrade, ref JobHandle __result)
        {
            if (s_InRerun || s_Disabled || !s_HasRawPoints)
                return;
            s_HasRawPoints = false;

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
                    DebugLog(reach, reject, settings.MaxGuideReach, CountNearGuideLines(snapped, snapLines, settings.MaxGuideReach));
                if (!reject)
                    return;

                // SnapJob does not change the number of control points outside Replace mode; bail out if it did.
                if (controlPoints.Length != s_RawPointCount)
                    return;

                // Recompute the snap once from the unsnapped points with guide lines disabled.
                for (int i = 0; i < s_RawPointCount; i++)
                    controlPoints[i] = s_RawPoints[i];
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
        /// Returns how far the snapped point is from the guide line origins (road ends) that explain it, or -1 when
        /// the point is not on any guide line.
        ///
        /// Guide lines that run along the same line (parallel or opposite and both through the point) are redundant:
        /// SnapJob creates a tangent guide line at every edge end, so on a road made of several edges the line
        /// ahead of the road also comes from every inner node. For such a group only the nearest origin counts.
        /// Lines in different directions mean the point is their intersection, so the farthest group decides.
        /// </summary>
        private static float GetGuideLineReach(ControlPoint snapped, NativeList<SnapLine> snapLines)
        {
            float2 p = snapped.m_Position.xz;
            int count = 0;
            for (int i = 0; i < snapLines.Length; i++)
            {
                SnapLine line = snapLines[i];
                if ((line.m_Flags & SnapLineFlags.GuideLine) == 0)
                    continue;

                // Guide lines are created with NetUtils.StraightCurve(origin, end): curve.a = origin, curve.d = end.
                var segment = new Line2.Segment(line.m_Curve.a.xz, line.m_Curve.d.xz);
                float2 direction = segment.b - segment.a;
                if (!MathUtils.TryNormalize(ref direction))
                    continue;
                if (MathUtils.Distance(segment, p, out float _) > kOnLineTolerance)
                    continue;

                if (s_OnLineDirections.Length <= count)
                {
                    Array.Resize(ref s_OnLineDirections, s_OnLineDirections.Length * 2);
                    Array.Resize(ref s_OnLineReaches, s_OnLineReaches.Length * 2);
                }
                s_OnLineDirections[count] = direction;
                s_OnLineReaches[count] = math.distance(segment.a, p);
                count++;
            }

            float result = -1f;
            for (int i = 0; i < count; i++)
            {
                // Nearest origin among the lines running along the same line as line i.
                float nearest = s_OnLineReaches[i];
                for (int j = 0; j < count; j++)
                {
                    if (math.abs(math.dot(s_OnLineDirections[i], s_OnLineDirections[j])) >= kSameLineDot)
                        nearest = math.min(nearest, s_OnLineReaches[j]);
                }
                result = math.max(result, nearest);
            }
            return result;
        }

        /// <summary>
        /// Counts guide lines whose origin is within the limit of the cursor. When a snap is rejected these are
        /// dropped too (the rerun disables all guide lines), so this shows what a rejection costs.
        /// </summary>
        private static int CountNearGuideLines(ControlPoint snapped, NativeList<SnapLine> snapLines, int limit)
        {
            float2 hit = snapped.m_HitPosition.xz;
            int count = 0;
            for (int i = 0; i < snapLines.Length; i++)
            {
                SnapLine line = snapLines[i];
                if ((line.m_Flags & SnapLineFlags.GuideLine) != 0 && math.distance(line.m_Curve.a.xz, hit) <= limit)
                    count++;
            }
            return count;
        }

        private static void DebugLog(float reach, bool rejected, int limit, int nearGuideLines)
        {
            float now = Time.realtimeSinceStartup;
            if (!rejected && math.abs(reach - s_LastDebugReach) < 1f && now - s_LastDebugLogTime < 0.5f)
                return;
            s_LastDebugLogTime = now;
            s_LastDebugReach = reach;
            Mod.log.Info($"Guide line snap: reach={reach:F1} m, limit={limit} m, {(rejected ? "REJECTED" : "kept")}, guide lines within limit={nearGuideLines}{(rejected && nearGuideLines > 0 ? " (also dropped)" : "")}");
        }
    }
}
