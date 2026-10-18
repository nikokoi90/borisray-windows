using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BorisRay.Core;

public sealed record Node(string Name, string Country, string Channel, JsonObject Outbound)
{
    public string Tag => Outbound["tag"]!.GetValue<string>();
}

public sealed record AppRule(string Path, bool Vpn, bool IncludeFolder = false);
public sealed class RoutingSettings
{
    public string Country { get; set; } = "auto";
    public string Channel { get; set; } = "all";
    public string Server { get; set; } = "auto";
    public bool DefaultVpn { get; set; } = true;
    public Dictionary<string, bool> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AppRule> Apps { get; set; } = [];
}

public static class Profiles
{
    public const int MaximumBytes = 1_048_576;
    public static Uri SubscriptionUri(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Нужна HTTPS-ссылка подписки без логина и фрагмента в адресе.");
        return uri;
    }

    private static readonly Dictionary<string, (string Name, string Pattern)> Countries = new()
    {
        ["pl"] = ("Польша", "poland|warsaw|польш|варшав"), ["fi"] = ("Финляндия", "finland|helsinki|финлянд|хельсинки"),
        ["de"] = ("Германия", "germany|frankfurt|герман|франкфурт"), ["nl"] = ("Нидерланды", "netherlands|amsterdam|нидерланд|амстердам"),
        ["us"] = ("США", "united states|usa|сша|new york"), ["gb"] = ("Великобритания", @"united kingdom|london|великобрит|лондон|\buk\b"),
        ["fr"] = ("Франция", "france|paris|франц|париж"), ["se"] = ("Швеция", "sweden|stockholm|швец|стокгольм"),
        ["ch"] = ("Швейцария", "switzerland|zurich|швейцар|цюрих"), ["ee"] = ("Эстония", "estonia|tallinn|эстон|таллин"),
        ["lv"] = ("Латвия", "latvia|riga|латв|рига"), ["lt"] = ("Литва", "lithuania|vilnius|литва|вильнюс"),
        ["tr"] = ("Турция", "turkey|türkiye|istanbul|турци|стамбул"), ["ru"] = ("Россия", "russia|moscow|росси|москв"),
        ["kz"] = ("Казахстан", "kazakhstan|казахстан"), ["jp"] = ("Япония", "japan|tokyo|япони|токио"),
        ["sg"] = ("Сингапур", "singapore|сингапур"), ["hk"] = ("Гонконг", "hong kong|гонконг"),
        ["ca"] = ("Канада", "canada|toronto|канад|торонто"), ["au"] = ("Австралия", "australia|sydney|австрали|сидней"),
        ["es"] = ("Испания", "spain|madrid|испани|мадрид"), ["it"] = ("Италия", "italy|milan|итали|милан"),
        ["at"] = ("Австрия", "austria|vienna|австрия|вена"), ["cz"] = ("Чехия", "czech|prague|чехи|прага")
    };
    public static string CountryName(string code) => Countries.TryGetValue(code, out var value) ? value.Name : code.ToUpperInvariant();
    private static string CountryFromName(string name)
    {
        foreach (var (code, value) in Countries)
        {
            var flag = string.Concat(code.ToUpperInvariant().Select(c => char.ConvertFromUtf32(0x1f1e6 + c - 'A')));
            if (name.Contains(flag, StringComparison.Ordinal) || Regex.IsMatch(name, @"\b" + code + @"\b|" + value.Pattern, RegexOptions.IgnoreCase)) return code;
        }
        return "auto";
    }
    public static string Domain(string input)
    {
        var text = input.Trim().TrimEnd('.').ToLowerInvariant();
        if (text.Contains("://", StringComparison.Ordinal) || text.IndexOfAny(['/', '*', ':', ' ', '@']) >= 0)
            throw new ArgumentException("Укажи домен без https://, пути и порта.");
        var result = new IdnMapping().GetAscii(text);
        if (result.Length > 253 || !result.Contains('.') || IPAddress.TryParse(result, out _) ||
            result.Split('.').Any(p => p.Length is < 1 or > 63 || !Regex.IsMatch(p, "^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")))
            throw new ArgumentException("Проверь домен сайта.");
        return result;
    }

    public static List<Node> Parse(string input) => SubscriptionParser.Parse(input).Nodes;

    internal static Node Named(JsonObject outbound, string label, int index)
    {
        var name = new string(label.Where(c => !char.IsControl(c)).Take(160).ToArray()).Trim();
        if (name.Length == 0) name = $"Сервер {index + 1}";
        outbound["tag"] = $"{name} · {index + 1}";
        var channel = outbound["type"]!.GetValue<string>() is "hysteria2" or "tuic" ? "udp" : "tcp";
        return new Node(name, CountryFromName(name), channel, outbound);
    }
}
