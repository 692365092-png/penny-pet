using System;

namespace PennyPet
{
    internal enum PetPersonaRepeatClass
    {
        Loopable,
        Meaningful
    }

    internal enum PetPersonaContextClass
    {
        ContextFree,
        Contextual
    }

    [Flags]
    internal enum PetPersonaContext
    {
        SmallTalk = 1,
        Morning = 2,
        Noon = 4,
        Afternoon = 8,
        Evening = 16,
        LateNight = 32
    }

    internal enum PetPersonaCategory
    {
        General,
        Greeting,
        Care,
        Playful,
        Work,
        Rest,
        Music,
        Catchphrase,
        Trivia,
        FanReference,
        Persona
    }

    // Minimal runtime entry consumed by the Daypart and SmallTalk selectors.
    // Content governance, cleaning and tagging live outside this runtime model.
    internal sealed class PetPersonaEntry
    {
        internal PetPersonaEntry(string stableContentId,
            string canonicalBody, PetPersonaCategory category,
            PetSentenceIntent intent, PetPersonaContext eligibleContexts,
            PetPersonaRepeatClass repeatClass,
            PetPersonaContextClass contextClass, bool preserveEnding,
            bool approved)
        {
            StableContentId = stableContentId ?? String.Empty;
            CanonicalBody = canonicalBody ?? String.Empty;
            Category = category;
            Intent = intent;
            EligibleContexts = eligibleContexts;
            RepeatClass = repeatClass;
            ContextClass = contextClass;
            PreserveEnding = preserveEnding;
            Approved = approved;
        }

        internal string StableContentId { get; private set; }
        internal string CanonicalBody { get; private set; }
        internal PetPersonaCategory Category { get; private set; }
        internal PetSentenceIntent Intent { get; private set; }
        internal PetPersonaContext EligibleContexts { get; private set; }
        internal PetPersonaRepeatClass RepeatClass { get; private set; }
        internal PetPersonaContextClass ContextClass { get; private set; }
        internal bool PreserveEnding { get; private set; }
        internal bool Approved { get; private set; }

        internal static PetPersonaEntry CreateApproved(string stableContentId,
            string canonicalBody, PetPersonaCategory category,
            PetSentenceIntent intent, PetPersonaContext eligibleContexts,
            PetPersonaRepeatClass repeatClass,
            PetPersonaContextClass contextClass, bool preserveEnding)
        {
            return new PetPersonaEntry(stableContentId, canonicalBody,
                category, intent, eligibleContexts, repeatClass,
                contextClass, preserveEnding, true);
        }
    }

    // Temporary development catalog. It keeps the runtime architecture honest
    // while the real Approved Persona Corpus is produced by the data pipeline.
    internal static class PetPersonaTempCatalog
    {
        internal static readonly PetPersonaEntry[] SmallTalkLoopable =
        {
            Loopable("SMALLTALK-LOOP-IN", "我在。"),
            Loopable("SMALLTALK-LOOP-QUESTION", "怎么了？"),
            Loopable("SMALLTALK-LOOP-EN", "嗯？"),
            Loopable("SMALLTALK-LOOP-AGAIN", "又来戳我了。"),
            Loopable("SMALLTALK-LOOP-SEE", "好啦，看见你了。"),
            Loopable("SMALLTALK-LOOP-KNOW", "我知道你在。"),
            Loopable("SMALLTALK-LOOP-CALL", "有事就叫我。"),
            Loopable("SMALLTALK-LOOP-POKE", "还戳呀。")
        };

        internal static readonly PetPersonaEntry[] SmallTalkMeaningful =
        {
            Meaningful("MEANINGFUL-MOVE", "别坐太久，起来动一动。",
                PetPersonaContext.SmallTalk),
            Meaningful("MEANINGFUL-WATER", "水还是要喝的。",
                PetPersonaContext.SmallTalk),
            Meaningful("MEANINGFUL-MEAL", "忙归忙，饭还是要吃。",
                PetPersonaContext.SmallTalk),
            Meaningful("MEANINGFUL-RETHINK", "卡住了就先换个思路。",
                PetPersonaContext.SmallTalk),
            Meaningful("MEANINGFUL-SLOW", "不急，慢慢来。",
                PetPersonaContext.SmallTalk),
            Meaningful("MEANINGFUL-ENOUGH", "今天也不用什么都做完。",
                PetPersonaContext.SmallTalk),
            Meaningful("MEANINGFUL-EYES", "眼睛也休息一下。",
                PetPersonaContext.SmallTalk)
        };

        internal static readonly PetPersonaEntry[] DaypartMeaningful =
        {
            Meaningful("MEANINGFUL-MOVE", "别坐太久，起来动一动。",
                PetPersonaContext.Morning | PetPersonaContext.Noon |
                PetPersonaContext.Afternoon | PetPersonaContext.Evening),
            Meaningful("MEANINGFUL-WATER", "水还是要喝的。",
                PetPersonaContext.Morning | PetPersonaContext.Noon |
                PetPersonaContext.Afternoon),
            Meaningful("MEANINGFUL-MEAL", "忙归忙，饭还是要吃。",
                PetPersonaContext.Noon),
            Meaningful("MEANINGFUL-SLOW", "不急，慢慢来。",
                PetPersonaContext.Afternoon | PetPersonaContext.Evening),
            Meaningful("MEANINGFUL-ENOUGH", "今天也不用什么都做完。",
                PetPersonaContext.Evening),
            Meaningful("MEANINGFUL-EYES", "眼睛也休息一下。",
                PetPersonaContext.Afternoon | PetPersonaContext.Evening)
        };

        internal static PetPersonaEntry SelectLoopable(Random random)
        {
            if (SmallTalkLoopable.Length == 0) return null;
            Random source = random ?? new Random();
            return SmallTalkLoopable[source.Next(SmallTalkLoopable.Length)];
        }

        internal static PetPersonaEntry SelectUnusedMeaningful(
            PetPersonaContext context, Func<string, bool> wasUsed)
        {
            if (wasUsed == null) return null;
            foreach (PetPersonaEntry entry in DaypartMeaningful)
                if ((entry.EligibleContexts & context) != 0 &&
                    !wasUsed(entry.StableContentId)) return entry;
            foreach (PetPersonaEntry entry in SmallTalkMeaningful)
                if ((entry.EligibleContexts & context) != 0 &&
                    !wasUsed(entry.StableContentId)) return entry;
            return null;
        }

        private static PetPersonaEntry Loopable(string id, string body)
        {
            return PetPersonaEntry.CreateApproved(id, body,
                PetPersonaCategory.Playful, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Loopable,
                PetPersonaContextClass.ContextFree, true);
        }

        private static PetPersonaEntry Meaningful(string id, string body,
            PetPersonaContext contexts)
        {
            return PetPersonaEntry.CreateApproved(id, body, PetPersonaCategory.Care,
                PetSentenceIntent.Gentle, contexts | PetPersonaContext.SmallTalk,
                PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true);
        }
    }
}
