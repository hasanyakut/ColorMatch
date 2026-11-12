using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Rules;

/// <summary>One colour of the Color Atlas. <see cref="Difficulty"/> decides which daily hunt can ask for it.</summary>
public sealed record AtlasColor(string Key, string Name, string Hex, HuntDifficulty Difficulty, string CollectionKey);

public sealed record AtlasCollection(string Key, string Name, string Description, IReadOnlyList<AtlasColor> Colors);

/// <summary>
/// The Color Atlas: five themed collections of twenty colours, defined here (never by the client).
/// The same 100 colours are the pool of the daily hunts (see <see cref="DailyHuntPalette"/>): a colour is unlocked by a valid hunt
/// result (score ≥ the hunt's minimum) on a day when that colour is a hunt target. Easy colours are vivid, medium ones are named shades,
/// hard ones are muted tones that are easy to confuse. Hex values are unique across the catalogue so a hunt target maps to exactly one colour.
/// </summary>
public static class AtlasCatalog
{
    private const char E = 'E', M = 'M', H = 'H';

    private static AtlasCollection Build(string key, string name, string description, params (string Name, string Hex, char D)[] colors)
    {
        var list = colors.Select((c, i) => new AtlasColor($"{key}-{i + 1:00}", c.Name, c.Hex.ToUpperInvariant(),
            c.D switch { 'E' => HuntDifficulty.Easy, 'M' => HuntDifficulty.Medium, _ => HuntDifficulty.Hard }, key)).ToList();
        return new AtlasCollection(key, name, description, list);
    }

