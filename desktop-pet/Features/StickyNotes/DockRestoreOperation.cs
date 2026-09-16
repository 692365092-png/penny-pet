using System;
using System.Collections.Generic;
using System.Threading;

namespace PennyPet
{
    internal enum DockTopologyReprojectReason
    {
        CurrentRuntimeRepair,
        PreferredReturn,
        TemporaryRehome,
        RestorePreferred,
        LegacyRecovery
    }

    // One detached restore intent. Pet owns its lifetime; the STA reads only
    // snapshots, the placement request, and the cancellation token.
    internal sealed class DockRestoreOperation
    {
        private readonly StickyNoteData[] _originals;
        private readonly int[] _orders;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private DockRestoreOperation(IList<StickyNoteData> ordered, string focusId,
            bool focusEditor, bool persistVisibility, DisplayTopologySnapshot topology,
            DisplaySurfaceSnapshot target, DockTopologyReprojectReason reason, DockGroupReprojectPlan plan)
        {
            _originals = new List<StickyNoteData>(ordered).ToArray();
            _orders = new int[ordered.Count];
            var snapshots = new List<StickyNoteUiSnapshot>(ordered.Count);
            for (int index = 0; index < ordered.Count; index++)
            {
                _orders[index] = ordered[index].DockGroupOrder;
                snapshots.Add(StickyNoteUiSnapshot.Capture(ordered[index], alwaysOnTop: ordered[0].AlwaysOnTop));
            }
            GroupId = ordered[0].DockGroupId;
            FocusId = focusId;
            FocusEditor = focusEditor;
            PersistVisibility = persistVisibility;
            Topology = topology;
            Target = target;
            Reason = reason;
            Plan = plan;
            Snapshots = snapshots.AsReadOnly();
            MemberIds = plan.MemberIds;
            Cancellation = _cancellation.Token;
        }

        internal string GroupId { get; private set; }
        internal string FocusId { get; private set; }
        internal bool FocusEditor { get; private set; }
        internal bool PersistVisibility { get; private set; }
        internal DisplayTopologySnapshot Topology { get; private set; }
        internal DisplaySurfaceSnapshot Target { get; private set; }
        internal DockTopologyReprojectReason Reason { get; private set; }
        internal DockGroupReprojectPlan Plan { get; private set; }
        internal IReadOnlyList<StickyNoteUiSnapshot> Snapshots { get; private set; }
        internal IReadOnlyList<string> MemberIds { get; private set; }
        internal CancellationToken Cancellation { get; private set; }

        internal static DockRestoreOperation TryCreate(IList<StickyNoteData> ordered,
            string focusId, bool focusEditor, bool persistVisibility,
            DisplayTopologySnapshot topology, WindowFacts petFacts, long planSequence)
        {
            if (ordered == null || ordered.Count < 2 || topology == null ||
                ordered[0] == null || String.IsNullOrWhiteSpace(ordered[0].DockGroupId)) return null;
            StickyNoteData root = ordered[0];
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData member in ordered)
            {
                if (member == null || String.IsNullOrWhiteSpace(member.Id) || !ids.Add(member.Id) ||
                    !String.Equals(root.DockGroupId, member.DockGroupId, StringComparison.OrdinalIgnoreCase)) return null;
            }
            if (focusEditor && !ids.Contains(focusId ?? String.Empty)) return null;
            if (!HasCompletePreferred(ordered))
            {
                DockGroupReprojectPlan recovery = StickyPlacementRecovery.SelectDockPhysical(ordered, topology, planSequence);
                return new DockRestoreOperation(ordered, focusId, focusEditor, persistVisibility,
                    topology, topology.FindByRuntimeSurfaceId(recovery.TargetSurfaceId),
                    DockTopologyReprojectReason.LegacyRecovery, recovery);
            }
            var members = new List<DockLogicalMember>(ordered.Count);
            foreach (StickyNoteData member in ordered)
                members.Add(new DockLogicalMember(member.Id, root.PreferredPlacement.LocalLogicalRect.Width,
                    member.PreferredPlacement.LocalLogicalRect.Height));
            DisplaySurfaceSnapshot target = FindCommonPreferredSurface(ordered, topology);
            DockTopologyReprojectReason reason = DockTopologyReprojectReason.RestorePreferred;
            if (target == null)
            {
                target = FallbackDisplayPolicy.ResolveFallbackSurface(topology, root.PreferredPlacement.PreferredTargetKey,
                    new PhysicalRect(root.X, root.Y, root.Width, root.Height),
                    petFacts == null ? String.Empty : petFacts.RuntimeGdiName);
                reason = DockTopologyReprojectReason.TemporaryRehome;
            }
            if (target == null) return null;
            var logical = new DockGroupLogicalState(new LogicalPoint {
                X = root.PreferredPlacement.LocalLogicalRect.X, Y = root.PreferredPlacement.LocalLogicalRect.Y }, members);
            var plan = new DockGroupReprojectPlan(topology.Generation, planSequence, target.RuntimeSurfaceId,
                logical, reason == DockTopologyReprojectReason.TemporaryRehome);
            return new DockRestoreOperation(ordered, focusId, focusEditor, persistVisibility, topology, target, reason, plan);
        }

