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

    // Deterministic geometry only: no gesture, sequence, dispatcher or commit state.
    internal static class DockLayout
    {
        internal static LogicalPoint CenteredAnchor(DockGroupLogicalState group,
            DisplaySurfaceSnapshot targetSurface, int targetDpi)
        {
            int logicalWidth = 1;
            long logicalHeight = 0;
            foreach (DockLogicalMember member in group.Members)
            {
                logicalWidth = Math.Max(logicalWidth, member.Width);
                logicalHeight += member.Height;
            }
            double scale = targetDpi / 96.0;
            int physicalWidth = Math.Max(1, (int)Math.Min(Int32.MaxValue,
                Math.Round(logicalWidth * scale,
                    MidpointRounding.AwayFromZero)));
            int physicalHeight = Math.Max(1, (int)Math.Min(Int32.MaxValue,
                Math.Round(logicalHeight * scale,
                    MidpointRounding.AwayFromZero)));
            int left = targetSurface.WorkArea.Left + Math.Max(0,
                (targetSurface.WorkArea.Width - physicalWidth) / 2);
            int top = targetSurface.WorkArea.Top + Math.Max(0,
                (targetSurface.WorkArea.Height - physicalHeight) / 2);
            return DisplayGeometry.PhysicalToLocal(left, top,
                targetSurface.Bounds.Left, targetSurface.Bounds.Top,
                scale);
        }

        internal static List<DockWindowTarget> ProjectGroup(DockGroupLogicalState group,
            LogicalPoint anchor, DisplaySurfaceSnapshot targetSurface, int targetDpi)
        {
            double scale = targetDpi / 96.0;
            int logicalTop = anchor.Y;
            List<DockWindowTarget> targets =
                new List<DockWindowTarget>(group.Members.Count);
            foreach (DockLogicalMember member in group.Members)
            {
                long nextTop = (long)logicalTop + member.Height;
                if (nextTop > Int32.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(group),
                        "The logical Dock stack is too tall.");
                long logicalRight = (long)anchor.X + member.Width;
                if (logicalRight > Int32.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(group),
                        "The logical Dock member is too wide.");
                int left = ProjectEdge(targetSurface.Bounds.Left,
                    anchor.X, scale);
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

            return targets;
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

    }
}
