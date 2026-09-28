using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static void RunPc2PersistencePause(string root, List<string> evidence)
        {
            using (var scene = new Pc2Scene(root, "R25-persistence-pause", true))
            {
                scene.Start();
                StickyNoteData note = scene.Notes[0];
                scene.Send(StickyUiCommand.EnsureSession(
                    StickyNoteUiSnapshot.Capture(note), null, scene.Topology));
                scene.Send(StickyUiCommand.Show(note.Id, false, scene.Topology,
                    StickyPlacementRecovery.SelectForShow(note, scene.Topology)));
                scene.Host.SetCommandHandler(command =>
                {
                    var sessions = (Dictionary<string, StickyWindowSession>)Pc2Get(scene.Host, "_sessions");
                    StickyWindowSession session = sessions[note.Id];
                    var window = (StickyNoteWindow)Pc2Get(session, "_window");
                    var view = (StickyNoteWindow.StickyTextContentView)Pc2Get(window, "_contentView");
                    IntPtr hwnd = session.PlacementHwnd;
                    view.EditorTextCompositionStarted(null, null);
                    var refused = (StickyUiCommandResult)Pc2Call(scene.Host, "PreparePersistenceSessions");
                    Pc2Assert(refused.Status == StickyUiCommandStatus.NotAccepted && window.IsEnabled,
                        "active IME cannot be frozen or captured for exit");
                    view.SetEditorPlainText("最后一笔中文输入");
                    view.EditorTextCompositionCompleted(null, null);
                    var prepared = (StickyUiCommandResult)Pc2Call(scene.Host, "PreparePersistenceSessions");
                    Pc2Assert(prepared.Status == StickyUiCommandStatus.Handled &&
                        prepared.FinalSnapshots.Length == 1 &&
                        prepared.FinalSnapshots[0].Snapshot.Text.Contains("最后一笔中文输入") &&
                        !window.IsEnabled && session.PlacementHwnd == hwnd,
                        "prepare flushes final text and retains the disabled HWND");
                    Pc2Call(scene.Host, "HandleCommand", new StickyUiCommand(
                        StickyUiCommandKind.ResumeAfterPersistence, String.Empty, false));
                    Pc2Assert(window.IsEnabled && session.PlacementHwnd == hwnd &&
                        ReferenceEquals(session, sessions[note.Id]),
                        "cancel resumes the same editor/session/undo lifetime");
                    view.SetEditorPlainText("取消退出后继续编辑");
                    var again = (StickyUiCommandResult)Pc2Call(scene.Host, "PreparePersistenceSessions");
                    Pc2Assert(again.FinalSnapshots[0].Snapshot.Text.Contains("取消退出后继续编辑"),
                        "a second exit captures edits made after cancellation");
                    Pc2Call(scene.Host, "HandleCommand", new StickyUiCommand(
                        StickyUiCommandKind.ResumeAfterPersistence, String.Empty, false));
                    return StickyUiCommandResult.Handled();
                });
                StickyUiCommandResult result = scene.Send(new StickyUiCommand(
                    StickyUiCommandKind.PreparePersistence, String.Empty, false));
                scene.Host.SetCommandHandler(command =>
                    (StickyUiCommandResult)Pc2Call(scene.Host, "HandleCommand", command));
                Pc2Assert(result.Status == StickyUiCommandStatus.Handled,
                    "persistence pause probe: " + result.Error);
                evidence.Add("R25: IME preflight, final content capture, retained HWND, cancel and repeated exit passed.");
            }
        }
    }
}
