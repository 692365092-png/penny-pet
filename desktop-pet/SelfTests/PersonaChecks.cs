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

        private static bool RunPersonaRuntimeCatalogCheck()
        {
            List<PetPersonaEntry> all = new List<PetPersonaEntry>();
            all.AddRange(PetPersonaRuntimeCatalog.SmallTalkLoopable);
            all.AddRange(PetPersonaRuntimeCatalog.SmallTalkMeaningful);
            all.AddRange(PetPersonaRuntimeCatalog.DaypartMeaningful);
            if (all.Count == 0) return false;
            Dictionary<string, string> idToBody =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PetPersonaEntry entry in all)
            {
                if (entry == null || !entry.Approved) return false;
                if (String.IsNullOrWhiteSpace(entry.StableContentId) ||
                    String.IsNullOrWhiteSpace(entry.CanonicalBody))
                    return false;
                string existingBody;
                if (idToBody.TryGetValue(entry.StableContentId,
                    out existingBody))
                {
                    if (!String.Equals(existingBody, entry.CanonicalBody,
                        StringComparison.Ordinal)) return false;
                }
                else idToBody[entry.StableContentId] = entry.CanonicalBody;
                if (entry.EligibleContexts == 0) return false;
                if (entry.RepeatClass != PetPersonaRepeatClass.Loopable &&
                    entry.RepeatClass != PetPersonaRepeatClass.Meaningful)
                    return false;
                if (entry.ContextClass != PetPersonaContextClass.ContextFree &&
                    entry.ContextClass != PetPersonaContextClass.Contextual)
                    return false;
                if ((int)entry.Intent < (int)PetSentenceIntent.Statement ||
                    (int)entry.Intent > (int)PetSentenceIntent.Serious)
                    return false;
                if ((int)entry.Category <
                        (int)PetPersonaCategory.General ||
                    (int)entry.Category >
                        (int)PetPersonaCategory.Inspiration)
                    return false;
            }
            PetPersonaEntry noonMeal = FindPersonaEntry(
                PetPersonaRuntimeCatalog.DaypartMeaningful, "PENNY-000004");
            PetPersonaEntry question = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkLoopable, "PENNY-000002");
            PetPersonaEntry songEnding = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkMeaningful, "PENNY-000007");
            PetPersonaEntry inspiration = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkMeaningful, "PENNY-000019");
            PetPersonaEntry legacyLoop = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkLoopable,
                "SMALLTALK-LOOP-IN");
            PetPersonaEntry legacyMeal = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                "MEANINGFUL-MEAL");
            PetPersonaEntry lyricSong = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkMeaningful, "PENNY-000005");
            PetPersonaEntry legacyEn = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkLoopable,
                "SMALLTALK-LOOP-EN");
            return PetPersonaRuntimeCatalog.SmallTalkLoopable.Length == 11 &&
                PetPersonaRuntimeCatalog.SmallTalkMeaningful.Length == 20 &&
                noonMeal != null &&
                (noonMeal.EligibleContexts & PetPersonaContext.Noon) != 0 &&
                (noonMeal.EligibleContexts & PetPersonaContext.SmallTalk) == 0 &&
                FindPersonaEntry(PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                    "PENNY-000004") == null &&
                question != null && question.PreserveEnding &&
                String.Equals(question.CanonicalBody, "嗯？",
                    StringComparison.Ordinal) &&
                FindPersonaEntry(
                    PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                    "PENNY-000006").CanonicalBody.EndsWith("。",
                        StringComparison.Ordinal) &&
                FindPersonaEntry(
                    PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                    "PENNY-000008").CanonicalBody.EndsWith("。",
                        StringComparison.Ordinal) &&
                FindPersonaEntry(
                    PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                    "PENNY-000017").CanonicalBody.EndsWith("。",
                        StringComparison.Ordinal) &&
                FindPersonaEntry(
                    PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                    "PENNY-000019").CanonicalBody.EndsWith("。",
                        StringComparison.Ordinal) &&
                FindPersonaEntry(
                    PetPersonaRuntimeCatalog.SmallTalkMeaningful,
                    "PENNY-000020").CanonicalBody.EndsWith("。",
                        StringComparison.Ordinal) &&
                legacyLoop != null &&
                legacyMeal != null &&
                lyricSong != null && lyricSong.PreserveEnding &&
                String.Equals(lyricSong.CanonicalBody,
                    "我们现在还在一起会是怎样~", StringComparison.Ordinal) &&
                legacyEn != null && legacyEn.PreserveEnding &&
                String.Equals(legacyEn.CanonicalBody, "嗯？",
                    StringComparison.Ordinal) &&
                ContainsPersonaBody(PetPersonaRuntimeCatalog.SmallTalkLoopable,
                    "嗯？") &&
                songEnding != null && songEnding.PreserveEnding &&
                songEnding.CanonicalBody.EndsWith("~",
                    StringComparison.Ordinal) &&
                inspiration != null &&
                inspiration.Category == PetPersonaCategory.Inspiration;
        }

        private static PetPersonaEntry FindPersonaEntry(
            IList<PetPersonaEntry> entries, string stableContentId)
        {
            if (entries == null) return null;
            foreach (PetPersonaEntry entry in entries)
                if (entry != null && String.Equals(
                    entry.StableContentId, stableContentId,
                    StringComparison.Ordinal))
                    return entry;
            return null;
        }

        private static bool ContainsPersonaBody(
            IList<PetPersonaEntry> entries, string body)
        {
            if (entries == null) return false;
            foreach (PetPersonaEntry entry in entries)
                if (entry != null && String.Equals(entry.CanonicalBody, body,
                    StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool RunSolarTermAttachmentCheck()
        {
            if (PetSolarTermAttachmentCatalog.Count != 5) return false;
            string[][] expected = new string[][]
            {
                new string[] { "春分", "PENNY-000013",
                    "春天最适合热聊，祝大家都有一个被爱包围的春分。" },
                new string[] { "大寒", "PENNY-000014", "大寒节气记得多保暖。" },
                new string[] { "小寒", "PENNY-000015", "祝大家小寒安康喜乐。" },
                new string[] { "冬至", "PENNY-000016", "冬至平安喜乐。" },
                new string[] { "大雪", "PENNY-000018",
                    "大雪，沉淀成成果的时刻，身心都要继续保暖。" }
            };
            foreach (string[] item in expected)
            {
                PetSolarTermAttachment value;
                if (!PetSolarTermAttachmentCatalog.TryGet(item[0],
                    out value)) return false;
                if (!String.Equals(value.StableContentId, item[1],
                    StringComparison.Ordinal)) return false;
                if (!String.Equals(value.Text, item[2],
                    StringComparison.Ordinal)) return false;
            }
            PetSolarTermAttachment unknown;
            return !PetSolarTermAttachmentCatalog.TryGet("立春",
                out unknown);
        }

        private static bool RunPersonaLyricAnimationCheck()
        {
            PetPersonaEntry song = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkMeaningful, "PENNY-000005");
            PetPersonaEntry fly = FindPersonaEntry(
                PetPersonaRuntimeCatalog.SmallTalkMeaningful, "PENNY-000007");
            if (song == null || fly == null) return false;
            return song.AnimationKind == PetPersonaAnimationKind.Guitar &&
                fly.AnimationKind == PetPersonaAnimationKind.Guitar &&
                PetAnimationController.WaitingRow == 6;
        }

        private static bool RunSmallTalkAnimationProtectionCheck()
        {
            foreach (PetPersonaEntry entry in
                PetPersonaRuntimeCatalog.SmallTalkLoopable)
                if (entry == null ||
                    entry.AnimationKind != PetPersonaAnimationKind.Hover)
                    return false;
            return RunProtectedInteractionCheck();
        }

        private static bool RunSolarPreservePlumbingCheck()
        {
            DailyBriefingSentence preserved = new DailyBriefingSentence(
                "大寒节气记得多保暖。", PetSentenceContentKind.Solar,
                PetSentenceIntent.Gentle, "PENNY-000014", true);
            string first = DailyBriefingComposer.ComposeSentences(
                new DateTime(2026, 9, 3),
                new DailyBriefingSentence[] { preserved });
            string second = DailyBriefingComposer.ComposeSentences(
                new DateTime(2026, 12, 21),
                new DailyBriefingSentence[] { preserved });
            bool preservedStable = first == "大寒节气记得多保暖。" &&
                second == first;

            DailyBriefingSentence normal = new DailyBriefingSentence(
                "今天是什么日子", PetSentenceContentKind.Solar,
                PetSentenceIntent.Gentle, "X-NORMAL", false);
            string ending = DailyBriefingComposer.ComposeSentences(
                new DateTime(2026, 9, 3),
                new DailyBriefingSentence[] { normal });
            bool normalUsesEnding = ending != "今天是什么日子" &&
                (ending.EndsWith("。", StringComparison.Ordinal) ||
                    ending.EndsWith("喔～", StringComparison.Ordinal) ||
                    ending.EndsWith("哦～", StringComparison.Ordinal));

            DailyBriefingSentence en = new DailyBriefingSentence(
                "嗯？", PetSentenceContentKind.SmallTalk,
                PetSentenceIntent.Question, "PENNY-000002", true);
            string enOutput = DailyBriefingComposer.ComposeSentences(
                new DateTime(2026, 9, 3),
                new DailyBriefingSentence[] { en });
            DailyBriefingSentence lyric = new DailyBriefingSentence(
                "我们现在还在一起会是怎样~", PetSentenceContentKind.SmallTalk,
                PetSentenceIntent.Statement, "PENNY-000005", true);
            string lyricOutput = DailyBriefingComposer.ComposeSentences(
                new DateTime(2026, 9, 3),
                new DailyBriefingSentence[] { lyric });
            return preservedStable && normalUsesEnding &&
                enOutput == "嗯？" && lyricOutput ==
                    "我们现在还在一起会是怎样~";
        }
    }
}
