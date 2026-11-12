namespace ColorMatch.Domain.Rules;

/// <summary>
/// Curated list of everyday, findable colours (real objects exist in these shades).
/// The colour of a day is a pure function of the date, so every server instance agrees.
/// </summary>
public static class DailyColorPalette
{
    public sealed record Entry(string Name, string Hex);

    public static readonly IReadOnlyList<Entry> Colors =
    [
        new("Mercan", "#E86C52"), new("Domates Kırmızısı", "#D9392B"), new("Kiraz", "#B3202A"), new("Bordo", "#7B1E2E"),
        new("Gül Kurusu", "#C98B8B"), new("Pembe", "#F2A0B8"), new("Fuşya", "#C2397F"), new("Lavanta", "#A790D4"),
        new("Mor", "#6B3FA0"), new("Patlıcan", "#472C57"), new("Lacivert", "#1F2F5C"), new("Kobalt", "#2A56C6"),
        new("Gök Mavisi", "#6FB1E6"), new("Bebek Mavisi", "#A9D1EE"), new("Turkuaz", "#2BB5B0"), new("Petrol", "#1E6F78"),
        new("Nane Yeşili", "#9ED9B8"), new("Zümrüt", "#0F8F5F"), new("Çimen Yeşili", "#5CAD3F"), new("Zeytin", "#7C8133"),
        new("Orman Yeşili", "#22513A"), new("Fıstık Yeşili", "#B6D36A"), new("Limon Sarısı", "#F4DE4A"), new("Hardal", "#D5A021"),
        new("Altın", "#E0A82E"), new("Kayısı", "#F4A261"), new("Turuncu", "#F07F1A"), new("Kiremit", "#B5532F"),
        new("Bakır", "#B87333"), new("Çikolata", "#5A3825"), new("Kahve", "#7A4E2D"), new("Karamel", "#B9803F"),
        new("Bej", "#D8C3A5"), new("Krem", "#F1E6CF"), new("Kum", "#C9B28F"), new("Taş", "#9C9488"),
        new("Gri", "#8A8D91"), new("Antrasit", "#3B3F45"), new("Gece Siyahı", "#16181C"), new("Kırık Beyaz", "#EFEBE3"),
        new("Şeftali", "#F7B98F"), new("Somon", "#F08A7A"), new("Mürdüm", "#7D2F6B"), new("Eflatun", "#B79ADB"),
        new("Deniz Mavisi", "#2F7FB0"), new("Buz Mavisi", "#CDE8EE"), new("Adaçayı", "#8CA38A"), new("Pas", "#A5472A"),
    ];

    private const int Step = 11; // coprime with Colors.Count (48) -> visits every colour before repeating

    public static Entry ForDate(DateOnly date)
    {
        var index = (int)(((long)date.DayNumber * Step) % Colors.Count);
        return Colors[index];
    }
}
