namespace PennyPet
{
    internal sealed class AlmanacDailySelection
    {
        internal AlmanacDailySelection(AlmanacTopic topic,
            string sourceTerm, bool isYi, string variantId,
            string framingId, string wordingId, string endingId,
            string text)
        {
            Topic = topic;
            SourceTerm = sourceTerm;
            IsYi = isYi;
            VariantId = variantId;
            FramingId = framingId;
            WordingId = wordingId;
            EndingId = endingId;
            Text = text;
        }

        internal AlmanacTopic Topic { get; private set; }
        internal string SourceTerm { get; private set; }
        internal bool IsYi { get; private set; }
        internal string VariantId { get; private set; }
        internal string FramingId { get; private set; }
        internal string WordingId { get; private set; }
        internal string EndingId { get; private set; }
        internal string Text { get; private set; }
    }
}
