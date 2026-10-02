using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private sealed class ReminderCoordinatorCheckResult
        {
            internal bool MultipleLinkedReminderOk;
            internal bool ConcreteDateTimeOk;
            internal bool BannerTickThrottleOk;
            internal bool RuntimeOwnershipOk;
            internal bool DueBubblePersistentOk;
            internal bool DueBubbleUsesOwnSizeOk;
            internal bool DueBubbleReplacementOk;
            internal bool PreAlertBubbleProtectionOk;
            internal bool ExpiredAtLaunchDiscardedOk;
        }

        private static ReminderCoordinatorCheckResult
            RunReminderCoordinatorChecks(DateTime reminderBaseUtc)
        {
            ReminderCoordinatorCheckResult result =
                new ReminderCoordinatorCheckResult();
            ReminderSchedule sameNoteSchedule = new ReminderSchedule();
            sameNoteSchedule.Add(reminderBaseUtc.AddMinutes(30), "later",
                "shared-note");
            ReminderItem earlierLinked = sameNoteSchedule.Add(
                reminderBaseUtc.AddMinutes(10), "earlier", "shared-note");
            sameNoteSchedule.Add(reminderBaseUtc.AddMinutes(20), "unrelated",
                "other-note");
            result.MultipleLinkedReminderOk = Object.ReferenceEquals(
                sameNoteSchedule.FindBySourceNoteId("shared-note"),
                earlierLinked) &&
                sameNoteSchedule.RemoveBySourceNoteId("shared-note") == 2 &&
                sameNoteSchedule.Count == 1 &&
                sameNoteSchedule.FindBySourceNoteId("shared-note") == null &&
                sameNoteSchedule.FindBySourceNoteId("other-note") != null;
            ReminderSchedule concreteSchedule = new ReminderSchedule();
            DateTime concreteLocal = DateTime.Now.AddMinutes(10);
            ReminderItem concrete = concreteSchedule.Add(
                concreteLocal.ToUniversalTime(), "concrete");
            result.ConcreteDateTimeOk = Math.Abs(
                (concrete.DeadlineUtc.ToLocalTime() -
                    concreteLocal).TotalSeconds) < 1;
            result.BannerTickThrottleOk = RunStickyReminderHostChecks();
            result.RuntimeOwnershipOk = RunReminderRuntimeChecks();
            result.DueBubblePersistentOk =
                ReminderRules.DueReminderBubbleDurationMilliseconds == 0;
            result.DueBubbleUsesOwnSizeOk = Math.Abs(
                PetForm.DueReminderBubbleFontSizePoints(100) -
                KeyboardOverlayForm.TextFontSizePoints(100)) < 0.2F;
            result.DueBubbleReplacementOk =
                !PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.Feedback, false) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.ReminderDue, false) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.DailyGreeting, true) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.Hover,
                    PetMessageKind.Feedback, false) &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.DailyGreeting, false);
            result.PreAlertBubbleProtectionOk =
                !PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.Feedback, false) &&
                PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.ReminderDue, false) &&
                PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.Feedback, true);
            ReminderItem expired = new ReminderItem(
                DateTime.UtcNow.AddMinutes(-1), "已错过");
            ReminderItem future = new ReminderItem(
                DateTime.UtcNow.AddMinutes(1), "仍有效");
            DateTime launchGate = DateTime.UtcNow;
            result.ExpiredAtLaunchDiscardedOk =
                !ReminderRules.ShouldRestoreReminderAfterLaunch(
                    expired, launchGate) &&
                ReminderRules.ShouldRestoreReminderAfterLaunch(
                    future, launchGate);
            return result;
        }
    }
}
