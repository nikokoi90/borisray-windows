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

    private static string Decode(string s) => Uri.UnescapeDataString(s);

    public static List<Node> Parse(string input)
    {
        if (Encoding.UTF8.GetByteCount(input) > MaximumBytes) throw new ArgumentException("Подписка слишком большая.");
        var raw = input.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(raw)); }
            catch (FormatException) { throw new ArgumentException("Нужен список VLESS Reality / Hysteria2 или HTTPS-подписка."); }
        }
        var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is < 1 or > 256) throw new ArgumentException("В подписке должно быть от 1 до 256 профилей.");
        var result = new List<Node>();
        for (var i = 0; i < lines.Length; i++)
        {
            try { result.Add(ParseNode(lines[i], i)); }
            catch (Exception e) when (e is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
            { throw new ArgumentException($"Не удалось разобрать профиль {i + 1}. Поддерживаются TCP VLESS Reality и UDP Hysteria2."); }
        }
        return result;
    }

    private static Node ParseNode(string line, int index)
    {
        var uri = new Uri(line, UriKind.Absolute);
        if (uri.Scheme is not ("vless" or "hysteria2" or "hy2") || string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535 || uri.UserInfo.Length == 0)
            throw new ArgumentException("Unsupported profile");
        var q = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).ToDictionary(p => Decode(p[0]), p => p.Length == 2 ? Decode(p[1]) : "", StringComparer.Ordinal);
        string Get(string key, string fallback = "") => q.GetValueOrDefault(key, fallback);
        if (Get("insecure") == "1" || Get("allowInsecure") == "1") throw new ArgumentException("Insecure TLS");
        var name = uri.Fragment.Length > 1 ? Decode(uri.Fragment[1..]) : $"Сервер {index + 1}";
        name = new string(name.Where(c => !char.IsControl(c)).Take(160).ToArray());
        var country = CountryFromName(name);
        var tls = new JsonObject { ["enabled"] = true, ["server_name"] = Get("sni", Get("peer", uri.Host)) };
        var node = new JsonObject { ["tag"] = $"{name} · {index + 1}", ["server"] = uri.Host.Trim('[', ']'), ["server_port"] = uri.Port, ["tls"] = tls };
        string channel;
        if (uri.Scheme == "vless")
        {
            if (Get("type", "tcp") != "tcp" || Get("security") != "reality" || string.IsNullOrWhiteSpace(Get("pbk")) || !Guid.TryParseExact(Decode(uri.UserInfo), "D", out _)) throw new ArgumentException("Unsupported VLESS");
            tls["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = Get("fp", "chrome") };
            tls["reality"] = new JsonObject { ["enabled"] = true, ["public_key"] = Get("pbk"), ["short_id"] = Get("sid") };
            node["type"] = "vless"; node["uuid"] = Decode(uri.UserInfo);
            if (Get("flow").Length > 0) node["flow"] = Get("flow");
            channel = "tcp";
        }
        else
        {
            node["type"] = "hysteria2"; node["password"] = Decode(uri.UserInfo); channel = "udp";
            if (Get("obfs").Length > 0)
            {
                if (Get("obfs") != "salamander" || Get("obfs-password").Length == 0) throw new ArgumentException("Unsupported obfuscation");
                node["obfs"] = new JsonObject { ["type"] = "salamander", ["password"] = Get("obfs-password") };
            }
            if (Get("mport").Length > 0)
            {
                if (!Regex.IsMatch(Get("mport"), "^[0-9,:-]+$")) throw new ArgumentException("Invalid ports");
                node["server_ports"] = new JsonArray(Get("mport").Split(',').Select(p => (JsonNode?)JsonValue.Create(p.Replace('-', ':'))).ToArray());
            }
        }
        return new Node(name, country, channel, node);
    }
}