        internal static bool HasCompletePreferred(IList<StickyNoteData> group)
        {
            if (group == null || group.Count < 2) return false;
            foreach (StickyNoteData note in group)
                if (note?.PreferredPlacement == null) return false;
            return true;
        }

        internal static DisplaySurfaceSnapshot FindCommonPreferredSurface(
            IList<StickyNoteData> group, DisplayTopologySnapshot topology)
        {
            if (!HasCompletePreferred(group) || topology == null) return null;
            DisplaySurfaceSnapshot target = null;
            foreach (StickyNoteData note in group)
            {
                DisplaySurfaceSnapshot surface = topology.FindByTargetKey(note.PreferredPlacement.PreferredTargetKey);
                if (surface == null || (target != null && !String.Equals(target.RuntimeSurfaceId,
                    surface.RuntimeSurfaceId, StringComparison.OrdinalIgnoreCase))) return null;
                target = surface;
            }
            return target;
        }

        // Editing content does not invalidate placement. Replacement, hide,
        // removal, reordering or regrouping does invalidate this captured intent.
        internal bool MatchesMembers(IList<StickyNoteData> ordered)
        {
            if (Cancellation.IsCancellationRequested || ordered == null || ordered.Count != _originals.Length) return false;
            for (int index = 0; index < ordered.Count; index++)
            {
                StickyNoteData note = ordered[index];
                if (!ReferenceEquals(note, _originals[index]) || note.DockGroupOrder != _orders[index] ||
                    note.Visible != Snapshots[index].Visible ||
                    !String.Equals(note.DockGroupId, GroupId, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        internal bool ContainsMember(string noteId)
        {
            foreach (string id in MemberIds)
                if (String.Equals(id, noteId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal void End()
        {
            if (Cancellation.IsCancellationRequested) return;
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }

    // The registry contains the operation itself, not a second busy flag.
    internal sealed class DockRestoreOperations
    {
        private readonly Dictionary<string, DockRestoreOperation> _active =
            new Dictionary<string, DockRestoreOperation>(StringComparer.OrdinalIgnoreCase);

        internal bool ContainsGroup(string groupId)
        { return groupId != null && _active.ContainsKey(groupId); }

        internal bool TryBegin(DockRestoreOperation operation)
        {
            if (operation == null || operation.Cancellation.IsCancellationRequested || ContainsGroup(operation.GroupId)) return false;
            _active.Add(operation.GroupId, operation);
            return true;
        }

        internal bool IsCurrent(DockRestoreOperation operation)
        {
            DockRestoreOperation current;
            return operation != null && _active.TryGetValue(operation.GroupId, out current) && ReferenceEquals(current, operation);
        }

        internal bool Finish(DockRestoreOperation operation)
        {
            if (!IsCurrent(operation)) return false;
            _active.Remove(operation.GroupId);
            operation.End();
            return true;
        }

        internal DockRestoreOperation[] Snapshot()
        { return new List<DockRestoreOperation>(_active.Values).ToArray(); }
    }
}
