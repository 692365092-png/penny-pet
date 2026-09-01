namespace PennyPet
{
    internal sealed class WeatherDailySelection
    {
        internal WeatherDailySelection(WeatherMeaning meaning, string text)
        {
            Meaning = meaning;
            Text = text;
        }

        internal WeatherMeaning Meaning { get; private set; }
        internal string Text { get; private set; }
    }
}
