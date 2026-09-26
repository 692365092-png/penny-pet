using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Desired state only. Actual HWND facts remain the effective truth after
    // a later native executor applies this immutable plan.
    internal sealed class DockPlacementPlan
    {
        private readonly DockWindowTarget[] _windowTargets;

        internal DockPlacementPlan(long topologyGeneration,
            long planSequence, string sourceNoteId, string targetSurfaceId,
            int targetDpi, IEnumerable<DockWindowTarget> windowTargets,
            long interactionEpoch = 0, DockInput input = null)
        {
            TopologyGeneration = topologyGeneration;
            PlanSequence = planSequence;
            SourceNoteId = sourceNoteId ?? String.Empty;
            TargetSurfaceId = targetSurfaceId ?? String.Empty;
            TargetDpi = targetDpi;
            _windowTargets = windowTargets == null
                ? new DockWindowTarget[0]
                : new List<DockWindowTarget>(windowTargets).ToArray();
            WindowTargets = Array.AsReadOnly(_windowTargets);
        }

        internal long TopologyGeneration { get; private set; }
        internal long PlanSequence { get; private set; }
        internal string SourceNoteId { get; private set; }
        internal string TargetSurfaceId { get; private set; }
        internal int TargetDpi { get; private set; }
        internal IReadOnlyList<DockWindowTarget> WindowTargets
            { get; private set; }
    }

    // Whole-group placement intent. Normal requests contain logical geometry;
    // the recovery factory captures legacy pixels instead. These inputs are
    // mutually exclusive and live only until this request completes. The STA
    // supplies actual target DPI after parking every HWND on the target.
    internal sealed class DockGroupReprojectPlan
    {
        internal DockGroupReprojectPlan(long topologyGeneration,
            long planSequence, string targetSurfaceId,
            DockGroupLogicalState group, bool centerInWorkArea)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            TopologyGeneration = topologyGeneration;
            PlanSequence = planSequence;
            TargetSurfaceId = targetSurfaceId ?? String.Empty;
            Group = group;
            CenterInWorkArea = centerInWorkArea;
            var ids = new List<string>(group.Members.Count);
            foreach (DockLogicalMember member in group.Members) ids.Add(member.NoteId);
            MemberIds = ids.AsReadOnly();
        }

        private DockGroupReprojectPlan(long topologyGeneration, long planSequence,
            string targetSurfaceId, IList<DockWindowTarget> recoveryTargets)
        {
            TopologyGeneration = topologyGeneration;
            PlanSequence = planSequence;
            TargetSurfaceId = targetSurfaceId;
            RecoveryTargets = new List<DockWindowTarget>(recoveryTargets).AsReadOnly();
            var ids = new List<string>(recoveryTargets.Count);
            foreach (DockWindowTarget target in recoveryTargets) ids.Add(target.NoteId);
            MemberIds = ids.AsReadOnly();
        }

        internal static DockGroupReprojectPlan RecoverPhysical(long topologyGeneration,
            long planSequence, string targetSurfaceId, IList<DockWindowTarget> targets)
        {
            if (targets == null || targets.Count == 0) throw new ArgumentException("A recovery group is required.", nameof(targets));
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DockWindowTarget target in targets)
                if (target == null || String.IsNullOrWhiteSpace(target.NoteId) ||
                    !target.PhysicalBounds.IsValid || !ids.Add(target.NoteId))
                    throw new ArgumentException("Recovery members must have unique ids and valid bounds.", nameof(targets));
            return new DockGroupReprojectPlan(topologyGeneration, planSequence, targetSurfaceId, targets);
        }

        internal long TopologyGeneration { get; private set; }
        internal long PlanSequence { get; private set; }
        internal string TargetSurfaceId { get; private set; }
        internal DockGroupLogicalState Group { get; private set; }
        internal bool CenterInWorkArea { get; private set; }
        internal IReadOnlyList<string> MemberIds { get; private set; }
        internal IReadOnlyList<DockWindowTarget> RecoveryTargets { get; private set; }
    }

    internal static class DockPlacementPlanner
    {
        internal static DockPlacementPlan PlanReproject(
            DockGroupReprojectPlan request,
            DisplaySurfaceSnapshot targetSurface, int targetDpi)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (targetSurface == null)
                throw new ArgumentNullException(nameof(targetSurface));
            if (request.TopologyGeneration < 0 || targetDpi <= 0 ||
                !String.Equals(request.TargetSurfaceId,
                    targetSurface.RuntimeSurfaceId,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "The topology reproject target is invalid.",
                    nameof(request));

            if (request.RecoveryTargets != null)
                return new DockPlacementPlan(request.TopologyGeneration, request.PlanSequence,
                    String.Empty, request.TargetSurfaceId, targetDpi, request.RecoveryTargets);

            DockGroupLogicalState group = request.Group;
            LogicalPoint anchor = group.RootAnchor;
            if (request.CenterInWorkArea)
            {
                anchor = DockLayout.CenteredAnchor(group, targetSurface, targetDpi);
            }

            return new DockPlacementPlan(request.TopologyGeneration,
                request.PlanSequence, String.Empty, request.TargetSurfaceId,
                targetDpi, DockLayout.ProjectGroup(group, anchor, targetSurface, targetDpi));
        }

        internal static DockPlacementPlan Plan(
            DockGroupLogicalState group, WindowFacts sourceFacts,
            DisplaySurfaceSnapshot targetSurface, int targetDpi,
            long topologyGeneration, long planSequence,
            long interactionEpoch = 0, DockInput input = null)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            if (sourceFacts == null)
                throw new ArgumentNullException(nameof(sourceFacts));
            if (targetSurface == null)
                throw new ArgumentNullException(nameof(targetSurface));
            if (targetDpi <= 0 || sourceFacts.Dpi != targetDpi)
                throw new ArgumentException(
                    "The source window DPI must be the target DPI.",
                    nameof(targetDpi));
            if (sourceFacts.TopologyGeneration != topologyGeneration)
                throw new ArgumentException(
                    "Source facts and plan must use one topology generation.",
                    nameof(topologyGeneration));
            if (!IsSourceOnTarget(sourceFacts, targetSurface))
                throw new ArgumentException(
                    "The source window must be on the target surface.",
                    nameof(targetSurface));

            bool containsSource = false;
            foreach (DockLogicalMember member in group.Members)
                if (String.Equals(member.NoteId, sourceFacts.WindowId,
                    StringComparison.OrdinalIgnoreCase)) containsSource = true;
            if (!containsSource)
                throw new ArgumentException(
                    "The source window must belong to the Dock group.",
                    nameof(sourceFacts));

            return new DockPlacementPlan(topologyGeneration, planSequence,
                sourceFacts.WindowId, targetSurface.RuntimeSurfaceId,
                targetDpi, DockLayout.ProjectGroup(group, group.RootAnchor, targetSurface, targetDpi),
                interactionEpoch, input);
        }

        private static bool IsSourceOnTarget(WindowFacts sourceFacts,
            DisplaySurfaceSnapshot targetSurface)
        {
            if (String.Equals(sourceFacts.RuntimeGdiName,
                targetSurface.RuntimeGdiName,
                StringComparison.OrdinalIgnoreCase)) return true;
            foreach (DisplayTargetIdentity target in targetSurface.Targets)
                if (String.Equals(sourceFacts.ActiveTargetKey,
                    target.StableKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
