using System;
using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using W = System.Windows;

namespace PennyPet
{
    internal sealed partial class StickyNoteWindow
    {
        private readonly DispatcherTimer _linkRefreshTimer;
        private bool _applyingAutoLinkFormat;
        private readonly List<OrdinaryLinkRange> _ordinaryLinkRanges =
            new List<OrdinaryLinkRange>();

        private void QueueOrdinaryLinkRefresh()
        {
            if (_disposed || Data.IsTodoList || Data.IsSchedule) return;
            _linkRefreshTimer.Stop();
            _linkRefreshTimer.Start();
        }

        private void RefreshOrdinaryLinks(bool saveAfterFormatting)
        {
            if (_disposed || Data.IsTodoList || Data.IsSchedule ||
                _editorTextCompositionActive) return;
            _applyingAutoLinkFormat = true;
            try
            {
                ClearOrdinaryLinkFormatting();
                foreach (Paragraph paragraph in CollectParagraphs(
                    _editor.Document.Blocks))
                {
                    string text = new TextRange(paragraph.ContentStart,
                        paragraph.ContentEnd).Text ?? String.Empty;
                    foreach (StickyLinkMatch match in
                        WindowsStickyNoteLinkDetector.Find(text))
                    {
                        TextPointer start = PointerAtCharacterOffset(
                            paragraph.ContentStart, paragraph.ContentEnd,
                            match.Start);
                        TextPointer end = PointerAtCharacterOffset(
                            paragraph.ContentStart, paragraph.ContentEnd,
                            match.Start + match.Length);
                        if (start == null || end == null ||
                            start.CompareTo(end) >= 0) continue;
                        TextRange range = new TextRange(start, end);
                        range.ApplyPropertyValue(
                            TextElement.ForegroundProperty,
                            System.Windows.Media.Brushes.DodgerBlue);
                        range.ApplyPropertyValue(
                            Inline.TextDecorationsProperty,
                            W.TextDecorations.Underline);
                        _ordinaryLinkRanges.Add(new OrdinaryLinkRange(start,
                            end, match.Target, match.IsFileTarget));
                    }
                }
                _editor.ToolTip = _ordinaryLinkRanges.Count == 0 ? null :
                    "单击蓝色链接即可打开";
            }
            finally { _applyingAutoLinkFormat = false; }
            if (saveAfterFormatting) ScheduleSave();
        }

        private void ClearOrdinaryLinkFormatting()
        {
            System.Windows.Media.Brush text = OpaqueBrush(EffectiveTextColor());
            foreach (OrdinaryLinkRange link in _ordinaryLinkRanges)
            {
                try
                {
                    if (link.Start.CompareTo(link.End) >= 0) continue;
                    TextRange range = new TextRange(link.Start, link.End);
                    range.ApplyPropertyValue(TextElement.ForegroundProperty,
                        text);
                    range.ApplyPropertyValue(Inline.TextDecorationsProperty,
                        null);
                }
                catch (InvalidOperationException) { }
            }
            _ordinaryLinkRanges.Clear();
        }

        private static IList<Paragraph> CollectParagraphs(
            BlockCollection blocks)
        {
            List<Paragraph> result = new List<Paragraph>();
            CollectParagraphs(blocks, result);
            return result;
        }

        private static void CollectParagraphs(BlockCollection blocks,
            List<Paragraph> result)
        {
            foreach (Block block in blocks)
            {
                Paragraph paragraph = block as Paragraph;
                if (paragraph != null) result.Add(paragraph);
                Section section = block as Section;
                if (section == null) continue;
                CollectParagraphs(section.Blocks, result);
            }
        }

        private static TextPointer PointerAtCharacterOffset(TextPointer start,
            TextPointer end, int characterOffset)
        {
            if (characterOffset < 0) return null;
            TextPointer position = start;
            int remaining = characterOffset;
            while (position != null && position.CompareTo(end) <= 0)
            {
                if (position.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    int runLength = position.GetTextRunLength(
                        LogicalDirection.Forward);
                    if (remaining <= runLength)
                        return position.GetPositionAtOffset(remaining,
                            LogicalDirection.Forward);
                    remaining -= runLength;
                }
                if (position.CompareTo(end) == 0) break;
                position = position.GetNextContextPosition(
                    LogicalDirection.Forward);
            }
            return remaining == 0 ? end : null;
        }

        private OrdinaryLinkRange OrdinaryLinkAt(TextPointer position)
        {
            if (position == null) return null;
            foreach (OrdinaryLinkRange link in _ordinaryLinkRanges)
            {
                try
                {
                    if (link.Start.CompareTo(position) <= 0 &&
                        link.End.CompareTo(position) > 0) return link;
                }
                catch (InvalidOperationException) { }
            }
            return null;
        }

        private void EditorPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Data.IsTodoList || Data.IsSchedule)
            {
                _editor.Cursor = Cursors.IBeam;
                return;
            }
            TextPointer position = _editor.GetPositionFromPoint(
                e.GetPosition(_editor), true);
            _editor.Cursor = OrdinaryLinkAt(position) == null
                ? Cursors.IBeam : Cursors.Hand;
        }

        private void EditorPreviewMouseLeftButtonUp(object sender,
            MouseButtonEventArgs e)
        {
            if (Data.IsTodoList || Data.IsSchedule ||
                !_editor.Selection.IsEmpty) return;
            TextPointer position = _editor.GetPositionFromPoint(
                e.GetPosition(_editor), true);
            OrdinaryLinkRange link = OrdinaryLinkAt(position);
            if (link == null) return;
            e.Handled = true;
            OpenOrdinaryLink(link);
        }

        private void OpenOrdinaryLink(OrdinaryLinkRange link)
        {
            if (link == null || String.IsNullOrWhiteSpace(link.Target)) return;
            Exception error;
            StickyLinkOpenResult result = StickyLinkService.Open(link.Target,
                link.IsFileTarget,
                delegate(StickyLinkOpenRisk risk, string target)
                {
                    return W.MessageBox.Show(this,
                        StickyLinkPolicy.ConfirmationMessage(risk, target),
                        "确认打开可能有风险的路径",
                        W.MessageBoxButton.YesNo,
                        W.MessageBoxImage.Warning,
                        W.MessageBoxResult.No) == W.MessageBoxResult.Yes;
                }, out error);
            if (result == StickyLinkOpenResult.Missing)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            if (result == StickyLinkOpenResult.Failed)
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-link-open", error);
        }

        private sealed class OrdinaryLinkRange
        {
            internal OrdinaryLinkRange(TextPointer start, TextPointer end,
                string target, bool fileTarget)
            {
                Start = start;
                End = end;
                Target = target;
                IsFileTarget = fileTarget;
            }

            internal TextPointer Start { get; private set; }
            internal TextPointer End { get; private set; }
            internal string Target { get; private set; }
            internal bool IsFileTarget { get; private set; }
        }

    }
}
