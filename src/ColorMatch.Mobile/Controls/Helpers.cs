using System.Globalization;
using ColorMatch.ColorEngine.ColorConversion;
using CommunityToolkit.Mvvm.ComponentModel;
using ColorMatch.Mobile.Services;

namespace ColorMatch.Mobile.Controls;

public sealed class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : false;
}

public sealed class HexToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ColorHelper.FromHex(value as string);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HexToInkConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ColorHelper.InkOn(ColorHelper.FromHex(value as string));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => !string.IsNullOrWhiteSpace(value as string);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class ColorHelper
{
    public static Color FromHex(string? hex)
        => Rgb.TryParseHex(hex, out var rgb) ? Color.FromRgb(rgb.R, rgb.G, rgb.B) : Colors.Gray;

    public static Rgb ToRgb(Color c) => new((byte)Math.Round(c.Red * 255), (byte)Math.Round(c.Green * 255), (byte)Math.Round(c.Blue * 255));

    /// <summary>Readable text colour (WCAG-style relative luminance) on top of <paramref name="background"/>.</summary>
    public static Color InkOn(Color background)
    {
        static double Lin(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        var l = 0.2126 * Lin(background.Red) + 0.7152 * Lin(background.Green) + 0.0722 * Lin(background.Blue);
        return l > 0.35 ? Color.FromArgb("#1B1B1F") : Colors.White;
    }
}

public static class TimeText
{
    public static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalSeconds < 60) return "az önce";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} dk";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} sa";
        if (d.TotalDays < 7) return $"{(int)d.TotalDays} g";
        return utc.ToLocalTime().ToString("d MMM", new CultureInfo("tr-TR"));
    }
}

/// <summary>Shared view-model base: busy flag, error text and a guard that turns exceptions into messages.</summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotBusy))] private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? errorMessage;

    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private readonly Dictionary<string, string> _seen = [];

    /// <summary>
    /// True when <paramref name="data"/> differs from what this slot showed last time. Screens reload on every visit; rebuilding a list that did
    /// not change throws away and re-creates all of its views in the middle of the page transition (the main cause of stutter), so lists are
    /// only rebuilt when this says so.
    /// </summary>
    protected bool Changed(string slot, object? data)
    {
        var sig = System.Text.Json.JsonSerializer.Serialize(data);
        if (_seen.TryGetValue(slot, out var old) && old == sig) return false;
        _seen[slot] = sig;
        return true;
    }

    protected async Task<bool> GuardAsync(Func<Task> action, bool showBusy = true)
    {
        if (showBusy && IsBusy) return false;
        try
        {
            if (showBusy) IsBusy = true;
            ErrorMessage = null;
            await action();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (ApiException ex) { ErrorMessage = ex.Message; return false; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ErrorMessage = "Beklenmeyen bir hata oluştu. Lütfen tekrar dene.";
            return false;
        }
        finally { if (showBusy) IsBusy = false; }
    }
}

public sealed class HexToInkBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(ColorHelper.InkOn(ColorHelper.FromHex(value as string)));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(ColorHelper.FromHex(value as string));
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Builds an image source from either a server URL or a local file path (offline demo).</summary>
public static class ImageHelper
{
    public static ImageSource From(string pathOrUrl)
        => pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? ImageSource.FromUri(new Uri(pathOrUrl))
            : ImageSource.FromFile(pathOrUrl);
}

/// <summary>DateTime (UTC) → "3 sa", "az önce", "8 Kas".</summary>
public sealed class TimeAgoConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime d ? TimeText.Ago(d) : "";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
