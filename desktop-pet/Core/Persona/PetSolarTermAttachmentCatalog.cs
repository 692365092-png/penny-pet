using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Approved messages appended after the existing solar-term sentence. The
    // existing "today is <term>" rule stays unchanged; this catalog only adds
    // the follow-up line with its StableContentId.
    internal sealed class PetSolarTermAttachment
    {
        internal PetSolarTermAttachment(string stableContentId, string text)
        {
            StableContentId = stableContentId ?? String.Empty;
            Text = text ?? String.Empty;
        }

        internal string StableContentId { get; private set; }
        internal string Text { get; private set; }
    }

    internal static class PetSolarTermAttachmentCatalog
    {
        private static readonly Dictionary<string, PetSolarTermAttachment>
            Attachments = new Dictionary<string, PetSolarTermAttachment>(
                StringComparer.Ordinal)
        {
            { "春分", new PetSolarTermAttachment("PENNY-000013",
                "春天最适合热聊，祝大家都有一个被爱包围的春分") },
            { "大寒", new PetSolarTermAttachment("PENNY-000014",
                "大寒节气记得多保暖") },
            { "小寒", new PetSolarTermAttachment("PENNY-000015",
                "祝大家小寒安康喜乐") },
            { "冬至", new PetSolarTermAttachment("PENNY-000016",
                "冬至平安喜乐") },
            { "大雪", new PetSolarTermAttachment("PENNY-000018",
                "大雪，沉淀成成果的时刻，身心都要继续保暖") }
        };

        internal static bool TryGet(string chineseName,
            out PetSolarTermAttachment value)
        {
            return Attachments.TryGetValue(chineseName ?? String.Empty,
                out value);
        }

        internal static int Count
        {
            get { return Attachments.Count; }
        }

        internal static IEnumerable<KeyValuePair<string,
            PetSolarTermAttachment>> GetAll()
        {
            return Attachments;
        }
    }
}
