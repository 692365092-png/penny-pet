namespace PennyPet
{
    internal sealed class WeatherDailySelection
    {
        internal WeatherDailySelection(WeatherMeaning meaning,
            string variantId, string text)
        {
            Meaning = meaning;
            VariantId = variantId;
            Text = text;
        }

        internal WeatherMeaning Meaning { get; private set; }
        internal string VariantId { get; private set; }
        internal string Text { get; private set; }
    }
}
