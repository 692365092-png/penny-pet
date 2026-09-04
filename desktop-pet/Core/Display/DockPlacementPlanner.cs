using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Detached logical member size. Dock order is the order supplied by the
    // immutable group state; no window or repository object crosses Core.
    internal sealed class DockLogicalMember
    {
        internal DockLogicalMember(string noteId, int width, int height)
        {
            if (String.IsNullOrWhiteSpace(noteId))
                throw new ArgumentException("A note id is required.",
                    nameof(noteId));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width),
                    "Logical member size must be positive.");
            NoteId = noteId.Trim();
            Width = width;
            Height = height;
        }

        internal string NoteId { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
    }

    internal sealed class DockGroupLogicalState
    {
        private readonly DockLogicalMember[] _members;

        internal DockGroupLogicalState(LogicalPoint rootAnchor,
            IEnumerable<DockLogicalMember> members)
        {
            RootAnchor = rootAnchor;
            _members = members == null
                ? new DockLogicalMember[0]
                : new List<DockLogicalMember>(members).ToArray();
            if (_members.Length == 0)
                throw new ArgumentException(
                    "A Dock group needs at least one member.",
                    nameof(members));
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (DockLogicalMember member in _members)
            {
                if (member == null || !ids.Add(member.NoteId))
                    throw new ArgumentException(
                        "Dock members must be non-null and unique.",
                        nameof(members));
            }
            Members = Array.AsReadOnly(_members);
        }

        internal LogicalPoint RootAnchor { get; private set; }
        internal IReadOnlyList<DockLogicalMember> Members
            { get; private set; }
    }

    internal sealed class DockWindowTarget
    {
        internal DockWindowTarget(string noteId, PhysicalRect physicalBounds)
        {
            NoteId = noteId ?? String.Empty;
            PhysicalBounds = physicalBounds;
        }

        internal string NoteId { get; private set; }
        internal PhysicalRect PhysicalBounds { get; private set; }
    }

    // Desired state only. Actual HWND facts remain the effective truth after
    // a later native executor applies this immutable plan.
    internal sealed class DockPlacementPlan
    {
        private readonly DockWindowTarget[] _windowTargets;

        internal DockPlacementPlan(long topologyGeneration,
            long planSequence, string sourceNoteId, string targetSurfaceId,
            int targetDpi, IEnumerable<DockWindowTarget> windowTargets)
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

    internal static class DockPlacementPlanner
    {
        internal static DockPlacementPlan Plan(
            DockGroupLogicalState group, WindowFacts sourceFacts,
            DisplaySurfaceSnapshot targetSurface, int targetDpi,
            long topologyGeneration, long planSequence)
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

            double scale = targetDpi / 96.0;
            int logicalTop = group.RootAnchor.Y;
            List<DockWindowTarget> targets =
                new List<DockWindowTarget>(group.Members.Count);
            foreach (DockLogicalMember member in group.Members)
            {
                long nextTop = (long)logicalTop + member.Height;
                if (nextTop > Int32.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(group),
                        "The logical Dock stack is too tall.");
                long logicalRight = (long)group.RootAnchor.X + member.Width;
                if (logicalRight > Int32.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(group),
                        "The logical Dock member is too wide.");
                int left = ProjectEdge(targetSurface.Bounds.Left,
                    group.RootAnchor.X, scale);
                int right = ProjectEdge(targetSurface.Bounds.Left,
                    logicalRight, scale);
                int top = ProjectEdge(targetSurface.Bounds.Top,
                    logicalTop, scale);
                int bottom = ProjectEdge(targetSurface.Bounds.Top,
                    nextTop, scale);
                targets.Add(new DockWindowTarget(member.NoteId,
                    new PhysicalRect(left, top,
                        PositiveLength(left, right),
                        PositiveLength(top, bottom))));
                logicalTop = (int)nextTop;
            }

            return new DockPlacementPlan(topologyGeneration, planSequence,
                sourceFacts.WindowId, targetSurface.RuntimeSurfaceId,
                targetDpi, targets);
        }

        private static int ProjectEdge(int physicalOrigin,
            long logicalCoordinate, double scale)
        {
            double value = physicalOrigin + Math.Round(
                logicalCoordinate * scale,
                MidpointRounding.AwayFromZero);
            if (value <= Int32.MinValue) return Int32.MinValue;
            if (value >= Int32.MaxValue) return Int32.MaxValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static int PositiveLength(int start, int end)
        {
            long length = (long)end - start;
            return length <= 0 ? 1 :
                (length > Int32.MaxValue ? Int32.MaxValue : (int)length);
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
