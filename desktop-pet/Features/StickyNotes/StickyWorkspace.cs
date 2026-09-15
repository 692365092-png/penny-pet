using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns hosted sticky windows, their accepted state and the side-tab UI.
    // Constructing this component has no window/startup side effects.
    internal sealed class StickyWorkspace : IDisposable
    {
        private readonly PetForm _pet;
        internal readonly StickyNoteRepository Notes;
        internal readonly StickyDockController Dock;
        private bool _disposed;
        internal bool IsDisposed { get { return _disposed || _pet.IsDisposed || _pet.Disposing; } }
        internal Rectangle PetBounds { get { return _pet.Bounds; } }
        internal ReminderSchedule Reminders { get { return _pet._reminders; } }

        internal StickyWorkspace(PetForm pet, StickyNoteRepository notes, SynchronizationContext context)
        {
            _pet = pet;
            Notes = notes;
            Context = context;
            Facts = new StickyFactsReceiver(Notes, Hosted, Placement);
            Dock = new StickyDockController(this);
        }

        internal void Start()
        {
            _leftNoteTabs = CreateTabs(StickyTabSide.Left);
            _rightNoteTabs = CreateTabs(StickyTabSide.Right);
            Host.Start();
            Host.Configure(HostedStickyEventReceived, Context);
            Host.SetFaultHandler(HostedStickyFaulted);
        }

        internal void ApplyWindowLayer()
        {
            _pet._windowLayers.KeepTransientBelowModal(_leftNoteTabs);
            _pet._windowLayers.KeepTransientBelowModal(_rightNoteTabs);
        }

        private StickyNoteTabsForm CreateTabs(StickyTabSide side)
        {
            return new StickyNoteTabsForm(side,
                id => { StickyNoteData note = Notes.Find(id); if (note != null) ShowHostedSticky(note, true); },
                id => { StickyNoteData note = Notes.Find(id); if (note != null) ConfirmDeleteStickyNote(note); },
                (id, index) => { StickyNoteData note = Notes.Find(id); if (note != null) ReorderStickyNoteTab(note, index); });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Dock.Dispose();
            Host.BeginShutdown();
            if (_leftNoteTabs != null) _leftNoteTabs.Close();
            if (_rightNoteTabs != null) _rightNoteTabs.Close();
        }

        internal DisplayTopologySnapshot CurrentTopologySnapshot() { return _pet.CurrentTopologySnapshot(); }
        internal WindowFacts CapturePetWindowFacts(DisplayTopologySnapshot topology) { return _pet.CapturePetWindowFacts(topology); }
        internal void RefreshMenuText() { _pet.RefreshMenuText(); }
        internal void ShowBubble(string text) { _pet.ShowBubble(text); }

        internal void UpdateAllStickyNoteReminderBanners()
        {
            System.Collections.Generic.List<ReminderItem> reminders =
                _pet._reminders.GetItems();
            foreach (StickyNoteData note in Notes.GetAll())
            {
                if (note == null || !Hosted.ContainsNote(note.Id))
                    continue;
                PostHostedStickyCommand(
                    StickyUiCommand.UpdateReminders(note.Id, reminders),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result == null ||
                            result.Status == StickyUiCommandStatus.Handled)
                            return;
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-reminder-refresh", result);
                    });
            }
        }

        internal void PreviewHostedReminderFontSize(ReminderItem existing,
            float fontSizePoints)
        {
            if (existing == null ||
                String.IsNullOrEmpty(existing.SourceNoteId)) return;
            System.Collections.Generic.List<ReminderItem> preview =
                _pet._reminders.GetItems();
            int index = preview.IndexOf(existing);
            if (index < 0) return;
            preview[index] = new ReminderItem(existing.DeadlineUtc,
                existing.Text, existing.SourceNoteId, fontSizePoints,
                existing.PreAlertEnabled);
            PostHostedStickyCommand(StickyUiCommand.UpdateReminders(
                existing.SourceNoteId, preview),
                delegate(StickyUiCommandResult result) { });
        }
        internal readonly StickyFactsReceiver Facts;
        private StickyNoteTabsForm _leftNoteTabs;
        private StickyNoteTabsForm _rightNoteTabs;
        internal static int HostedStickyWindowCreatedCount;
        internal readonly StickyUiHost Host = new StickyUiHost();
        internal readonly SynchronizationContext Context;
        internal readonly StickyHostedRuntime Hosted =
            new StickyHostedRuntime();
        internal readonly StickyPlacementRuntime Placement =
            new StickyPlacementRuntime();
        private bool _positioningNoteTabs;
        private string _noteTabsSignature = String.Empty;
        private bool? _leftTabsCovered;
        private bool? _rightTabsCovered;
        private readonly HashSet<string> _pendingStandaloneTopologyNotes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void DeleteStickyNote(StickyNoteData note)
        {
            DeleteStickyNote(note, null);
        }

        private void DeleteStickyNote(StickyNoteData note,
            Action<bool> completed)
        {
            if (note == null)
            {
                if (completed != null) completed(false);
                return;
            }
            if (Dock.DeferDockMutation(note.Id, () => DeleteStickyNote(Notes.Find(note.Id), completed))) return;
            Dock.CancelHostedDockRestores(note.Id);
            // A cancelled restore may have prepared a real HWND before its
            // lease was registered here. Let the STA acknowledge closure.
            BeginHostedStickyDelete(note, completed);
        }

        private void BeginHostedStickyDelete(StickyNoteData note)
        {
            BeginHostedStickyDelete(note, null);
        }

        private void BeginHostedStickyDelete(StickyNoteData note,
            Action<bool> completed)
        {
            if (note == null)
            {
                if (completed != null) completed(false);
                return;
            }
            string noteId = note.Id;
            if (!Hosted.TryBeginDelete(noteId))
            {
                if (completed != null) completed(false);
                return;
            }
            Dock.ClearHostedDockResizeSessionIfMember(noteId);
            PostHostedStickyCommand(StickyUiCommand.Close(noteId),
                delegate(StickyUiCommandResult result)
                {
                    Hosted.EndDelete(noteId);
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-delete", result);
                        ShowBubble("便利贴仍在编辑，删除已取消。");
                        if (completed != null) completed(false);
                        return;
                    }
                    ApplyHostedStickySnapshot(result.Snapshot,
                        result.Sequence, false, result.Facts, result.Topology);
                    Hosted.RemoveNote(noteId);
                    StickyNoteData canonical = Notes.Find(noteId);
                    if (canonical != null)
                        DeleteStickyNoteAfterWindowClosed(canonical);
                    if (completed != null) completed(true);
                });
        }

        private void DeleteStickyNoteAfterWindowClosed(StickyNoteData note)
        {
            Dock.ClearHostedDockResizeSessionIfMember(note.Id);
            _pet.CancelReminderForNote(note, false);
            Notes.Remove(note);
            Dock.RefreshDockResizeRoles();
            RefreshMenuText();
            RefreshNoteTabs();
        }

        internal void ConfirmDeleteStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (MessageBox.Show(_pet,
                "确定删除便签“" + note.DisplayTitle + "”吗？此操作无法撤销。",
                "删除侧边页签", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DeleteStickyNote(note);
        }

        internal void CreateStickyNote(string text) { CreateStickyNote(text, false, false); }
        internal void CreateTodoStickyNote() { CreateStickyNote(String.Empty, true, false); }
        internal void CreateScheduleStickyNote() { CreateStickyNote(String.Empty, false, true); }

        private void CreateStickyNote(string text, bool todo, bool schedule)
        {
            StickyNoteData note = null;
            try
            {
                note = PrepareStickyNoteDraft(text, new DockSize(320, schedule ? 360 : 300), todo, schedule);
                if (note == null) return;
                Notes.SaveAsync();
                StartHostedSticky(note, true);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure(schedule ? "日程" : todo ? "待办清单" : "便利贴", error);
            }
        }

        internal void QueueStickyWindowAction(Action action, string context)
        {
            if (action == null || IsDisposed) return;
            if (_pet._menu != null && _pet._menu.Visible) _pet._menu.Close();
            _pet.BeginInvoke((MethodInvoker)delegate
            {
                try { action(); }
                catch (Exception error) { ShowStickyWindowFailure(context, error); }
            });
        }

        internal static List<Rectangle> CalculateStickyRecoveryLayout(
            Rectangle work, IList<Size> componentSizes)
        {
            return CalculateStickyRecoveryLayout(work, componentSizes, 1F);
        }

        private static List<Rectangle> CalculateStickyRecoveryLayout(
            Rectangle work, IList<Size> componentSizes, float scale)
        {
            List<DockSize> dockSizes = new List<DockSize>();
            if (componentSizes != null)
            {
                foreach (Size size in componentSizes)
                    dockSizes.Add(new DockSize
                    {
                        Width = size.Width,
                        Height = size.Height
                    });
            }
            List<DockRect> dockLayout =
                StickyDockGeometry.CalculateStickyRecoveryLayout(
                    new DockRect
                    {
                        Left = work.Left,
                        Top = work.Top,
                        Width = work.Width,
                        Height = work.Height
                    },
                    dockSizes,
                    scale);
            List<Rectangle> result = new List<Rectangle>();
            foreach (DockRect item in dockLayout)
                result.Add(new Rectangle(item.Left, item.Top,
                    item.Width, item.Height));
            return result;
        }

        internal static Point CalculateStickyRecoveryAnchor(Rectangle work,
            Rectangle pet, Size window, int componentIndex)
        {
            DockPoint anchor = StickyDockGeometry.CalculateStickyRecoveryAnchor(
                new DockRect
                {
                    Left = work.Left,
                    Top = work.Top,
                    Width = work.Width,
                    Height = work.Height
                },
                new DockRect
                {
                    Left = pet.Left,
                    Top = pet.Top,
                    Width = pet.Width,
                    Height = pet.Height
                },
                new DockSize
                {
                    Width = window.Width,
                    Height = window.Height
                },
                componentIndex);
            return new Point(anchor.X, anchor.Y);
        }

        private void RollBackFailedStickyCreation(StickyNoteData note)
        {
            if (note == null) return;
            Notes.Remove(note);
            RefreshMenuText();
            RefreshNoteTabs();
        }

        internal void ShowStickyWindowFailure(string kind, Exception error)
        {
            ApplicationDiagnostics.ReportNonFatal(kind ?? "sticky-window", error);
            MessageBox.Show(_pet,
                "未能显示" + (String.IsNullOrEmpty(kind) ? "便利贴" : kind) +
                "。程序没有保留不可见的空白项目。\n\n" +
                "请把下面的诊断文件发给作者：\n" +
                ApplicationDiagnostics.LogFilePath,
                "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // One creation attempt = one topology snapshot + one in-memory draft
        // fully configured (type, v10 compatibility, v11 preferred, visible)
        // before the caller performs the single first save.
        private StickyNoteData PrepareStickyNoteDraft(string text,
            DockSize logicalSize, bool todo, bool schedule)
        {
            if (!Notes.CanCreate)
            {
                if (!Notes.LoadSucceeded)
                    ShowBubble("旧便利贴数据暂时无法安全恢复，请查看诊断记录。" +
                        "程序没有覆盖原文件。");
                else
                    ShowBubble("便利贴最多可以保存 " +
                        StickyNoteLimits.MaximumNotes +
                        " 张，请先删除不需要的便利贴。");
                return null;
            }

            // The whole attempt reads one topology snapshot, and Pet facts
            // are captured against that same snapshot (SPAWN-INV-05).
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            WindowFacts petFacts = CapturePetWindowFacts(topology);
            DisplaySurfaceSnapshot targetSurface = topology != null &&
                petFacts != null
                    ? topology.FindByRuntimeGdiName(petFacts.RuntimeGdiName)
                    : null;

            StickyNoteData note = Notes.CreateDraft(text, Point.Empty);
            if (note == null) return null;
            note.IsTodoList = todo;
            note.IsSchedule = schedule;
            if (todo) note.Title = "待办清单";
            if (schedule)
            {
                note.Title = "日程";
                note.FontSizeTwips = 320;
            }

            if (targetSurface != null)
            {
                StickyCanonicalPlacement placement =
                    StickySpawnPolicy.PlanCenteredSpawn(
                        targetSurface.RuntimeGdiName,
                        targetSurface.WorkArea, targetSurface.Bounds.Left,
                        targetSurface.Bounds.Top, petFacts.Scale,
                        Math.Max(1, logicalSize.Width),
                        Math.Max(1, logicalSize.Height));
                placement.ApplyTo(note);
                string preferredKey =
                    DisplayTopologyRules.SelectPreferredTargetKey(
                        targetSurface, null);
                CommitHostedStickyPreferred(note, preferredKey,
                    placement.LocalX, placement.LocalY,
                    placement.LocalWidth, placement.LocalHeight,
                    PlacementReason.Spawn);
                TraceSpawnPlacement(note, petFacts, targetSurface,
                    preferredKey);
            }
            else
            {
                // Fallback keeps the note visible but never fabricates a
                // durable preferred identity without real facts.
                ApplyLegacySpawnFallback(note, logicalSize);
            }
            note.Visible = true;
            return note;
        }

        private void ApplyLegacySpawnFallback(StickyNoteData note,
            DockSize logicalSize)
        {
            // Degraded fallback keeps the same product invariant as the
            // normal path: centered in Penny's current screen WorkArea, with
            // the logical default size. No durable preferred identity is
            // fabricated here.
            Rectangle work = Screen.FromRectangle(_pet.Bounds).WorkingArea;
            PhysicalRect centered = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(work.Left, work.Top, work.Width,
                    work.Height),
                Math.Max(1, logicalSize.Width),
                Math.Max(1, logicalSize.Height));
            note.X = centered.Left;
            note.Y = centered.Top;
            note.Width = centered.Width;
            note.Height = centered.Height;
        }

        private static void TraceSpawnPlacement(StickyNoteData note,
            WindowFacts petFacts, DisplaySurfaceSnapshot surface,
            string preferredKey)
        {
            DisplayDiagnostics.Trace("PlacementResolved",
                "reason=Spawn note=" + note.Id +
                " topology=" + (petFacts == null
                    ? 0 : petFacts.TopologyGeneration) +
                " petGdi=" + (petFacts == null
                    ? "-" : petFacts.RuntimeGdiName) +
                " targetSurface=" + surface.RuntimeSurfaceId +
                " targetKey=" + (String.IsNullOrEmpty(preferredKey)
                    ? "-" : preferredKey) +
                " dpi=" + (petFacts == null ? 0 : petFacts.Dpi) +
                " work=(" + surface.WorkArea.Left + "," +
                surface.WorkArea.Top + "," + surface.WorkArea.Width + "," +
                surface.WorkArea.Height + ")" +
                " logical=(" + note.LocalLogicalX + "," +
                note.LocalLogicalY + "," + note.LocalLogicalWidth + "," +
                note.LocalLogicalHeight + ")" +
                " physical=(" + note.X + "," + note.Y + "," +
                note.Width + "," + note.Height + ")" +
                " preferredDurable=" +
                !String.IsNullOrEmpty(preferredKey));
        }

        internal bool IsTopologyCurrent(DisplayTopologySnapshot topology)
        {
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            return topology != null && current != null &&
                topology.Generation == current.Generation;
        }

        private WindowFactsVersionDisposition ClassifyHostedGeometry(
            StickyUiEvent value)
        {
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            return value == null ? WindowFactsVersionDisposition.Invalid :
                WindowFactsVersionRules.Classify(value.NoteId, value.Sequence,
                    value.Facts, value.Topology,
                    current == null ? -1 : current.Generation);
        }

        internal bool IsCurrentHostedGeometryEvent(StickyUiEvent value)
        {
            return ClassifyHostedGeometry(value) ==
                WindowFactsVersionDisposition.Current;
        }

        // DRT-7/11: publish the new generation as a hard Dock barrier, resume
        // an active drag from freshly captured source facts, then reconcile
        // standalone windows and whole persisted Dock groups independently.
        internal void HandleStickyTopologyChanged(
            DisplayTopologySnapshot snapshot)
        {
            if (snapshot == null || IsDisposed) return;
            Dock.InvalidateDockPlansForTopologyChange(snapshot);
            Dock.RestartHostedDockRestores(snapshot);
            Dock.ResumeDockDragAfterTopologyChange(snapshot);
            WindowFacts petFacts = CapturePetWindowFacts(snapshot);
            foreach (StickyNoteData note in Notes.GetAll())
            {
                if (note == null || !note.Visible || !IsHostedSticky(note))
                    continue;
                if (!String.IsNullOrEmpty(note.DockGroupId)) continue;
                ReconcileStandaloneSticky(note, snapshot, petFacts);
            }
            Dock.ReconcileDockGroups(snapshot, petFacts);
        }

        private void ReconcileStandaloneSticky(StickyNoteData note,
            DisplayTopologySnapshot snapshot, WindowFacts petFacts)
        {
            bool hasPreferred =
                !String.IsNullOrWhiteSpace(note.PreferredDisplayTargetKey) &&
                note.PreferredLocalLogicalWidth > 0 &&
                note.PreferredLocalLogicalHeight > 0;
            DisplaySurfaceSnapshot preferredSurface = hasPreferred
                ? snapshot.FindByTargetKey(note.PreferredDisplayTargetKey)
                : null;
            if (preferredSurface != null)
            {
                // Preferred display is active again. Only a note the user did
                // not manually move away gets pulled back to its preference.
                if (Placement.IsTemporaryRehome(note.Id) &&
                    !Placement.UserMovedSinceRehome(note.Id))
                {
                    string noteId = note.Id;
                    StickyUiReprojectTarget returnTarget =
                        new StickyUiReprojectTarget(
                            preferredSurface.RuntimeGdiName,
                            note.PreferredLocalLogicalX,
                            note.PreferredLocalLogicalY,
                            note.PreferredLocalLogicalWidth,
                            note.PreferredLocalLogicalHeight,
                            false, true);
                    PostHostedStickyCommand(StickyUiCommand.Reproject(
                        noteId, returnTarget, snapshot),
                        delegate(StickyUiCommandResult result)
                        {
                            if (result != null && result.Status ==
                                StickyUiCommandStatus.Handled &&
                                ApplyReprojectResult(result, noteId, snapshot))
                            {
                                Placement.
                                    MarkReturnedToPreferred(noteId);
                                DisplayDiagnostics.Trace(
                                    "PreferredReturned",
                                    "note=" + noteId);
                            }
                            else if (IsStaleReprojectResult(result, noteId,
                                snapshot))
                                ScheduleLatestStandaloneReconcile(noteId);
                        });
                }
                return;
            }

            if (Placement.IsTemporaryRehome(note.Id)) return;
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            if (!TryBuildTemporaryRehomeTarget(note, snapshot, petFacts,
                true, out fallback, out rehomeTarget,
                Placement.GetEffective(note.Id))) return;
            string rehomedNoteId = note.Id;
            PostHostedStickyCommand(StickyUiCommand.Reproject(rehomedNoteId,
                rehomeTarget, snapshot),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null && result.Status ==
                        StickyUiCommandStatus.Handled &&
                        ApplyReprojectResult(result, rehomedNoteId, snapshot))
                    {
                        CompleteTemporaryRehome(rehomedNoteId, fallback,
                            "preferred-display-missing", snapshot);
                    }
                    else if (IsStaleReprojectResult(result, rehomedNoteId,
                        snapshot))
                        ScheduleLatestStandaloneReconcile(rehomedNoteId);
                });
        }

        // Builds the typed temporary-rehome intent without guessing the
        // target DPI: the Sticky STA projects the preferred logical size with
        // GetDpiForWindow after bootstrapping the HWND onto the fallback
        // surface. The durable preferred fields are never modified here.
        private static bool TryBuildTemporaryRehomeTarget(StickyNoteData note,
            DisplayTopologySnapshot topology, WindowFacts petFacts,
            bool showAfter, out DisplaySurfaceSnapshot fallback,
            out StickyUiReprojectTarget target, WindowFacts actual = null)
        {
            fallback = null;
            target = null;
            if (note == null || topology == null ||
                !String.IsNullOrEmpty(note.DockGroupId) ||
                String.IsNullOrWhiteSpace(
                    note.PreferredDisplayTargetKey) ||
                note.PreferredLocalLogicalWidth <= 0 ||
                note.PreferredLocalLogicalHeight <= 0 ||
                topology.FindByTargetKey(
                    note.PreferredDisplayTargetKey) != null) return false;
            fallback = FallbackDisplayPolicy.ResolveFallbackSurface(topology,
                note.PreferredDisplayTargetKey,
                actual == null
                    ? new PhysicalRect(note.X, note.Y, note.Width, note.Height)
                    : actual.PhysicalBounds,
                petFacts == null ? String.Empty : petFacts.RuntimeGdiName);
            if (fallback == null) return false;
            target = new StickyUiReprojectTarget(
                fallback.RuntimeGdiName, 0, 0,
                note.PreferredLocalLogicalWidth,
                note.PreferredLocalLogicalHeight,
                true, showAfter);
            return true;
        }

        private void CompleteTemporaryRehome(string noteId,
            DisplaySurfaceSnapshot fallback, string reason,
            DisplayTopologySnapshot observedTopology)
        {
            Placement.MarkTemporaryRehome(noteId, reason);
            DisplayDiagnostics.Trace("TemporaryRehome",
                "note=" + noteId +
                " target=" + fallback.RuntimeSurfaceId +
                " work=(" + fallback.WorkArea.Left + "," +
                fallback.WorkArea.Top + "," + fallback.WorkArea.Width + "," +
                fallback.WorkArea.Height + ")");
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            if (current == null || observedTopology == null ||
                current.Generation == observedTopology.Generation) return;
            // The command completed against an older immutable snapshot.
            // Clear that temporary decision and immediately reconcile against
            // the newest topology so an async hotplug race cannot strand it.
            Placement.ClearTemporaryRehome(noteId);
            StickyNoteData note = Notes.Find(noteId);
            if (note != null && note.Visible &&
                String.IsNullOrEmpty(note.DockGroupId))
                ReconcileStandaloneSticky(note, current,
                    CapturePetWindowFacts(current));
        }

        private void StartHostedSticky(StickyNoteData note,
            bool focusEditor)
        {
            if (note == null) return;
            string noteId = note.Id;
            if (!Hosted.AddNote(noteId))
            {
                PostHostedStickyShow(note, focusEditor);
                return;
            }
            HostedStickyWindowCreatedCount++;
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            bool temporaryRehome = TryBuildTemporaryRehomeTarget(note,
                topology, CapturePetWindowFacts(topology), false,
                out fallback, out rehomeTarget, Placement.GetEffective(note.Id));
            StickyNoteUiSnapshot createSnapshot =
                StickyNoteUiSnapshot.FromData(note);
            StickyUiCommand command = StickyUiCommand.Create(
                createSnapshot, focusEditor, _pet._reminders.GetItems(), topology,
                rehomeTarget, StickyPlacementRecovery.SelectForShow(note, topology));
            PostHostedStickyCommand(command,
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        if (temporaryRehome)
                        {
                            if (!ApplyReprojectResult(result, noteId, topology))
                            {
                                if (IsStaleReprojectResult(result, noteId,
                                    topology))
                                {
                                    ScheduleLatestStandaloneReconcile(noteId);
                                    return;
                                }
                                HandleHostedStickyFailure(
                                    new string[] { noteId },
                                    "sticky-hosted-create-reproject", result);
                                return;
                            }
                        }
                        else
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, true, result.Facts, result.Topology);
                        if (temporaryRehome)
                            CompleteTemporaryRehome(noteId, fallback,
                                "preferred-display-missing-at-restore",
                                topology);
                        return;
                    }
                    HandleHostedStickyFailure(new string[] { noteId },
                        "sticky-hosted-create", result);
                });
        }

        internal bool IsHostedSticky(StickyNoteData note)
        {
            return note != null && Hosted.ContainsNote(note.Id);
        }

        private bool PostHostedStickyShow(StickyNoteData note,
            bool focusEditor)
        {
            if (!IsHostedSticky(note)) return false;
            string noteId = note.Id;
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            if (TryBuildTemporaryRehomeTarget(note, topology,
                CapturePetWindowFacts(topology), true,
                out fallback, out rehomeTarget, Placement.GetEffective(note.Id)))
            {
                PostHostedStickyCommand(StickyUiCommand.Reproject(noteId,
                    rehomeTarget, topology),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled &&
                            ApplyReprojectResult(result, noteId, topology))
                        {
                            CompleteTemporaryRehome(noteId, fallback,
                                "preferred-display-missing-at-reopen",
                                topology);
                            if (focusEditor)
                                PostHostedStickyCommand(
                                    StickyUiCommand.FocusPrimaryInput(noteId),
                                    delegate(StickyUiCommandResult ignored) { });
                            return;
                        }
                        if (IsStaleReprojectResult(result, noteId, topology))
                        {
                            ScheduleLatestStandaloneReconcile(noteId);
                            return;
                        }
                        HandleHostedStickyFailure(new string[] { noteId },
                            "sticky-hosted-show", result);
                    });
                return true;
            }
            PostHostedStickyCommand(StickyUiCommand.Show(noteId,
                focusEditor, topology, StickyPlacementRecovery.SelectForShow(note, topology)),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence, true, result.Facts, result.Topology);
                        if (Placement.IsTemporaryRehome(noteId) &&
                            topology != null && topology.FindByTargetKey(
                                note.PreferredDisplayTargetKey) != null)
                            Placement.MarkReturnedToPreferred(noteId);
                        return;
                    }
                    HandleHostedStickyFailure(new string[] { noteId },
                        "sticky-hosted-show", result);
                });
            return true;
        }

        internal bool PostHostedStickyHide(StickyNoteData note)
        {
            if (note != null) Dock.CancelHostedDockRestores(note.Id);
            if (!IsHostedSticky(note)) return false;
            string noteId = note.Id;
            Dock.ClearHostedDockResizeSessionIfMember(noteId);
            PostHostedStickyCommand(StickyUiCommand.Hide(noteId),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        if (result.Snapshot != null &&
                            !result.Snapshot.Visible)
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, true, result.Facts, result.Topology);
                        return;
                    }
                    ReportHostedStickyCommandFailure(
                        "sticky-hosted-hide", result);
                });
            return true;
        }

        internal void PostHostedStickyCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed)
        {
            Host.PostCommand(command, completed, Context);
        }

        internal void HostedStickyFaulted(Exception error)
        {
            Dock.CancelHostedDockRestores();
            Dock.ClearHostedDockResizeSession();
            Dock.ResetDockDragState(true);
            if (error != null)
                ApplicationDiagnostics.ReportNonFatal(
                    "hosted-sticky-faulted", error);
            // Hosted Sticky windows are degraded, but canonical note data stays
            // untouched and Penny itself can still exit safely.
            if (_pet._exiting || IsDisposed) return;
            ShowBubble(
                "便利贴界面遇到问题，已停止使用，数据仍然保留。请重启 Penny 后再试。");
        }

        internal void HostedStickyEventReceived(StickyUiEvent value)
        {
            if (value == null || IsDisposed ||
                !Hosted.ContainsNote(value.NoteId)) return;
            TraceHostedWindowFacts(value);
            if (value.IsDockInput)
            {
                if (value.BeginsDockInput)
                {
                    if (value.Snapshot == null || !String.Equals(value.NoteId,
                        value.Snapshot.NoteId, StringComparison.OrdinalIgnoreCase) ||
                        !Hosted.CanApplySequence(value.NoteId, value.Sequence)) return;
                    // Input ownership is independent of topology. Retire the old
                    // gesture even if this start's geometry needs a later rebase.
                    Dock.BeginDockInput(value.Input);
                    if (!Dock.Gestures.Matches(value.Input)) return;
                    StickyNoteData source = Notes.Find(value.NoteId);
                    // Released mutations may have hidden or deleted the source.
                    if (source == null || !source.Visible || !Hosted.ContainsNote(value.NoteId)) return;
                }
                else if (!Dock.Gestures.Matches(value.Input)) return;
            }
            if (value.Kind == StickyUiEventKind.TypingActivity)
            {
                if (!_pet._exiting) _pet.TriggerTypingAnimation();
                return;
            }
            if (value.Kind == StickyUiEventKind.InputFocusChanged)
            {
                Hosted.SetInputFocus(value.NoteId, value.Flag);
                return;
            }
            if (value.Kind == StickyUiEventKind.ImeCompositionChanged)
            {
                Hosted.SetImeComposition(value.NoteId, value.Flag);
                if (!value.Flag)
                {
                    if (Hosted.ExitRequested &&
                        !Hosted.HasImeComposition)
                        TryCloseAllHostedStickies();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.FirstRendered)
            {
                _pet.MarkFirstRendered(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.HeaderDragStarted ||
                value.Kind == StickyUiEventKind.HeaderDragMoved ||
                value.Kind == StickyUiEventKind.HeaderDragCompleted)
            {
                bool geometryCurrent = IsCurrentHostedGeometryEvent(value);
                if (!ApplyHostedStickyEvent(value, false) || !geometryCurrent)
                    return;
                StickyNoteData canonical = Notes.Find(value.NoteId);
                if (canonical == null) return;
                DockWindowFacts facts = DockWindowFacts.FromWindowFacts(
                    value.Facts, canonical.Visible, canonical.AlwaysOnTop);
                if (facts == null) return;
                if (value.Kind == StickyUiEventKind.HeaderDragStarted)
                    Dock.BeginStickyDockDrag(facts, value.Facts, value.Topology);
                else if (value.Kind == StickyUiEventKind.HeaderDragMoved)
                {
                    Dock.MoveStickyDockDrag(facts, value.Facts, value.Topology);
                    ApplyNoteTabZOrder();
                }
                else
                {
                    Dock.CompleteStickyDockDrag(facts, value);
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.BoundsChanged)
            {
                ApplyHostedStickyEvent(value, false);
                ApplyNoteTabZOrder();
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizeStarted ||
                value.Kind == StickyUiEventKind.DockHorizontalResizeStarted)
            {
                if (IsCurrentHostedGeometryEvent(value) && ApplyHostedStickyEvent(value, false))
                    Dock.BeginHostedStickyDockResize(value, value.Kind == StickyUiEventKind.DockHorizontalResizeStarted
                        ? DockResizeKind.Horizontal : DockResizeKind.Divider);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizing ||
                value.Kind == StickyUiEventKind.DockHorizontalResizing)
            {
                if (IsCurrentHostedGeometryEvent(value) &&
                    Hosted.CanApplySequence(value.NoteId, value.Sequence))
                    Dock.ResizeHostedStickyDock(value);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizeCompleted ||
                value.Kind == StickyUiEventKind.DockHorizontalResizeCompleted)
            {
                Dock.CompleteHostedStickyDockResize(value);
                return;
            }
            if (value.Kind == StickyUiEventKind.CloseRequested)
            {
                if (!IsCurrentHostedGeometryEvent(value) ||
                    !ApplyHostedStickyEvent(value, false)) return;
                Dock.CloseStickyDockNote(Notes.Find(value.NoteId),
                    DockWindowFacts.FromWindowFacts(value.Facts,
                        value.Snapshot.Visible, value.Snapshot.AlwaysOnTop));
                return;
            }
            if (value.Kind == StickyUiEventKind.SnapshotChanged)
            {
                StickyNoteData canonical = Notes.Find(value.NoteId);
                bool topMostChanged = canonical != null &&
                    value.Snapshot != null && canonical.AlwaysOnTop !=
                    value.Snapshot.AlwaysOnTop;
                bool geometryCurrent = IsCurrentHostedGeometryEvent(value);
                if (!ApplyHostedStickyEvent(value)) return;
                if (geometryCurrent)
                    AdoptPreferredIfEmpty(canonical, value.Facts,
                        value.Topology);
                if (topMostChanged)
                {
                    Dock.ApplyDockComponentTopMost(canonical,
                        value.Snapshot.AlwaysOnTop, value.NoteId);
                    Notes.SaveAsync();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.UserResizeStarted)
            {
                ApplyHostedStickyEvent(value, false);
                return;
            }
            if (value.Kind == StickyUiEventKind.UserResizeCompleted)
            {
                bool geometryCurrent = IsCurrentHostedGeometryEvent(value);
                if (!ApplyHostedStickyEvent(value, false)) return;
                if (!geometryCurrent)
                {
                    DisplayDiagnostics.Trace("PreferredCommitRejected",
                        "note=" + value.NoteId + " reason=stale-user-resize");
                    return;
                }
                StickyNoteData canonical = Notes.Find(value.NoteId);
                string targetKey;
                LogicalRect local;
                if (canonical != null && TryBuildPreference(value.Facts,
                    value.Topology, canonical.PreferredDisplayTargetKey,
                    out targetKey, out local) &&
                    CommitHostedStickyPreferred(canonical, targetKey,
                        local.X, local.Y, local.Width, local.Height,
                        PlacementReason.UserResizeCommit))
                {
                    Placement.MarkUserPlacementCommit(
                        value.NoteId);
                    Notes.SaveAsync();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.Closed)
            {
                if (!ApplyHostedStickyEvent(value)) return;
                Hosted.RemoveNote(value.NoteId);
                Placement.Remove(value.NoteId);
                _pet._renderedFirstRenderNoteIds.Remove(value.NoteId);
                Dock.ClearHostedDockResizeSessionIfMember(value.NoteId);
                Dock.CancelDockFinalizationIfMember(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.CancelReminderRequested)
            {
                StickyNoteData note = Notes.Find(value.NoteId);
                if (note != null) _pet.CancelReminderForNote(note, true);
                return;
            }
            if (value.Kind == StickyUiEventKind.ModifyReminderRequested)
            {
                if (value.Reminder != null) _pet.EditReminder(value.Reminder);
                return;
            }
            if (value.Kind == StickyUiEventKind.DeleteReminderRequested)
            {
                if (value.Reminder != null) _pet.CancelReminder(value.Reminder, true);
                return;
            }
            if (value.Kind == StickyUiEventKind.DeleteRequested)
            {
                ConfirmHostedStickyDelete(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.NewNoteRequested)
            {
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-hosted-create-note");
                return;
            }
            if (value.Kind == StickyUiEventKind.NewTodoRequested)
            {
                QueueStickyWindowAction(CreateTodoStickyNote,
                    "sticky-hosted-create-todo");
                return;
            }
            if (value.Kind == StickyUiEventKind.NewScheduleRequested)
            {
                QueueStickyWindowAction(CreateScheduleStickyNote,
                    "sticky-hosted-create-schedule");
                return;
            }
            ApplyHostedStickyEvent(value);
        }

        private void TraceHostedWindowFacts(StickyUiEvent value)
        {
            if (!DisplayDiagnostics.Enabled || value.Facts == null) return;
            WindowFacts facts = value.Facts;
            DisplayDiagnostics.Trace("WindowFacts",
                "note=" + facts.WindowId + " topology=" +
                facts.TopologyGeneration + " target=" +
                facts.ActiveTargetKey + " seq=" + facts.WindowSequence +
                " dpi=" + facts.Dpi + " gdi=" + facts.RuntimeGdiName +
                " physical=(" + facts.PhysicalBounds.Left + "," +
                facts.PhysicalBounds.Top + "," +
                facts.PhysicalBounds.Width + "," +
                facts.PhysicalBounds.Height + ")");
        }

        // Geometry-bearing production events treat WindowFacts as the only
        // geometry truth. Content flows through ApplyContentTo, Visible and
        // AlwaysOnTop are applied explicitly, and the v10 compatibility
        // geometry is derived from the facts plus the capture-time topology
        // surface - never from the snapshot's WPF-derived geometry.
        private bool ApplyHostedStickyEvent(StickyUiEvent value,
            bool persist = true)
        {
            return value != null && value.Snapshot != null &&
                String.Equals(value.NoteId, value.Snapshot.NoteId, StringComparison.OrdinalIgnoreCase) &&
                ApplyHostedStickySnapshot(value.Snapshot,
                value.Sequence, persist, value.Facts, value.Topology);
        }



        // A successful Reproject carries actual HWND facts; the repository is
        // updated from those facts, never from the WPF-derived snapshot
        // geometry, and the runtime Effective advances to the same facts.
        private bool ApplyReprojectResult(StickyUiCommandResult result,
            string noteId, DisplayTopologySnapshot expectedTopology)
        {
            if (result == null || result.Status != StickyUiCommandStatus.Handled ||
                result.Snapshot == null || result.Topology == null ||
                !IsTopologyCurrent(expectedTopology) ||
                result.Topology.Generation != expectedTopology.Generation) return false;
            StickyFactsReceiver.Update update;
            if (!Facts.TryPrepare(new DockBatchMemberResult(noteId, result.Sequence,
                result.Facts, result.Snapshot), result.Topology, out update)) return false;
            update.Commit();
            Notes.SaveAsync();
            RefreshMenuText();
            return true;
        }

        private bool IsStaleReprojectResult(StickyUiCommandResult result,
            string noteId, DisplayTopologySnapshot expectedTopology)
        {
            if (result == null || result.Status != StickyUiCommandStatus.Handled ||
                result.Facts == null || result.Topology == null ||
                expectedTopology == null) return false;
            return !IsTopologyCurrent(expectedTopology) ||
                result.Topology.Generation != expectedTopology.Generation ||
                WindowFactsVersionRules.Classify(noteId, result.Sequence,
                    result.Facts, expectedTopology,
                    expectedTopology.Generation) ==
                    WindowFactsVersionDisposition.StaleTopology;
        }

        // One deferred reconcile per note collapses topology churn. An active
        // dock gesture owns its source until its epoch reaches a barrier.
        private void ScheduleLatestStandaloneReconcile(string noteId)
        {
            if (String.IsNullOrWhiteSpace(noteId) || IsDisposed ||
                !_pendingStandaloneTopologyNotes.Add(noteId)) return;
            _pet.BeginInvoke((MethodInvoker)delegate
            {
                _pendingStandaloneTopologyNotes.Remove(noteId);
                if (IsDisposed || String.Equals(noteId,
                    Dock.Interaction.SourceNoteId, StringComparison.OrdinalIgnoreCase))
                    return;
                StickyNoteData note = Notes.Find(noteId);
                DisplayTopologySnapshot snapshot = CurrentTopologySnapshot();
                if (note == null || snapshot == null || !IsHostedSticky(note))
                    return;
                ReconcileStandaloneSticky(note, snapshot,
                    CapturePetWindowFacts(snapshot));
            });
        }

        private static bool TryBuildPreference(WindowFacts facts,
            DisplayTopologySnapshot topology, string existingKey,
            out string targetKey, out LogicalRect localRect)
        {
            targetKey = null;
            localRect = new LogicalRect();
            WindowPlacementPreference preference;
            if (!StickyPlacementRules.TryBuildPreferredPlacement(facts,
                topology, existingKey, out preference)) return false;
            targetKey = preference.PreferredTargetKey;
            localRect = preference.LocalLogicalRect;
            return true;
        }

        internal bool CommitHostedStickyPreferred(StickyNoteData canonical,
            string targetKey, int localX, int localY, int localWidth,
            int localHeight, PlacementReason reason)
        {
            if (canonical == null ||
                !StickyPlacementRules.CanCommitPreferred(reason)) return false;
            if (String.IsNullOrWhiteSpace(targetKey) ||
                localWidth <= 0 || localHeight <= 0) return false;
            canonical.PreferredDisplayTargetKey = targetKey;
            canonical.PreferredLocalLogicalX = localX;
            canonical.PreferredLocalLogicalY = localY;
            canonical.PreferredLocalLogicalWidth = localWidth;
            canonical.PreferredLocalLogicalHeight = localHeight;
            return true;
        }

        // Fills a missing preference only. v10 migration is attempted first
        // (preserving the persisted display-local intent), then the actual
        // shown WindowFacts when the saved display is not resolvable. An
        // existing preference is never overwritten here.
        private void AdoptPreferredIfEmpty(StickyNoteData canonical,
            WindowFacts facts, DisplayTopologySnapshot topology)
        {
            if (canonical == null ||
                !String.IsNullOrWhiteSpace(
                    canonical.PreferredDisplayTargetKey)) return;
            if (StickyPlacementRules.MigrateV10Preferred(canonical,
                topology))
            {
                Notes.SaveAsync();
                return;
            }
            string targetKey;
            LogicalRect local;
            if (TryBuildPreference(facts, topology, String.Empty,
                out targetKey, out local) &&
                !String.IsNullOrWhiteSpace(targetKey) &&
                local.Width > 0 && local.Height > 0)
            {
                canonical.PreferredDisplayTargetKey = targetKey;
                canonical.PreferredLocalLogicalX = local.X;
                canonical.PreferredLocalLogicalY = local.Y;
                canonical.PreferredLocalLogicalWidth = local.Width;
                canonical.PreferredLocalLogicalHeight = local.Height;
                Notes.SaveAsync();
            }
        }

        internal bool ApplyHostedStickySnapshot(StickyNoteUiSnapshot snapshot,
            long sequence, bool persist = true, WindowFacts facts = null,
            DisplayTopologySnapshot topology = null)
        {
            bool tabsChanged;
            if (!Facts.TryApplySnapshot(snapshot, sequence, facts,
                topology, CurrentTopologySnapshot(), out tabsChanged)) return false;
            if (persist) Notes.SaveAsync();
            RefreshMenuText();
            if (tabsChanged) RefreshNoteTabs();
            return true;
        }

        internal static bool ShouldApplyHostedSequence(long sequence,
            long appliedSequence)
        {
            return sequence > appliedSequence;
        }

        private void HandleHostedStickyFailure(IEnumerable<string> noteIds,
            string context, StickyUiCommandResult result)
        {
            ReportHostedStickyCommandFailure(context, result);
            DockMutationQueue failedFinal = Dock.Interaction.Mutations;
            bool cancelHeaderFinal = false;
            if (noteIds != null)
            {
                foreach (string noteId in noteIds)
                {
                    if (String.IsNullOrEmpty(noteId)) continue;
                    StickyNoteData note = Notes.Find(noteId);
                    if (failedFinal != null && failedFinal.Contains(
                        noteId, note == null ? null : note.DockGroupId)) cancelHeaderFinal = true;
                    Dock.CancelHostedDockRestores(noteId);
                    Hosted.RemoveNote(noteId);
                    Placement.InvalidateEffective(noteId);
                    _pet._renderedFirstRenderNoteIds.Remove(noteId);
                    _pet._expectedFirstRenderNoteIds.Remove(noteId);
                    if (note != null) note.Visible = false;
                    PostHostedStickyCommand(StickyUiCommand.Close(noteId),
                        delegate(StickyUiCommandResult closeResult) { });
                    Dock.ClearHostedDockResizeSessionIfMember(noteId);
                }
            }
            if (cancelHeaderFinal && ReferenceEquals(Dock.Interaction.Mutations, failedFinal)) Dock.ResetDockDragState(true);
            Notes.SaveAsync();
            RefreshNoteTabs();
            RefreshMenuText();
            ShowBubble("便利贴窗口暂时无法显示，内容已保留在侧边页签中。");
        }

        internal static void ReportHostedStickyCommandFailure(string context,
            StickyUiCommandResult result)
        {
            string detail = result == null ? "No command result." :
                result.Status + ": " + result.Error;
            ApplicationDiagnostics.ReportNonFatal(context,
                new InvalidOperationException(detail));
        }

        internal bool BeginHostedStickyExitIfNeeded()
        {
            if (Dock.DeferDockMutation(null, _pet.BeginExitSequence)) return true;
            Dock.CancelHostedDockRestores();
            if (Hosted.NoteCount == 0 ||
                Hosted.ExitPrepared)
                return false;
            Dock.ClearHostedDockResizeSession();
            Hosted.RequestExit();
            TryCloseAllHostedStickies();
            return true;
        }

        private void TryCloseAllHostedStickies()
        {
            if (!Hosted.TryBeginCloseAll()) return;
            PostHostedStickyCommand(StickyUiCommand.CloseAll(),
                delegate(StickyUiCommandResult result)
                {
                    Hosted.EndCloseAll();
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.NotAccepted)
                        return;
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        Hosted.CancelExit();
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-exit", result);
                        ShowBubble("便利贴仍在收尾，退出已取消，请稍后重试。");
                        return;
                    }
                    if (result.FinalSnapshots != null)
                        foreach (StickyUiFinalSnapshot finalSnapshot in
                            result.FinalSnapshots)
                            ApplyHostedStickySnapshot(
                                finalSnapshot.Snapshot,
                                finalSnapshot.Sequence, false, finalSnapshot.Facts,
                                finalSnapshot.Topology);
                    Hosted.PrepareExit();
                    Host.BeginShutdown();
                    _pet.BeginExitSequence();
                });
        }

        internal void CloseHostedStickyRuntimeForReload(
            Action<StickyUiCommandResult> completed)
        {
            if (completed == null) return;
            if (Dock.DeferDockMutation(null, () => CloseHostedStickyRuntimeForReload(completed))) return;
            Dock.CancelHostedDockRestores();
            Dock.ClearHostedDockResizeSession();
            if (Hosted.NoteCount == 0)
            {
                completed(StickyUiCommandResult.Handled(
                    new StickyUiFinalSnapshot[0]));
                return;
            }
            PostHostedStickyCommand(StickyUiCommand.CloseAll(),
                delegate(StickyUiCommandResult result)
                {
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        completed(result);
                        return;
                    }
                    if (result.FinalSnapshots != null)
                        foreach (StickyUiFinalSnapshot finalSnapshot in
                            result.FinalSnapshots)
                        {
                            if (finalSnapshot == null) continue;
                            ApplyHostedStickySnapshot(
                                finalSnapshot.Snapshot,
                                finalSnapshot.Sequence, false, finalSnapshot.Facts,
                                finalSnapshot.Topology);
                            Hosted.RemoveNote(finalSnapshot.NoteId);
                            Placement.InvalidateEffective(
                                finalSnapshot.NoteId);
                        }
                    Dock.ClearHostedDockResizeSession();
                    completed(result);
                });
        }

        internal void ReloadAllHostedStickyRuntime()
        {
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in Notes.GetAll())
            {
                if (note == null || !note.Visible ||
                    restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    Dock.BuildDockChainOrderIncludingHidden(note);
                if (group.Count == 0) group.Add(note);
                foreach (StickyNoteData member in group)
                    if (member != null) restored.Add(member.Id);
                ShowHostedSticky(note, false, false);
            }
            Dock.RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ConfirmHostedStickyDelete(string noteId)
        {
            StickyNoteData note = Notes.Find(noteId);
            if (note == null || !IsHostedSticky(note)) return;
            if (MessageBox.Show(_pet,
                "确定删除这张便利贴吗？此操作无法撤销。", "删除便利贴",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                DialogResult.Yes) DeleteStickyNote(note);
        }

        internal void RecoverFailedHostedStickyWindow(StickyNoteData note)
        {
            if (note == null) return;
            HandleHostedStickyFailure(new string[] { note.Id },
                "deferred-sticky-restore",
                StickyUiCommandResult.Failed(new InvalidOperationException(
                    "Hosted sticky restore did not complete.")));
        }

        internal void ShowHostedSticky(StickyNoteData note, bool focusEditor)
        {
            ShowHostedSticky(note, focusEditor, true);
        }

        internal void ShowHostedSticky(StickyNoteData note, bool focusEditor,
            bool persistVisibility)
        {
            if (note == null) return;
            if (Dock.DeferDockMutation(note.Id, () => ShowHostedSticky(Notes.Find(note.Id), focusEditor, persistVisibility))) return;
            List<StickyNoteData> storedDockOrder =
                Dock.BuildDockChainOrderIncludingHidden(note);
            bool anyHiddenDockMember = storedDockOrder.Exists(
                delegate(StickyNoteData member)
                {
                    return !member.Visible;
                });
            if (StickyDockOperations.ShouldRestoreWholeDockComponent(
                storedDockOrder.Count, anyHiddenDockMember))
            {
                if (Dock.TryRestoreHostedDockComponent(storedDockOrder, note,
                    focusEditor, persistVisibility))
                    return;
                return;
            }
            if (PostHostedStickyShow(note, focusEditor)) return;
            StartHostedSticky(note, focusEditor);
            if (!focusEditor && persistVisibility) Notes.SaveAsync();
            RefreshNoteTabs();
        }

        internal void ReloadImportedStickyRuntime(
            StickyImportMergeResult merge)
        {
            if (merge == null || merge.Actions == null) return;
            HashSet<string> requested = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyImportAction action in merge.Actions)
            {
                if (action == null ||
                    String.IsNullOrEmpty(action.ResultNoteId) ||
                    !requested.Add(action.ResultNoteId)) continue;
                StickyNoteData note = Notes.Find(action.ResultNoteId);
                if (note == null || !note.Visible || IsHostedSticky(note))
                    continue;
                // Imported windows use the same restore path as startup and
                // reopen. Existing hosted sessions remain untouched because
                // merge planning never replaces current NoteIds.
                ShowHostedSticky(note, false, false);
            }
            Dock.RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        internal void ReorderStickyNoteTab(StickyNoteData note,
            int destinationIndex)
        {
            Notes.ReorderHidden(note, destinationIndex);
            _noteTabsSignature = String.Empty;
            RefreshNoteTabs();
        }

        private void CollapseAllStickyNotes()
        {
            if (Dock.DeferDockMutation(null, CollapseAllStickyNotes)) return;
            Dock.CancelHostedDockRestores();
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in Notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    Dock.BuildDockChainOrderIncludingHidden(note);
                if (group.Count == 0) group.Add(note);
                foreach (StickyNoteData member in group)
                {
                    handled.Add(member.Id);
                    member.Visible = false;
                    PostHostedStickyHide(member);
                }
            }
            Notes.SaveAsync();
            Dock.RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ExpandAllStickyNoteTabs()
        {
            List<StickyNoteData> hidden = Notes.GetHiddenInTabOrder();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in hidden)
            {
                if (restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    Dock.BuildDockChainOrderIncludingHidden(note);
                foreach (StickyNoteData member in group)
                    restored.Add(member.Id);
                ShowHostedSticky(note, false);
            }
            if (hidden.Count > 0)
                PostHostedStickyCommand(StickyUiCommand.FocusPrimaryInput(
                    hidden[0].Id),
                    delegate(StickyUiCommandResult result) { });
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private bool TryGetPetDerivedDisplayContext(
            out WindowFacts petFacts,
            out DisplaySurfaceSnapshot surface,
            out Rectangle workArea,
            out SideTabPhysicalMetrics metrics)
        {
            petFacts = null;
            surface = null;
            workArea = Rectangle.Empty;
            metrics = null;

            DisplayTopologySnapshot topology = CurrentTopologySnapshot();

            if (topology == null || !_pet.IsHandleCreated ||
                _pet.Handle == IntPtr.Zero)
                return false;

            petFacts = CapturePetWindowFacts(topology);

            if (petFacts == null ||
                petFacts.TopologyGeneration != topology.Generation ||
                petFacts.Dpi <= 0)
                return false;

            surface = topology.FindByRuntimeGdiName(
                petFacts.RuntimeGdiName);

            if (surface == null) return false;

            workArea = new Rectangle(
                surface.WorkArea.Left,
                surface.WorkArea.Top,
                surface.WorkArea.Width,
                surface.WorkArea.Height);

            metrics = SideTabPhysicalMetrics.ForDpi(petFacts.Dpi);

            return true;
        }

        internal void RefreshNoteTabs()
        {
            ApplicationDiagnostics.WriteWindowLayerEvent("RefreshNoteTabs",
                "structural");
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            // Side tabs have their own persistent order.  Sorting them by the
            // note's modified time here used to undo every successful drag.
            List<StickyNoteData> hiddenData = Notes.GetHiddenInTabOrder();
            List<SideTabSnapshot> hidden = new List<SideTabSnapshot>();
            foreach (StickyNoteData note in hiddenData)
                hidden.Add(SideTabSnapshot.FromData(note));
            WindowFacts petFacts;
            DisplaySurfaceSnapshot petSurface;
            Rectangle workArea;
            SideTabPhysicalMetrics metrics;

            if (!TryGetPetDerivedDisplayContext(
                out petFacts,
                out petSurface,
                out workArea,
                out metrics))
                return;

            _leftNoteTabs.ApplyPhysicalMetrics(metrics);
            _rightNoteTabs.ApplyPhysicalMetrics(metrics);

            DockRect petRect = new DockRect(
                petFacts.PhysicalBounds.Left,
                petFacts.PhysicalBounds.Top,
                petFacts.PhysicalBounds.Width,
                petFacts.PhysicalBounds.Height);
            DockRect workRect = new DockRect(
                workArea.Left, workArea.Top,
                workArea.Width, workArea.Height);
            int overlap = SideTabLayoutPolicy.CalculatePhysicalOverlap(
                petRect.Width, metrics);
            int leftCount = SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                hidden.Count, petRect, workRect, metrics.Width, overlap,
                metrics.WindowMarginX);

            StringBuilder signatureBuilder = new StringBuilder();
            foreach (SideTabSnapshot note in hidden)
            {
                signatureBuilder.Append(note.NoteId).Append('|')
                    .Append(note.DisplayTitle).Append('|')
                    .Append(note.ColorArgb).Append('\n');
            }
            signatureBuilder.Append("dpi=")
                .Append(metrics.Dpi)
                .Append('\n');
            signatureBuilder.Append("left=")
                .Append(leftCount)
                .Append('\n');
            string signature = signatureBuilder.ToString();
            if (String.Equals(signature, _noteTabsSignature,
                StringComparison.Ordinal))
            {
                PositionNoteTabs();
                ApplyNoteTabZOrder();
                return;
            }
            _noteTabsSignature = signature;

            int logicalWorkHeight =
                SideTabLayoutPolicy.PhysicalWorkHeightToLogical(
                    workArea.Height, metrics.Dpi);

            int logicalCapacity =
                SideTabLayoutPolicy.LogicalScreenCapacity(
                    logicalWorkHeight);

            DisplayDiagnostics.Trace("SideTabsLayout",
                "topology=" + petFacts.TopologyGeneration +
                " dpi=" + metrics.Dpi +
                " total=" + hidden.Count +
                " left=" + leftCount +
                " right=" + (hidden.Count - leftCount) +
                " petLeft=" + petRect.Left +
                " petRight=" + petRect.Right +
                " workLeft=" + workRect.Left +
                " workRight=" + workRect.Right +
                " stripWidth=" + metrics.Width +
                " overlap=" + overlap +
                " logicalCapacity=" + logicalCapacity +
                " surface=" + petSurface.RuntimeSurfaceId);
            List<SideTabSnapshot> left = hidden.GetRange(0, leftCount);
            List<SideTabSnapshot> right = hidden.GetRange(leftCount,
                hidden.Count - leftCount);
            _leftNoteTabs.SetNotes(left, 0);
            _rightNoteTabs.SetNotes(right, leftCount);
            PositionNoteTabs();
            ApplyNoteTabZOrder();
        }

        private bool IsStripCoveredByVisibleSticky(StickyNoteTabsForm tabs)
        {
            if (tabs == null || tabs.IsDisposed || !tabs.Visible) return false;
            Rectangle stripBounds = tabs.Bounds;
            foreach (StickyNoteData note in Notes.GetAll())
            {
                if (note == null || !note.Visible) continue;
                WindowFacts facts = Placement.GetEffective(note.Id);
                if (facts == null) continue;
                PhysicalRect actual = facts.PhysicalBounds;
                Rectangle noteBounds = new Rectangle(actual.Left, actual.Top,
                    actual.Width, actual.Height);
                if (stripBounds.IntersectsWith(noteBounds)) return true;
            }
            return false;
        }

        private void ApplyNoteTabZOrder()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            bool leftCovered = IsStripCoveredByVisibleSticky(_leftNoteTabs);
            bool rightCovered = IsStripCoveredByVisibleSticky(_rightNoteTabs);
            if (!_leftTabsCovered.HasValue || _leftTabsCovered.Value != leftCovered)
            {
                _leftTabsCovered = leftCovered;
                _leftNoteTabs.TopMost =
                    StickyNoteWindowRules.ShouldKeepSideTabsTopMost(leftCovered);
                if (!leftCovered && _leftNoteTabs.Visible)
                    _leftNoteTabs.BringToFront();
                ApplicationDiagnostics.WriteWindowLayerEvent("SideTabsLeft",
                    leftCovered ? "covered" : "clear");
            }
            if (!_rightTabsCovered.HasValue ||
                _rightTabsCovered.Value != rightCovered)
            {
                _rightTabsCovered = rightCovered;
                _rightNoteTabs.TopMost =
                    StickyNoteWindowRules.ShouldKeepSideTabsTopMost(rightCovered);
                if (!rightCovered && _rightNoteTabs.Visible)
                    _rightNoteTabs.BringToFront();
                ApplicationDiagnostics.WriteWindowLayerEvent("SideTabsRight",
                    rightCovered ? "covered" : "clear");
            }
        }

        internal void PositionNoteTabs()
        {
            if (_leftNoteTabs == null ||
                _rightNoteTabs == null ||
                !_pet.IsHandleCreated ||
                IsDisposed ||
                _positioningNoteTabs)
                return;

            WindowFacts petFacts;
            DisplaySurfaceSnapshot surface;
            Rectangle work;
            SideTabPhysicalMetrics metrics;

            if (!TryGetPetDerivedDisplayContext(
                out petFacts,
                out surface,
                out work,
                out metrics))
                return;

            Rectangle petBounds = new Rectangle(
                petFacts.PhysicalBounds.Left,
                petFacts.PhysicalBounds.Top,
                petFacts.PhysicalBounds.Width,
                petFacts.PhysicalBounds.Height);
            DockRect petRect = new DockRect(
                petBounds.Left, petBounds.Top,
                petBounds.Width, petBounds.Height);
            DockRect workRect = new DockRect(
                work.Left, work.Top, work.Width, work.Height);
            int total = _leftNoteTabs.Controls.Count +
                _rightNoteTabs.Controls.Count;
            int overlap = SideTabLayoutPolicy.CalculatePhysicalOverlap(
                petBounds.Width, metrics);
            int desiredLeftCount =
                SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                    total, petRect, workRect, metrics.Width, overlap,
                    metrics.WindowMarginX);

            if (_leftNoteTabs.Controls.Count != desiredLeftCount ||
                _rightNoteTabs.Controls.Count !=
                    total - desiredLeftCount)
            {
                _noteTabsSignature = String.Empty;
                RefreshNoteTabs();
                return;
            }

            _positioningNoteTabs = true;

            try
            {
                _leftNoteTabs.ApplyPhysicalMetrics(metrics);
                _rightNoteTabs.ApplyPhysicalMetrics(metrics);

                _leftNoteTabs.ShowNear(petBounds, work);
                _rightNoteTabs.ShowNear(petBounds, work);

                ApplyNoteTabZOrder();
            }
            finally
            {
                _positioningNoteTabs = false;
            }
        }

        internal void ShowStickyNotesManager()
        {
            bool createRequested = false;
            bool fullRestoreRequested = false;
            StickyNoteData showRequested = null;
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                delegate { return Notes.GetAll(); },
                new StickyNotesManagerCommands
                {
                    HideNote = delegate(StickyNoteData note)
                    { Dock.HideStickyNote(note); },
                    DeleteNote = delegate(StickyNoteData note,
                        Action<bool> completed)
                    { DeleteStickyNote(note, completed); },
                    CollapseAll = CollapseAllStickyNotes,
                    ExpandAll = ExpandAllStickyNoteTabs,
                    TileAll = delegate
                    {
                        QueueStickyWindowAction(
                            Dock.ExpandAndTileAllStickyNotesToPetScreen,
                            "sticky-manager-expand-and-tile");
                    },
                    ExportBackup = _pet.ExportStickyNotesBackup,
                    PrepareImport = _pet.PrepareStickyNotesImport,
                    ConfirmImport = _pet.CommitStickyNotesImport,
                    FullRestore = _pet.RestoreStickyNotesBackup
                }))
            {
                _pet._windowLayers.ShowModal(_pet, manager);
                createRequested = manager.CreateRequested;
                showRequested = manager.ShowRequested;
                fullRestoreRequested = manager.FullRestoreRequested;
            }
            if (fullRestoreRequested)
                _pet.RestoreStickyNotesBackup();
            else if (createRequested)
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-manager-create");
            else if (showRequested != null)
                QueueStickyWindowAction(delegate
                {
                    ShowHostedSticky(showRequested, true);
                }, "sticky-manager-show");
            RefreshMenuText();
        }
    }
}
