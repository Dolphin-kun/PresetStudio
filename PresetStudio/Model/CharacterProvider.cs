using System.Collections;
using System.Reflection;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace PresetStudio.Model
{
    internal static class CharacterProvider
    {
        public static IReadOnlyList<Character> GetCharacters(Timeline? timeline)
        {
            var characters = GetRegisteredCharacters();
            if (characters.Count > 0)
                return characters;

            return timeline is null
                ? []
                : [.. timeline.Items
                    .Select(item => item switch
                    {
                        TachieItem tachie => tachie.Character,
                        TachieFaceItem face => face.Character,
                        VoiceItem voice => voice.Character,
                        _ => null,
                    })
                    .OfType<Character>()
                    .Distinct()];
        }

        public static Character? Find(string name, Timeline? timeline) => GetCharacters(timeline).FirstOrDefault(character => character.Name == name);

        public static bool HasTachie(Character character) => character.TachieDefaultFaceParameter is not null;

        private static List<Character> GetRegisteredCharacters()
        {
            try
            {
                var settingsType = typeof(Character).Assembly.GetType("YukkuriMovieMaker.Settings.CharacterSettings");
                if (settingsType is null)
                    return [];

                var settings = typeof(SettingsBase<>).MakeGenericType(settingsType)
                    .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null);
                return settings?.GetType().GetProperty("Characters")?.GetValue(settings) is IEnumerable characters
                    ? [.. characters.OfType<Character>()]
                    : [];
            }
            catch
            {
                return [];
            }
        }
    }
}
