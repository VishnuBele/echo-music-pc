using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EchoMusic.Core;

namespace EchoMusic.UI;

public sealed record ThemePalette(
    string Id,
    string DisplayName,
    bool IsDark,
    Color BgRoot,
    Color BgSidebar,
    Color BgSurface,
    Color BgCard,
    Color BgHover,
    Color BgPlayerBar,
    Color BorderSubtle,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color Accent,
    Color Accent2,
    Color OnAccent
);

public static class ThemeManager
{
    public static readonly IReadOnlyList<ThemePalette> Palettes = new List<ThemePalette>
    {
        // 1. Official Spotify Desktop Green (Default!)
        new("SpotifyGreen", "Spotify Green (Default)", true,
            Color.Parse("#000000"), Color.Parse("#121212"), Color.Parse("#121212"),
            Color.Parse("#181818"), Color.Parse("#282828"), Color.Parse("#000000"),
            Color.Parse("#1FFFFFFF"), Color.Parse("#FFFFFF"), Color.Parse("#C4C4C4"), Color.Parse("#8C8C8C"),
            Color.Parse("#1ED760"), Color.Parse("#10B981"), Color.Parse("#000000")),

        // 2. Echo Material Dark
        new("EchoDark", "Echo Crimson Dark", true,
            Color.Parse("#07080C"), Color.Parse("#11131A"), Color.Parse("#12151E"),
            Color.Parse("#1A1E2B"), Color.Parse("#252A3B"), Color.Parse("#090B10"),
            Color.Parse("#1FFFFFFF"), Color.Parse("#F5F7FC"), Color.Parse("#A4ABC0"), Color.Parse("#6C748C"),
            Color.Parse("#ED5564"), Color.Parse("#FA2D48"), Color.Parse("#FFFFFF")),

        // 3. Pure Black AMOLED Emerald
        new("AmoledBlack", "AMOLED Pure Black", true,
            Color.Parse("#000000"), Color.Parse("#08080A"), Color.Parse("#0A0A0D"),
            Color.Parse("#131419"), Color.Parse("#1E2028"), Color.Parse("#000000"),
            Color.Parse("#1CFFFFFF"), Color.Parse("#FFFFFF"), Color.Parse("#A9AFBF"), Color.Parse("#686E80"),
            Color.Parse("#1ED760"), Color.Parse("#06B6D4"), Color.Parse("#000000")),

        // 4. Apple Velvet Glass
        new("AppleVelvet", "Velvet Rose", true,
            Color.Parse("#09070B"), Color.Parse("#141018"), Color.Parse("#16111B"),
            Color.Parse("#211928"), Color.Parse("#2D2237"), Color.Parse("#0B080E"),
            Color.Parse("#22FFFFFF"), Color.Parse("#FAF7FC"), Color.Parse("#B8AEC2"), Color.Parse("#7B7087"),
            Color.Parse("#FA2D48"), Color.Parse("#EC4899"), Color.Parse("#FFFFFF")),

        // 5. Electric Indigo Violet
        new("NothingMono", "Cyber Violet", true,
            Color.Parse("#06060A"), Color.Parse("#101018"), Color.Parse("#12121C"),
            Color.Parse("#1A1A28"), Color.Parse("#252538"), Color.Parse("#07070C"),
            Color.Parse("#22FFFFFF"), Color.Parse("#F4F4F8"), Color.Parse("#A6A6BC"), Color.Parse("#6B6B82"),
            Color.Parse("#8B5CF6"), Color.Parse("#06B6D4"), Color.Parse("#FFFFFF")),

        // 6. Clean Frosted Light
        new("EchoLight", "Clean Daylight", false,
            Color.Parse("#EAECF2"), Color.Parse("#FFFFFF"), Color.Parse("#FFFFFF"),
            Color.Parse("#F3F5FA"), Color.Parse("#E6E9F2"), Color.Parse("#F8F9FC"),
            Color.Parse("#16000000"), Color.Parse("#12141C"), Color.Parse("#555B6E"), Color.Parse("#858CA3"),
            Color.Parse("#1DB954"), Color.Parse("#0D9488"), Color.Parse("#FFFFFF")),

        // 7. Soft pink-tinted LIGHT palette with readable dark text.
        new("PookiePink", "Rose Petal Light", false,
            Color.Parse("#F7EEF4"), Color.Parse("#FFF8FC"), Color.Parse("#FFFBFE"),
            Color.Parse("#FFFFFF"), Color.Parse("#F3E6EF"), Color.Parse("#FBF5F9"),
            Color.Parse("#1C6D4760"), Color.Parse("#281A25"), Color.Parse("#674E60"), Color.Parse("#8B7485"),
            Color.Parse("#D83682"), Color.Parse("#A435A7"), Color.Parse("#FFFFFF")),

        // A gently rosier sibling to Rose Petal Light, still soft with high-contrast text.
        new("RosePetalBloom", "Rose Petal Bloom", false,
            Color.Parse("#F5E1ED"), Color.Parse("#FFF0F7"), Color.Parse("#FFF6FB"),
            Color.Parse("#FFF3F9"), Color.Parse("#F2DCEB"), Color.Parse("#FAEAF3"),
            Color.Parse("#286D4760"), Color.Parse("#281A25"), Color.Parse("#69475C"), Color.Parse("#8A6D80"),
            Color.Parse("#E64F98"), Color.Parse("#B94BB5"), Color.Parse("#FFFFFF")),

        // 8. Deep ink blue with cool moonlit accents.
        new("LateNight", "Late Night", true,
            Color.Parse("#070B16"), Color.Parse("#0D1322"), Color.Parse("#10182A"),
            Color.Parse("#172238"), Color.Parse("#23314A"), Color.Parse("#060A13"),
            Color.Parse("#27B6C8FF"), Color.Parse("#F4F7FF"), Color.Parse("#B8C6E0"), Color.Parse("#7E91B4"),
            Color.Parse("#7DA8FF"), Color.Parse("#54D6D0"), Color.Parse("#07101D")),

    };

