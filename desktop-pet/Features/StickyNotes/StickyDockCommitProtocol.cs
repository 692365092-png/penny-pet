using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PennyPet
{
    internal enum StickyDockCommitIntent
    {
        Move,
        Detach,
        MergeAfter,
        HorizontalResize,
        DividerResize
    }

    internal sealed class StickyDockGestureCommit
    {
        private readonly DockBatchMemberResult[] _members;
        private readonly Dictionary<string, long> _baselineVersions;

        internal StickyDockGestureCommit(long gestureId,
            long dependsOnGestureId, StickyDockCommitIntent intent,
            string sourceNoteId, string targetNoteId,
            long topologyGeneration,
            IDictionary<string, long> baselineVersions,
            IEnumerable<DockBatchMemberResult> members)
        {
            if (gestureId <= 0)
                throw new ArgumentOutOfRangeException(nameof(gestureId));
            if (dependsOnGestureId < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(dependsOnGestureId));
            GestureId = gestureId;
            DependsOnGestureId = dependsOnGestureId;
            Intent = intent;
            SourceNoteId = sourceNoteId ?? String.Empty;
            TargetNoteId = targetNoteId ?? String.Empty;
            TopologyGeneration = topologyGeneration;
            _baselineVersions = baselineVersions == null
                ? new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, long>(
                    baselineVersions,
                    StringComparer.OrdinalIgnoreCase);
            BaselineVersions =
                new ReadOnlyDictionary<string, long>(
                    _baselineVersions);
            _members = members == null
                ? new DockBatchMemberResult[0]
                : new List<DockBatchMemberResult>(members).ToArray();
            Members = Array.AsReadOnly(_members);
        }

        internal long GestureId { get; private set; }
        internal long DependsOnGestureId { get; private set; }
        internal StickyDockCommitIntent Intent { get; private set; }
        internal string SourceNoteId { get; private set; }
        internal string TargetNoteId { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal IReadOnlyDictionary<string, long> BaselineVersions
            { get; private set; }
        internal IReadOnlyList<DockBatchMemberResult> Members
            { get; private set; }

        internal StickyDockGestureCommit RebaseBaselineVersions(
            StickyDockSceneProjection scene)
        {
            if (scene == null) return this;
            Dictionary<string, long> rebased =
                new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (string noteId in _baselineVersions.Keys)
            {
                long version = scene.VersionOf(noteId);
                rebased[noteId] = version == Int64.MinValue
                    ? _baselineVersions[noteId] : version;
            }
            return new StickyDockGestureCommit(
                GestureId, DependsOnGestureId, Intent,
                SourceNoteId, TargetNoteId,
                TopologyGeneration, rebased, _members);
        }
    }

    internal sealed class StickyDockCommitAck
    {
        internal StickyDockCommitAck(long gestureId, bool accepted,
            StickyDockSceneProjection scene = null)
        {
            if (gestureId <= 0)
                throw new ArgumentOutOfRangeException(nameof(gestureId));
            GestureId = gestureId;
            Accepted = accepted;
            Scene = scene;
        }

        internal long GestureId { get; private set; }
        internal bool Accepted { get; private set; }
        internal StickyDockSceneProjection Scene { get; private set; }
    }

    // Sticky-side handoff state. Active native gestures live elsewhere; this
    // object owns only completed results awaiting Pet acknowledgement.
    // Therefore ACK(A) can never overwrite the live geometry of B.
    internal sealed class StickyDockCommitQueue
    {
        private const int MaxPending = 8;
        private readonly List<Pending> _pending =
            new List<Pending>();

        internal int PendingCount { get { return _pending.Count; } }
        internal bool CanBeginGesture
        {
            get { return _pending.Count < MaxPending; }
        }
        internal long LatestPendingGestureId
        {
            get
            {
                return _pending.Count == 0
                    ? 0
                    : _pending[_pending.Count - 1].Commit.GestureId;
            }
        }

        internal bool TryAdd(StickyDockGestureCommit commit)
        {
            if (commit == null || !CanBeginGesture)
                return false;
            foreach (Pending pending in _pending)
                if (pending.Commit.GestureId == commit.GestureId)
                    return false;
            if (commit.DependsOnGestureId != 0 &&
                !Contains(commit.DependsOnGestureId))
                return false;
            _pending.Add(new Pending(commit));
            return true;
        }

        internal StickyDockGestureCommit PeekReady()
        {
            foreach (Pending pending in _pending)
            {
                if (pending.Sent) continue;
                if (pending.Commit.DependsOnGestureId != 0 &&
                    Contains(pending.Commit.DependsOnGestureId))
                    return null;
                pending.Sent = true;
                return pending.Commit;
            }
            return null;
        }

        internal StickyDockCommitResolution Acknowledge(
            StickyDockCommitAck ack)
        {
            if (ack == null)
                return StickyDockCommitResolution.Ignored();
            int index = IndexOf(ack.GestureId);
            if (index < 0)
                return StickyDockCommitResolution.Ignored();

            Pending resolved = _pending[index];
            List<long> cancelled = new List<long>();
            _pending.RemoveAt(index);
            if (ack.Accepted && ack.Scene != null)
            {
                foreach (Pending pending in _pending)
                    if (!pending.Sent &&
                        pending.Commit.DependsOnGestureId ==
                            resolved.Commit.GestureId)
                        pending.Commit =
                            pending.Commit.RebaseBaselineVersions(
                                ack.Scene);
            }
            if (!ack.Accepted)
            {
                // Only descendants depend on the rejected local semantic
                // projection. Independent older/newer work remains intact.
                bool changed;
                do
                {
                    changed = false;
                    for (int i = _pending.Count - 1; i >= 0; i--)
                    {
                        long dependency =
                            _pending[i].Commit.DependsOnGestureId;
                        if (dependency == resolved.Commit.GestureId ||
                            cancelled.Contains(dependency))
                        {
                            cancelled.Add(
                                _pending[i].Commit.GestureId);
                            _pending.RemoveAt(i);
                            changed = true;
                        }
                    }
                } while (changed);
            }
            return new StickyDockCommitResolution(true,
                ack.Accepted, resolved.Commit.GestureId,
                cancelled.AsReadOnly(), ack.Scene);
        }

        internal void Clear()
        {
            _pending.Clear();
        }

        private bool Contains(long gestureId)
        {
            return IndexOf(gestureId) >= 0;
        }

        private int IndexOf(long gestureId)
        {
            for (int index = 0; index < _pending.Count; index++)
                if (_pending[index].Commit.GestureId == gestureId)
                    return index;
            return -1;
        }

        private sealed class Pending
        {
            internal Pending(StickyDockGestureCommit commit)
            {
                Commit = commit;
            }

            internal StickyDockGestureCommit Commit;
            internal bool Sent;
        }
    }

    internal sealed class StickyDockCommitResolution
    {
        internal StickyDockCommitResolution(bool matched,
            bool accepted, long gestureId,
            IReadOnlyList<long> cancelledDependents,
            StickyDockSceneProjection scene)
        {
            Matched = matched;
            Accepted = accepted;
            GestureId = gestureId;
            CancelledDependents = cancelledDependents ??
                Array.AsReadOnly(new long[0]);
            Scene = scene;
        }

        internal bool Matched { get; private set; }
        internal bool Accepted { get; private set; }
        internal long GestureId { get; private set; }
        internal IReadOnlyList<long> CancelledDependents
            { get; private set; }
        internal StickyDockSceneProjection Scene { get; private set; }

        internal static StickyDockCommitResolution Ignored()
        {
            return new StickyDockCommitResolution(false, false, 0,
                Array.AsReadOnly(new long[0]), null);
        }
    }
}
