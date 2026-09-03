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
        Persona,
        Inspiration
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

    // Approved Persona corpus consumed by the SmallTalk and Daypart selectors.
    // Content is provided by the data pipeline and must not be edited here.
    internal static class PetPersonaRuntimeCatalog
    {
        internal static readonly PetPersonaEntry[] SmallTalkLoopable =
        {
            Approved("PENNY-000001", "我在", PetPersonaCategory.General,
                PetSentenceIntent.Gentle, PetPersonaContext.SmallTalk,
                PetPersonaRepeatClass.Loopable, PetPersonaContextClass.ContextFree,
                false),
            Approved("PENNY-000002", "嗯", PetPersonaCategory.General,
                PetSentenceIntent.Question, PetPersonaContext.SmallTalk,
                PetPersonaRepeatClass.Loopable, PetPersonaContextClass.ContextFree,
                true),
            Approved("PENNY-000003", "需要我帮什么忙吗", PetPersonaCategory.General,
                PetSentenceIntent.Question, PetPersonaContext.SmallTalk,
                PetPersonaRepeatClass.Loopable, PetPersonaContextClass.ContextFree,
                false)
        };

        internal static readonly PetPersonaEntry[] SmallTalkMeaningful =
        {
            Approved("PENNY-000005", "今天突然有点想听《怎样》",
                PetPersonaCategory.Music, PetSentenceIntent.Statement,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, false),
            Approved("PENNY-000006",
                "跟你一起变成更好的自己，是我们一直在一起的意义",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000007", "只管闭上眼睛往前追~",
                PetPersonaCategory.Music, PetSentenceIntent.Statement,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000008", "心慢自然安",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Statement,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000009", "一起随风流动，一起向內生长",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Statement,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000010", "心有静水就不畏任何风吹草动的日子",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000011",
                "顺着节奏向内生长\n把时间交还给生活\n把自己安放于本心",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000012", "如果你是野蔷薇，那愿我是你身后的大树",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, false),
            Approved("PENNY-000017", "答案永远在心里面",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000019",
                "爱一个人不是帮他改变，而是允许他在你的爱里面看到自己，希望你们可以在我的歌里听见自己",
                PetPersonaCategory.Inspiration, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000020", "没有想法也是一种想法",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Statement,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000021", "无论在哪里都要继续照顾好自己",
                PetPersonaCategory.Care, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true),
            Approved("PENNY-000022", "允许外面依旧喧闹，允许自己喜欢独处",
                PetPersonaCategory.Catchphrase, PetSentenceIntent.Gentle,
                PetPersonaContext.SmallTalk, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.ContextFree, true)
        };

        internal static readonly PetPersonaEntry[] DaypartMeaningful =
        {
            Meaningful("MEANINGFUL-MOVE", "别坐太久，起来动一动。",
                PetPersonaContext.Morning | PetPersonaContext.Noon |
                PetPersonaContext.Afternoon | PetPersonaContext.Evening),
            Meaningful("MEANINGFUL-WATER", "水还是要喝的。",
                PetPersonaContext.Morning | PetPersonaContext.Noon |
                PetPersonaContext.Afternoon),
            Approved("PENNY-000004", "中午好，忙归忙，饭还是要吃。",
                PetPersonaCategory.Care, PetSentenceIntent.Gentle,
                PetPersonaContext.Noon, PetPersonaRepeatClass.Meaningful,
                PetPersonaContextClass.Contextual, false),
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

        private static PetPersonaEntry Approved(string id, string body,
            PetPersonaCategory category, PetSentenceIntent intent,
            PetPersonaContext contexts, PetPersonaRepeatClass repeatClass,
            PetPersonaContextClass contextClass, bool preserveEnding)
        {
            return PetPersonaEntry.CreateApproved(id, body, category, intent,
                contexts, repeatClass, contextClass, preserveEnding);
        }
    }
}