    public static ThemePalette GetPalette(string id)
    {
        if (string.Equals(id, "EchoCoral", StringComparison.OrdinalIgnoreCase)) id = "EchoDark";
        if (string.Equals(id, "CleanLight", StringComparison.OrdinalIgnoreCase)) id = "EchoLight";
        return Palettes.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];
    }

    public static void ApplyToResources(IResourceDictionary res, ThemePalette p)
    {
        // Keep the primary UI brand accent consistent with the selected theme.
        var accent = p.Accent;
        res["BgRootBrush"] = new SolidColorBrush(p.BgRoot);
        res["BgSidebarBrush"] = new SolidColorBrush(p.BgSidebar);
        res["BgSurfaceBrush"] = new SolidColorBrush(p.BgSurface);
        res["BgCardBrush"] = new SolidColorBrush(p.BgCard);
        res["BgHoverBrush"] = new SolidColorBrush(p.BgHover);
        res["BgPlayerBarBrush"] = new SolidColorBrush(p.BgPlayerBar);
        res["BorderSubtleBrush"] = new SolidColorBrush(p.BorderSubtle);
        res["TextPrimaryBrush"] = new SolidColorBrush(p.TextPrimary);
        res["TextSecondaryBrush"] = new SolidColorBrush(p.TextSecondary);
        res["TextMutedBrush"] = new SolidColorBrush(p.TextMuted);

        var accentBrush = new SolidColorBrush(accent);
        res["AccentBrush"] = accentBrush;
        res["Accent2Color"] = p.Accent2;
        res["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
        res["AccentGlowBrush"] = new SolidColorBrush(Color.FromArgb(64, accent.R, accent.G, accent.B));
        res["OnAccentBrush"] = new SolidColorBrush(p.OnAccent);

        res["SystemAccentColor"] = accent;
        res["SliderThumbBackground"] = Brushes.White;
        res["SliderThumbBackgroundPointerOver"] = accentBrush;
        res["SliderThumbBackgroundPressed"] = accentBrush;
        res["SliderTrackValueFill"] = accentBrush;
        res["SliderTrackValueFillPointerOver"] = accentBrush;
        res["SliderTrackValueFillPressed"] = accentBrush;
        res["ToggleSwitchFillOn"] = accentBrush;
        res["ToggleSwitchFillOnPointerOver"] = accentBrush;
        res["ProgressBarIndicatorForeground"] = accentBrush;
    }

    public static FontFamily ResolveFont(string choice) => choice switch
    {
        "PlusJakarta" => new FontFamily("avares://EchoMusic/Assets/Fonts#Plus Jakarta Sans, Segoe UI, Nirmala UI, Leelawadee UI, Malgun Gothic, Yu Gothic UI, avares://EchoMusic/Assets/Fonts/Indic#Noto Sans Devanagari, sans-serif"),
        "Outfit" => new FontFamily("avares://EchoMusic/Assets/Fonts#Outfit, Segoe UI, Nirmala UI, Leelawadee UI, Malgun Gothic, Yu Gothic UI, avares://EchoMusic/Assets/Fonts/Indic#Noto Sans Devanagari, sans-serif"),
        "Inter" => new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter, Segoe UI, Nirmala UI, Leelawadee UI, Malgun Gothic, Yu Gothic UI, avares://EchoMusic/Assets/Fonts/Indic#Noto Sans Devanagari, sans-serif"),
        "System" => new FontFamily("Segoe UI, Nirmala UI, Leelawadee UI, Malgun Gothic, Yu Gothic UI, avares://EchoMusic/Assets/Fonts/Indic#Noto Sans Devanagari, sans-serif"),
        // Default: Static 5-weight CircularGeo (440/510/600/690/780 optical weights) + full Windows Indic/Asian script fallbacks!
        _ => new FontFamily("avares://EchoMusic/Assets/Fonts/CircularGeo#CircularGeo, Segoe UI, Nirmala UI, Leelawadee UI, Malgun Gothic, Yu Gothic UI, Microsoft YaHei UI, avares://EchoMusic/Assets/Fonts/Indic#Noto Sans Devanagari, sans-serif"),
    };
}