    public static readonly IReadOnlyList<AtlasCollection> Collections =
    [
        Build("forest", "Orman", "Yaprak, kabuk, yosun ve gölge: ormanın renkleri.",
            ("Yaprak Yeşili", "#3FA34D", E), ("Çam Yeşili", "#1F6B3B", E), ("Çimen", "#6CC24A", E), ("Mantar Kırmızısı", "#D63B2F", E),
            ("Orman Sarısı", "#E6C229", E), ("Kozalak Kahvesi", "#7A4B2A", E), ("Gökyüzü Mavisi", "#5DB7E8", E),
            ("Eğrelti", "#4E9A5F", M), ("Zeytin Dalı", "#7C8B3A", M), ("Kavak Yeşili", "#A6C95B", M), ("Kütük", "#5B3A24", M),
            ("Yosun Yeşili", "#6F8F3F", M), ("Sabah Sisi", "#C9D8B6", M), ("Meşe Palamudu", "#A3722F", M),
            ("Ardıç", "#3F5B4B", H), ("Likenli Taş", "#8E9B84", H), ("Çürük Yaprak", "#8B7B3E", H), ("Gölge Yeşili", "#2D4A3A", H),
            ("Kuru Eğrelti", "#BFC49A", H), ("Ağaç Kabuğu Grisi", "#7A736A", H)),

        Build("ocean", "Okyanus", "Derinlik, köpük, mercan ve fırtına: denizin renkleri.",
            ("Okyanus Mavisi", "#1A73D1", E), ("Turkuaz Su", "#1FB5C9", E), ("Mercan Pembesi", "#F0647A", E), ("Deniz Yıldızı", "#F28B38", E),
            ("Yosun", "#19A974", E), ("Dalga Köpüğü", "#E8F4F8", E), ("Derin Lacivert", "#14315E", E),
            ("Lagün", "#38B6B0", M), ("Somon Balığı", "#F08A7A", M), ("Deniz Mavisi", "#2F7FB0", M), ("Kıyı Kumu", "#D8C7A0", M),
            ("Midye Grisi", "#7F8C96", M), ("Dalış Mavisi", "#0E4D7A", M),
            ("Sis Mavisi", "#9FB6C3", H), ("Fırtına Grisi", "#5C6F7C", H), ("Gölgeli Körfez", "#3B6A78", H), ("Deniz Camı", "#A8CFC0", H),
            ("Dalga Kumu", "#B9C5CF", H), ("Eskimiş Halat", "#8F7B5C", H), ("Gece Denizi", "#243B55", H)),

        Build("sunset", "Gün Batımı", "Ufuk, bulut ve alacakaranlık: akşamın renkleri.",
            ("Gün Batımı Turuncusu", "#F57C00", E), ("Alev Kırmızısı", "#E8381E", E), ("Pembe Bulut", "#F26BA3", E), ("Altın Saat", "#FFC21A", E),
            ("Mor Ufuk", "#7B3FB5", E), ("Şafak Sarısı", "#FFE066", E), ("Akşam Mavisi", "#2B4FA3", E),
            ("Kayısı Işığı", "#F4A261", M), ("Gül Kurusu Bulut", "#C98B8B", M), ("Kızıl Ufuk", "#C9472B", M), ("Lavanta Gökyüzü", "#A790D4", M),
            ("Erik", "#6B2D5C", M), ("Şeftali Alacası", "#F7B98F", M), ("Bakır Işıltı", "#B87333", M),
            ("Pus Pembesi", "#D8A7A0", H), ("Soluk Altın", "#D9C27A", H), ("Duman Moru", "#8E7A99", H), ("Toz Turuncu", "#C98F62", H),
            ("Alacakaranlık", "#4A4466", H), ("Küllü Gül", "#B38B8B", H)),

        Build("desert", "Çöl", "Kum, kaya, kaktüs ve gece: çölün renkleri.",
            ("Çöl Sarısı", "#E8B84A", E), ("Kızıl Kaya", "#C4552D", E), ("Kaktüs Yeşili", "#3E8E41", E), ("Çöl Göğü", "#2E9BD6", E),
            ("Dolunay", "#F4EFE2", E), ("Akrep Siyahı", "#1C1A1A", E), ("Kum Turuncusu", "#DD9A55", E),
            ("Kum Tepesi", "#D9B382", M), ("Kil Kahvesi", "#A0623B", M), ("Vaha Yeşili", "#2F8F83", M), ("Bakır Kanyon", "#B4652E", M),
            ("Deve Tüyü", "#C19A6B", M), ("Güneş Çatlağı", "#D4A03A", M),
            ("Toz Bej", "#CDBFA6", H), ("Kuru Ot", "#A59B6A", H), ("Taş Grisi", "#9A948A", H), ("Gölgeli Kanyon", "#7A4E3A", H),
            ("Çatlak Toprak", "#B09A82", H), ("Soluk Kaktüs", "#6F9E8E", H), ("Uçuk Mor Kaya", "#9C8A98", H)),

        Build("neon", "Neon", "Gece şehri, tabela ve ışık: neonun renkleri.",
            ("Neon Pembe", "#FF2E93", E), ("Elektrik Mavisi", "#1F6BFF", E), ("Asit Yeşili", "#3DFF3D", E), ("Lazer Sarısı", "#F2FF00", E),
            ("Ultra Mor", "#8F2BFF", E), ("Neon Turuncu", "#FF6A00", E),
            ("Siber Turkuaz", "#00D8D0", M), ("Magenta Işık", "#D61FBF", M), ("Lime Parlaması", "#A3F23A", M), ("Buz Neonu", "#6FD3FF", M),
            ("Plazma Pembesi", "#FF6FB5", M), ("Gece Kulübü Moru", "#5A2BC9", M), ("Flüoresan Mercan", "#FF7A6B", M),
            ("Soluk Neon Yeşil", "#9FD8A4", H), ("Pastel Fuşya", "#D58CC4", H), ("Mat Elektrik", "#5F7FD8", H), ("Toz Neon Sarısı", "#D8D78A", H),
            ("Karartılmış Cyan", "#3F9AA3", H), ("Dumanlı Mor", "#7B63A8", H), ("Kül Neon Pembesi", "#C98AA8", H)),
    ];

    public static IReadOnlyList<AtlasColor> All { get; } = Collections.SelectMany(c => c.Colors).ToList();

    private static readonly Dictionary<string, AtlasColor> ByHex = All.ToDictionary(c => c.Hex, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, AtlasColor> ByKey = All.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

    public static AtlasColor? FindByHex(string? hex) => hex is not null && ByHex.TryGetValue(hex, out var c) ? c : null;
    public static AtlasColor? FindByKey(string? key) => key is not null && ByKey.TryGetValue(key, out var c) ? c : null;
    public static AtlasCollection? Collection(string key) => Collections.FirstOrDefault(c => c.Key == key);

    public static IReadOnlyList<AtlasColor> For(HuntDifficulty d) => All.Where(c => c.Difficulty == d).ToList();
}
