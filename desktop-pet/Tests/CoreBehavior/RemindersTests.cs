using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {

        [TestMethod]
        public void ReminderSchedule_SortsAndReplacesWithoutDesktopState()
        {
            DateTime baseline = DateTime.UtcNow.AddHours(1);
            ReminderSchedule schedule = new ReminderSchedule();
            ReminderItem later = schedule.Add(baseline.AddMinutes(10),
                "later", "note-a", 12F, true);
            ReminderItem earlier = schedule.Add(baseline, "earlier",
                "note-b", 10.5F, false);

            Assert.AreSame(earlier, schedule.Next);
            Assert.AreSame(later, schedule.NextPreAlert);
            Assert.AreSame(later, schedule.FindBySourceNoteId("NOTE-A"));

            ReminderItem replacement = schedule.Replace(later,
                baseline.AddMinutes(-10), "replacement", 14F, false);
            Assert.AreSame(replacement, schedule.Next);
            Assert.AreEqual("note-a", replacement.SourceNoteId);
            Assert.IsNull(schedule.NextPreAlert);
        }

        [TestMethod]
        public void ReminderSchedule_RemovesOnlyOrphanedLinkedNotes()
        {
            DateTime baseline = DateTime.UtcNow.AddHours(1);
            ReminderSchedule schedule = new ReminderSchedule();
            schedule.Add(baseline, "linked", "note-a");
            schedule.Add(baseline.AddMinutes(1), "standalone");
            schedule.Add(baseline.AddMinutes(2), "orphan", "note-missing");

            int removed = schedule.RemoveLinkedNotesNotIn(
                new HashSet<string>(new[] { "note-a" },
                    StringComparer.OrdinalIgnoreCase));

            Assert.AreEqual(1, removed);
            Assert.IsNotNull(schedule.FindBySourceNoteId("note-a"));
            Assert.IsNull(schedule.FindBySourceNoteId("note-missing"));
            Assert.AreEqual(2, schedule.Count);
        }

        [TestMethod]
        public void ReminderCoordinator_ExpressesTimingRulesAsPureFunctions()
        {
            ReminderItem enabled = new ReminderItem(
                DateTime.UtcNow.AddMinutes(2), "enabled", null, 10.5F, true);
            Assert.IsTrue(ReminderRules.IsPreAlertWindow(
                TimeSpan.FromSeconds(20)));
            Assert.IsFalse(ReminderRules.IsPreAlertWindow(
                TimeSpan.FromSeconds(21)));
            Assert.IsTrue(ReminderRules.ShouldShowPreAlert(enabled,
                TimeSpan.FromSeconds(5)));
        }
    }
}
