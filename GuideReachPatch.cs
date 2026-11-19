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
    /// from that guide line's origin (the road end), the snap is recomputed without the distant guide lines:
    ///   1. The guide lines within the reach are saved from the first run.
    ///   2. SnapJob runs again with guide lines disabled (all other snapping as usual).
    ///   3. The saved guide lines are added back with the game's own ToolUtils.AddSnapPosition/AddSnapLine,
    ///      which gives the same choice vanilla would make if the distant guide lines did not exist
    ///      (the other snap stages only add candidates around the raw cursor, so the order does not matter).
    ///   4. If a guide line candidate wins, SnapJob runs a third time from that point with all position-moving
    ///      snaps disabled, so the game applies its own height, elevation and middle point handling.
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

        /// <summary>
        /// Snaps that move the point. Disabled for the final run, which only lets the game post-process a point
        /// that was already chosen (height, elevation, middle point, parent object, world bounds).
        /// </summary>
        private const Snap kPositionSnaps = Snap.ExistingGeometry | Snap.NearbyGeometry | Snap.GuideLines | Snap.CellLength |
            Snap.StraightDirection | Snap.ObjectSide | Snap.LotGrid | Snap.ZoneGrid | Snap.Shoreline;

        // Guide lines within the reach, saved from the first run (main thread only).
        private static SnapLine[] s_NearLines = new SnapLine[16];
        private static int s_NearLineCount;

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
                float reach = GetGuideLineReach(snapped, snapLines, out float farthestOrigin, out int lineCount, out int directionCount);
                if (reach < 0f)
                    return;

                int limit = settings.MaxGuideReach;
                bool reject = settings.IsLimiting && reach > limit;
                if (!reject)
                {
                    if (settings.DebugLogging)
                        DebugLog(reach, false, limit, 0, farthestOrigin, lineCount, directionCount, null);
                    return;
                }

                // SnapJob does not change the number of control points outside Replace mode; bail out if it did.
                if (controlPoints.Length != s_RawPointCount)
                    return;

                // 1. Keep the guide lines whose own snap position is within the reach. The next run clears m_SnapLines.
                SaveNearGuideLines(snapLines, limit);

                // 2. Snap again from the unsnapped points with guide lines disabled.
                RestoreRawPoints(controlPoints);
                __result = RunSnapJob(__instance, __result, removeUpgrade, ~Snap.GuideLines);
                __result.Complete();
                controlPoints = __instance.GetControlPoints(out cpDeps);
                snapLines = __instance.GetSnapLines(out slDeps);
                JobHandle.CombineDependencies(cpDeps, slDeps).Complete();
                if (controlPoints.Length != s_RawPointCount)
                    return;

                // 3. Add the near guide lines back, the way SnapJob adds them, and see whether one of them wins.
                ControlPoint withoutGuides = controlPoints[controlPoints.Length - 1];
                ControlPoint best = withoutGuides;
                var scratch = new NativeList<SnapLine>(snapLines.Length + s_NearLineCount, Allocator.Temp);
                try
                {
                    for (int i = 0; i < snapLines.Length; i++)
                        scratch.Add(snapLines[i]);
                    for (int i = 0; i < s_NearLineCount; i++)
                    {
                        ToolUtils.AddSnapPosition(ref best, s_NearLines[i].m_ControlPoint);
                        ToolUtils.AddSnapLine(ref best, scratch, s_NearLines[i]);
                    }
                }
                finally
                {
                    scratch.Dispose();
                }

                bool guideWins = !best.Equals(withoutGuides);
                if (guideWins)
                {
                    // 4. Let the game post-process the chosen point: snap from it with position-moving snaps disabled.
                    RestoreRawPoints(controlPoints);
                    ControlPoint raw = s_RawPoints[s_RawPointCount - 1];
                    ControlPoint start = raw;
                    start.m_HitPosition.xz = best.m_Position.xz;
                    start.m_Position.xz = best.m_Position.xz;
                    start.m_Direction = best.m_Direction;
                    controlPoints[controlPoints.Length - 1] = start;

                    __result = RunSnapJob(__instance, __result, removeUpgrade, ~kPositionSnaps);
                    __result.Complete();
                    controlPoints = __instance.GetControlPoints(out cpDeps);
                    snapLines = __instance.GetSnapLines(out slDeps);
                    JobHandle.CombineDependencies(cpDeps, slDeps).Complete();
                    if (controlPoints.Length == s_RawPointCount)
                    {
                        // Keep the real cursor position as the hit position, as vanilla does for a guide line snap.
                        ControlPoint final = controlPoints[controlPoints.Length - 1];
                        final.m_HitPosition = raw.m_HitPosition;
                        final.m_SnapPriority = best.m_SnapPriority;
                        controlPoints[controlPoints.Length - 1] = final;
                    }
                }

                // The guide line overlay only draws lines in m_SnapLines that the point lies on, so put the near ones back.
                for (int i = 0; i < s_NearLineCount; i++)
                    snapLines.Add(s_NearLines[i]);

                if (settings.DebugLogging)
                    DebugLog(reach, true, limit, s_NearLineCount, farthestOrigin, lineCount, directionCount, guideWins ? "snapped to a guide line within the reach" : "snapped without guide lines");
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
            return GetGuideLineReach(snapped, snapLines, out _, out _, out _);
        }

        /// <param name="farthestOrigin">Farthest origin among all guide lines through the point (the measure used before collinear lines were grouped).</param>
        /// <param name="lineCount">Number of guide lines through the point.</param>
        /// <param name="directionCount">Number of distinct directions among them (2 or more = an intersection).</param>
        private static float GetGuideLineReach(ControlPoint snapped, NativeList<SnapLine> snapLines, out float farthestOrigin, out int lineCount, out int directionCount)
        {
            float2 p = snapped.m_Position.xz;
            int count = 0;
            farthestOrigin = -1f;
            directionCount = 0;
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
                farthestOrigin = math.max(farthestOrigin, s_OnLineReaches[i]);

                // Nearest origin among the lines running along the same line as line i.
                float nearest = s_OnLineReaches[i];
                bool firstOfItsDirection = true;
                for (int j = 0; j < count; j++)
                {
                    if (math.abs(math.dot(s_OnLineDirections[i], s_OnLineDirections[j])) >= kSameLineDot)
                    {
                        nearest = math.min(nearest, s_OnLineReaches[j]);
                        if (j < i)
                            firstOfItsDirection = false;
                    }
                }
                if (firstOfItsDirection)
                    directionCount++;
                result = math.max(result, nearest);
            }
            lineCount = count;
            return result;
        }

        /// <summary>
        /// Saves the guide lines whose own snap position (the cursor projected onto the line) is within the reach.
        /// A distant line running along the same line as a near one is left out; the near one covers it.
        /// </summary>
        private static void SaveNearGuideLines(NativeList<SnapLine> snapLines, int limit)
        {
            s_NearLineCount = 0;
            for (int i = 0; i < snapLines.Length; i++)
            {
                SnapLine line = snapLines[i];
                if ((line.m_Flags & SnapLineFlags.GuideLine) == 0)
                    continue;
                if (math.distance(line.m_Curve.a.xz, line.m_ControlPoint.m_Position.xz) > limit)
                    continue;
                if (s_NearLines.Length <= s_NearLineCount)
                    Array.Resize(ref s_NearLines, s_NearLines.Length * 2);
                s_NearLines[s_NearLineCount++] = line;
            }
        }

        private static void RestoreRawPoints(NativeList<ControlPoint> controlPoints)
        {
            for (int i = 0; i < s_RawPointCount; i++)
                controlPoints[i] = s_RawPoints[i];
        }

        /// <summary>Runs SnapControlPoints again (without re-entering this patch) with the snap mask limited to keepMask.</summary>
        private static JobHandle RunSnapJob(NetToolSystem tool, JobHandle inputDeps, bool removeUpgrade, Snap keepMask)
        {
            ref Snap onMask = ref s_SnapOnMask(tool);
            Snap savedOnMask = onMask;
            onMask &= keepMask;
            s_InRerun = true;
            try
            {
                // The snap mask is copied into the job when it is scheduled, so it can be restored right after.
                return s_SnapControlPoints(tool, inputDeps, removeUpgrade);
            }
            finally
            {
                s_InRerun = false;
                onMask = savedOnMask;
            }
        }

        private static void DebugLog(float reach, bool rejected, int limit, int nearGuideLines, float farthestOrigin, int lineCount, int directionCount, string outcome)
        {
            float now = Time.realtimeSinceStartup;
            if (!rejected && math.abs(reach - s_LastDebugReach) < 1f && now - s_LastDebugLogTime < 0.5f)
                return;
            s_LastDebugLogTime = now;
            s_LastDebugReach = reach;
            Mod.log.Info($"Guide line snap: reach={reach:F1} m, limit={limit} m, {(rejected ? "REJECTED" : "kept")}, " +
                $"farthest origin={farthestOrigin:F1} m, lines through point={lineCount}, directions={directionCount}" +
                (rejected ? $", guide lines within limit={nearGuideLines}, result: {outcome}" : ""));
        }
    }
}
