using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PennyPet
{
    internal enum StickyDockLocalGestureKind
    {
        HeaderDrag,
        HorizontalResize,
        DividerResize
    }

    // Detached model projection. It carries membership/order only; live
    // geometry always comes from Sticky-owned HWND sessions.
    internal sealed class StickyDockSceneMember
    {
        internal StickyDockSceneMember(string noteId, string groupId,
            int groupOrder, bool visible, long dockVersion = 0)
        {
            if (String.IsNullOrWhiteSpace(noteId))
                throw new ArgumentException("A note id is required.",
                    nameof(noteId));
            NoteId = noteId.Trim();
            GroupId = (groupId ?? String.Empty).Trim();
            GroupOrder = groupOrder;
            Visible = visible;
            DockVersion = dockVersion;
        }

        internal string NoteId { get; private set; }
        internal string GroupId { get; private set; }
        internal int GroupOrder { get; private set; }
        internal bool Visible { get; private set; }
        internal long DockVersion { get; private set; }
    }

    internal sealed class StickyDockSceneProjection
    {
        private readonly StickyDockSceneMember[] _members;
        private readonly Dictionary<string, StickyDockSceneMember> _byId;

        internal StickyDockSceneProjection(
            IEnumerable<StickyDockSceneMember> members, long revision)
        {
            _members = members == null
                ? new StickyDockSceneMember[0]
                : new List<StickyDockSceneMember>(members).ToArray();
            _byId = new Dictionary<string, StickyDockSceneMember>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyDockSceneMember member in _members)
            {
                if (member == null || _byId.ContainsKey(member.NoteId))
                    throw new ArgumentException(
                        "Dock scene members must be non-null and unique.",
                        nameof(members));
                _byId.Add(member.NoteId, member);
            }
            Members = Array.AsReadOnly(_members);
            Revision = revision;
        }

        internal IReadOnlyList<StickyDockSceneMember> Members
            { get; private set; }
        internal long Revision { get; private set; }

        internal IReadOnlyList<string> VisibleGroup(string noteId)
        {
            StickyDockSceneMember source;
            if (String.IsNullOrWhiteSpace(noteId) ||
                !_byId.TryGetValue(noteId, out source) ||
                !source.Visible)
                return Array.AsReadOnly(new string[0]);

            if (String.IsNullOrEmpty(source.GroupId))
                return Array.AsReadOnly(new[] { source.NoteId });

            List<StickyDockSceneMember> group =
                new List<StickyDockSceneMember>();
            foreach (StickyDockSceneMember member in _members)
                if (member.Visible &&
                    String.Equals(member.GroupId, source.GroupId,
                        StringComparison.OrdinalIgnoreCase))
                    group.Add(member);
            group.Sort(delegate(StickyDockSceneMember left,
                StickyDockSceneMember right)
            {
                int order = left.GroupOrder.CompareTo(right.GroupOrder);
                return order != 0 ? order :
                    StringComparer.OrdinalIgnoreCase.Compare(
                        left.NoteId, right.NoteId);
            });
            List<string> ids = new List<string>(group.Count);
            foreach (StickyDockSceneMember member in group)
                ids.Add(member.NoteId);
            return ids.AsReadOnly();
        }

        internal IReadOnlyList<string> VisibleNoteIds()
        {
            List<string> ids = new List<string>();
            foreach (StickyDockSceneMember member in _members)
                if (member.Visible) ids.Add(member.NoteId);
            return ids.AsReadOnly();
        }

        internal long VersionOf(string noteId)
        {
            StickyDockSceneMember member;
            return !String.IsNullOrWhiteSpace(noteId) &&
                _byId.TryGetValue(noteId, out member)
                ? member.DockVersion : Int64.MinValue;
        }

        internal IReadOnlyList<string> AffectedIds(
            string sourceNoteId, string targetNoteId)
        {
            List<string> ids = new List<string>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string id in VisibleGroup(sourceNoteId))
                if (seen.Add(id)) ids.Add(id);
            if (!String.IsNullOrWhiteSpace(targetNoteId))
                foreach (string id in VisibleGroup(targetNoteId))
                    if (seen.Add(id)) ids.Add(id);
            return ids.AsReadOnly();
        }
    }

    // R22 candidate runtime. It is deliberately model/repository agnostic:
    // no Pet callback, save, dispatcher or mailbox is available here.
    // R23 will attach its completion object to the commit/ack protocol.
    internal sealed class StickyDockLocalGestureRuntime
    {
        private readonly Func<string, WindowFacts> _captureFacts;
        private readonly Action<IReadOnlyList<DockWindowTarget>, string>
            _applyFollowers;
        private StickyDockSceneProjection _scene;
        private DisplayTopologySnapshot _topology;
        private LocalGesture _active;
        private long _nextGestureId;
        private long _nextPlanSequence;

        internal StickyDockLocalGestureRuntime(
            Func<string, WindowFacts> captureFacts,
            Action<IReadOnlyList<DockWindowTarget>, string> applyFollowers)
        {
            _captureFacts = captureFacts ??
                throw new ArgumentNullException(nameof(captureFacts));
            _applyFollowers = applyFollowers ??
                throw new ArgumentNullException(nameof(applyFollowers));
        }

        internal bool IsActive { get { return _active != null; } }
        internal string LastSnapTargetNoteId { get; private set; }
        internal string SplitGuideParentNoteId
        {
            get
            {
                LocalGesture gesture = _active;
                return gesture != null &&
                    gesture.Kind == StickyDockLocalGestureKind.HeaderDrag &&
                    gesture.SplitEligible
                    ? gesture.MemberIds[gesture.SourceIndex - 1]
                    : String.Empty;
            }
        }

        internal void SetScene(StickyDockSceneProjection scene)
        {
            _scene = scene;
        }

        internal void SetTopology(DisplayTopologySnapshot topology)
        {
            _topology = topology;
        }

        internal long TryBegin(StickyDockLocalGestureKind kind,
            string sourceNoteId)
        {
            if (_active != null || _scene == null || _topology == null)
                return 0;
            IReadOnlyList<string> ids =
                _scene.VisibleGroup(sourceNoteId);
            if (ids.Count == 0 ||
                (kind != StickyDockLocalGestureKind.HeaderDrag &&
                    ids.Count < 2))
                return 0;

            List<WindowFacts> facts = new List<WindowFacts>(ids.Count);
            int sourceIndex = -1;
            for (int index = 0; index < ids.Count; index++)
            {
                WindowFacts current = _captureFacts(ids[index]);
                if (current == null ||
                    current.TopologyGeneration != _topology.Generation ||
                    !current.PhysicalBounds.IsValid)
                    return 0;
                if (String.Equals(current.WindowId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase))
                    sourceIndex = index;
                facts.Add(current);
            }
            if (sourceIndex < 0 ||
                (kind == StickyDockLocalGestureKind.DividerResize &&
                    sourceIndex == facts.Count - 1))
                return 0;

            long gestureId = ++_nextGestureId;
            if (gestureId <= 0)
                gestureId = _nextGestureId = 1;
            _active = new LocalGesture(gestureId, kind,
                sourceIndex, facts.ToArray(), _scene.Revision,
                _topology.Generation);
            LastSnapTargetNoteId = String.Empty;
            return gestureId;
        }

        internal bool MoveHeader(WindowFacts sourceFacts)
        {
            LocalGesture gesture = _active;
            if (gesture == null ||
                gesture.Kind != StickyDockLocalGestureKind.HeaderDrag ||
                !gesture.MatchesSource(sourceFacts, _topology))
                return false;

            gesture.EvaluateSplit(sourceFacts, DateTime.UtcNow);
            if (gesture.Detached)
            {
                LastSnapTargetNoteId =
                    FindSnapTarget(sourceFacts, gesture);
                return true;
            }

            DockGroupLogicalState group;
            if (!StickyPlacementRules.TryBuildLiveDockState(
                gesture.Baseline, sourceFacts, _topology, out group))
                return false;

            DisplaySurfaceSnapshot surface =
                _topology.FindByRuntimeGdiName(
                    sourceFacts.RuntimeGdiName) ??
                _topology.FindByTargetKey(sourceFacts.ActiveTargetKey);
            if (surface == null) return false;

            DockPlacementPlan plan;
            try
            {
                plan = DockPlacementPlanner.Plan(group, sourceFacts,
                    surface, sourceFacts.Dpi,
                    sourceFacts.TopologyGeneration,
                    ++_nextPlanSequence);
            }
            catch (ArgumentException)
            {
                return false;
            }
            bool applied = ApplyFollowers(plan.WindowTargets,
                gesture.SourceNoteId);
            LastSnapTargetNoteId = applied
                ? FindSnapTarget(sourceFacts, gesture)
                : String.Empty;
            return applied;
        }

        private string FindSnapTarget(WindowFacts sourceFacts,
            LocalGesture gesture)
        {
            HashSet<string> active = new HashSet<string>(
                gesture.ActiveMemberIds,
                StringComparer.OrdinalIgnoreCase);
            List<DockWindowTarget> candidates =
                new List<DockWindowTarget>();
            foreach (string noteId in _scene.VisibleNoteIds())
            {
                if (active.Contains(noteId)) continue;
                WindowFacts facts = _captureFacts(noteId);
                if (facts == null ||
                    facts.TopologyGeneration !=
                        sourceFacts.TopologyGeneration ||
                    !facts.PhysicalBounds.IsValid)
                    continue;
                candidates.Add(new DockWindowTarget(
                    noteId, facts.PhysicalBounds));
            }
            return StickyDockOperations.FindSnapTarget(
                new DockWindowTarget(sourceFacts.WindowId,
                    sourceFacts.PhysicalBounds),
                candidates, 20) ?? String.Empty;
        }

        internal bool ResizeHorizontal(int proposedLeft,
            int proposedWidth)
        {
            LocalGesture gesture = _active;
            if (gesture == null ||
                gesture.Kind !=
                    StickyDockLocalGestureKind.HorizontalResize ||
                proposedWidth <= 0)
                return false;
            List<DockRect> layout =
                StickyDockGeometry.CalculateHorizontalResizeTargets(
                    gesture.StartBounds, gesture.SourceIndex,
                    proposedLeft, proposedWidth);
            return ApplyFollowers(
                gesture.TargetsFromLayout(layout, false),
                gesture.SourceNoteId);
        }

        internal bool ResizeDivider(int proposedSourceHeight)
        {
            LocalGesture gesture = _active;
            if (gesture == null ||
                gesture.Kind != StickyDockLocalGestureKind.DividerResize ||
                proposedSourceHeight <= 0)
                return false;
            List<DockRect> layout =
                StickyDockGeometry
                    .CalculateDockMemberResizeTargetsExact(
                        gesture.StartBounds, gesture.SourceIndex,
                        proposedSourceHeight);
            return ApplyFollowers(
                gesture.TargetsFromLayout(layout, true),
                gesture.SourceNoteId);
        }

        internal StickyDockLocalGestureCompletion Complete()
        {
            LocalGesture gesture = _active;
            _active = null;
            if (gesture == null)
            {
                LastSnapTargetNoteId = String.Empty;
                return null;
            }

            string target = gesture.Kind ==
                    StickyDockLocalGestureKind.HeaderDrag
                ? LastSnapTargetNoteId : String.Empty;
            StickyDockCommitIntent intent =
                gesture.Kind ==
                    StickyDockLocalGestureKind.HorizontalResize
                    ? StickyDockCommitIntent.HorizontalResize
                : gesture.Kind ==
                    StickyDockLocalGestureKind.DividerResize
                    ? StickyDockCommitIntent.DividerResize
                : gesture.Detached
                    ? StickyDockCommitIntent.Detach
                : !String.IsNullOrEmpty(target)
                    ? StickyDockCommitIntent.MergeAfter
                    : StickyDockCommitIntent.Move;

            IReadOnlyList<string> affected =
                _scene.AffectedIds(
                    gesture.SourceNoteId, target);
            Dictionary<string, long> versions =
                new Dictionary<string, long>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (string id in affected)
                versions[id] = _scene.VersionOf(id);

            LastSnapTargetNoteId = String.Empty;
            return new StickyDockLocalGestureCompletion(
                gesture.GestureId, gesture.Kind,
                intent, gesture.SourceNoteId, target,
                gesture.SceneRevision,
                gesture.TopologyGeneration,
                gesture.MemberIds, affected, versions);
        }

        internal void Cancel()
        {
            _active = null;
            LastSnapTargetNoteId = String.Empty;
        }

        private bool ApplyFollowers(
            IReadOnlyList<DockWindowTarget> targets,
            string sourceNoteId)
        {
            if (targets == null) return false;
            List<DockWindowTarget> followers =
                new List<DockWindowTarget>(targets.Count);
            foreach (DockWindowTarget target in targets)
                if (target != null &&
                    !String.Equals(target.NoteId, sourceNoteId,
                        StringComparison.OrdinalIgnoreCase))
                    followers.Add(target);
            _applyFollowers(followers.AsReadOnly(), sourceNoteId);
            return true;
        }

        private sealed class LocalGesture
        {
            internal LocalGesture(long gestureId,
                StickyDockLocalGestureKind kind, int sourceIndex,
                WindowFacts[] baseline, long sceneRevision,
                long topologyGeneration)
            {
                GestureId = gestureId;
                Kind = kind;
                SourceIndex = sourceIndex;
                Baseline = baseline;
                SceneRevision = sceneRevision;
                TopologyGeneration = topologyGeneration;
                SourceNoteId = baseline[sourceIndex].WindowId;
                StartBounds = new DockRect[baseline.Length];
                string[] ids = new string[baseline.Length];
                for (int index = 0; index < baseline.Length; index++)
                {
                    WindowFacts facts = baseline[index];
                    PhysicalRect rect = facts.PhysicalBounds;
                    StartBounds[index] = new DockRect(
                        rect.Left, rect.Top, rect.Width, rect.Height);
                    ids[index] = facts.WindowId;
                }
                MemberIds = Array.AsReadOnly(ids);
                StartedUtc = DateTime.UtcNow;
                SplitEligible = kind ==
                    StickyDockLocalGestureKind.HeaderDrag &&
                    sourceIndex > 0;
            }

            internal long GestureId { get; private set; }
            internal StickyDockLocalGestureKind Kind { get; private set; }
            internal int SourceIndex { get; private set; }
            internal WindowFacts[] Baseline { get; private set; }
            internal DockRect[] StartBounds { get; private set; }
            internal string SourceNoteId { get; private set; }
            internal long SceneRevision { get; private set; }
            internal long TopologyGeneration { get; private set; }
            internal IReadOnlyList<string> MemberIds { get; private set; }
            internal DateTime StartedUtc { get; private set; }
            internal bool SplitEligible { get; private set; }
            internal bool Detached { get; private set; }
            internal IReadOnlyList<string> ActiveMemberIds
            {
                get
                {
                    return Detached
                        ? Array.AsReadOnly(new[] { SourceNoteId })
                        : MemberIds;
                }
            }

            internal void EvaluateSplit(WindowFacts facts,
                DateTime nowUtc)
            {
                if (!SplitEligible || Detached ||
                    facts == null) return;
                PhysicalRect start =
                    Baseline[SourceIndex].PhysicalBounds;
                PhysicalRect current = facts.PhysicalBounds;
                double held =
                    (nowUtc - StartedUtc).TotalMilliseconds;
                if (StickyDockOperations.CancelsDockSplitHold(
                    held, current.Left - start.Left,
                    current.Top - start.Top))
                {
                    SplitEligible = false;
                    return;
                }
                if (held >=
                    StickyDockOperations.SplitHoldMilliseconds)
                {
                    Detached = true;
                    SplitEligible = false;
                }
            }

            internal bool MatchesSource(WindowFacts facts,
                DisplayTopologySnapshot topology)
            {
                return facts != null && topology != null &&
                    facts.TopologyGeneration == TopologyGeneration &&
                    topology.Generation == TopologyGeneration &&
                    String.Equals(facts.WindowId, SourceNoteId,
                        StringComparison.OrdinalIgnoreCase);
            }

            internal IReadOnlyList<DockWindowTarget> TargetsFromLayout(
                IList<DockRect> layout, bool tailOnly)
            {
                List<DockWindowTarget> targets =
                    new List<DockWindowTarget>();
                for (int index = 0; index < layout.Count; index++)
                {
                    int memberIndex = tailOnly
                        ? SourceIndex + index + 1
                        : (index < SourceIndex
                            ? index : index + 1);
                    DockRect rect = layout[index];
                    targets.Add(new DockWindowTarget(
                        Baseline[memberIndex].WindowId,
                        new PhysicalRect(rect.Left, rect.Top,
                            rect.Width, rect.Height)));
                }
                return targets.AsReadOnly();
            }
        }
    }

    internal sealed class StickyDockLocalGestureCompletion
    {
        internal StickyDockLocalGestureCompletion(long gestureId,
            StickyDockLocalGestureKind kind,
            StickyDockCommitIntent intent,
            string sourceNoteId, string targetNoteId,
            long sceneRevision, long topologyGeneration,
            IReadOnlyList<string> memberIds,
            IReadOnlyList<string> affectedMemberIds,
            IDictionary<string, long> baselineVersions)
        {
            GestureId = gestureId;
            Kind = kind;
            Intent = intent;
            SourceNoteId = sourceNoteId ?? String.Empty;
            TargetNoteId = targetNoteId ?? String.Empty;
            SceneRevision = sceneRevision;
            TopologyGeneration = topologyGeneration;
            MemberIds = memberIds ??
                Array.AsReadOnly(new string[0]);
            AffectedMemberIds = affectedMemberIds ??
                Array.AsReadOnly(new string[0]);
            BaselineVersions =
                new ReadOnlyDictionary<string, long>(
                    baselineVersions == null
                        ? new Dictionary<string, long>(
                            StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, long>(
                            baselineVersions,
                            StringComparer.OrdinalIgnoreCase));
        }

        internal long GestureId { get; private set; }
        internal StickyDockLocalGestureKind Kind { get; private set; }
        internal StickyDockCommitIntent Intent { get; private set; }
        internal string SourceNoteId { get; private set; }
        internal string TargetNoteId { get; private set; }
        internal long SceneRevision { get; private set; }
        internal long TopologyGeneration { get; private set; }
        internal IReadOnlyList<string> MemberIds { get; private set; }
        internal IReadOnlyList<string> AffectedMemberIds
            { get; private set; }
        internal IReadOnlyDictionary<string, long> BaselineVersions
            { get; private set; }
    }
}
