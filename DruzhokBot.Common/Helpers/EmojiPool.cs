namespace DruzhokBot.Common.Helpers;

public static class EmojiPool
{
    private static readonly Dictionary<string, string> UkrainianNames = new()
    {
        ["🐶"] = "песик",
        ["🐈"] = "котик",
        ["🐸"] = "жабка",
        ["🦊"] = "лисичка",
        ["🐼"] = "панда",
        ["🐷"] = "свинка",
        ["🐵"] = "мавпочка",
        ["🍎"] = "яблуко",
        ["🍌"] = "банан",
        ["🍕"] = "піца",
        ["🍔"] = "бургер",
        ["🌮"] = "тако",
        ["🍉"] = "кавун",
        ["🚗"] = "машина",
        ["🚀"] = "ракета",
        ["✈️"] = "літак",
        ["⚽"] = "мʼяч",
        ["🎸"] = "гітара",
        ["🎁"] = "подарунок",
        ["🌵"] = "кактус",
        ["⭐"] = "зірка",
        ["☀️"] = "сонечко",
    };

    public static IReadOnlyList<string> All { get; } = UkrainianNames.Keys.ToList();

    public static string GetUkrainianName(string emoji)
    {
        if (!UkrainianNames.TryGetValue(emoji, out var name))
            throw new KeyNotFoundException($"No Ukrainian name for emoji: {emoji}");
        return name;
    }
}
