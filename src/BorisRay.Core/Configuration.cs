using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BorisRay.Core;

public static class Configuration
{
    private static JsonArray Array(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
    public static string AppPattern(AppRule app)
    {
        if (!Regex.IsMatch(app.Path, @"^[A-Za-z]:\\.+\.exe$", RegexOptions.IgnoreCase) || app.Path.Any(char.IsControl))
            throw new ArgumentException("Нужен полный путь настоящего .exe, а не ярлык.");
        static string GoEscape(string text) => string.Concat(text.Select(c => "\\.+*?()|[]{}^$".Contains(c) ? "\\" + c : c.ToString()));
        var folder = app.Path[..app.Path.LastIndexOf('\\')];
        if (app.IncludeFolder && folder.Length <= 2) throw new ArgumentException("Не выбирай весь системный диск. Добавь конкретный .exe.");
        var pattern = app.IncludeFolder ? GoEscape(folder) + @"\\.+\.exe" : GoEscape(app.Path);
        return "(?i)^" + pattern + "$";
    }
    public static bool GlobalIPv6(IPAddress address) => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && (address.GetAddressBytes()[0] & 0xe0) == 0x20;
    public static List<Node> Selected(IReadOnlyList<Node> nodes, RoutingSettings settings) => nodes.Where(n => (settings.Country == "auto" || n.Country == settings.Country) && (settings.Channel == "all" || n.Channel == settings.Channel)).ToList();

    public static JsonObject Build(IReadOnlyList<Node> nodes, RoutingSettings settings, bool directIPv6, int apiPort, string apiSecret,
        int? proxyPort = null, string? proxyPassword = null, bool measurement = false)
    {
        var chosen = Selected(nodes, settings);
        if (chosen.Count == 0) throw new ArgumentException("Для выбранной страны и канала нет профилей.");
        var tags = chosen.Select(n => n.Tag).ToArray();
        var selected = tags.Contains(settings.Server, StringComparer.Ordinal) ? settings.Server : "auto";
        var outbounds = new JsonArray
        {
            new JsonObject { ["type"] = "selector", ["tag"] = "vpn", ["outbounds"] = Array(["auto", ..tags]), ["default"] = selected },
            new JsonObject { ["type"] = "urltest", ["tag"] = "auto", ["outbounds"] = Array(tags), ["url"] = "https://cp.cloudflare.com/generate_204", ["interval"] = "3m", ["tolerance"] = 80 },
            new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
        };
        foreach (var node in chosen) outbounds.Add(node.Outbound.DeepClone());
        var routes = new JsonArray { new JsonObject { ["action"] = "sniff", ["timeout"] = "300ms" },
            new JsonObject { ["ip_cidr"] = Array(["172.19.48.2/32"]), ["port"] = 53, ["action"] = "hijack-dns" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
            new JsonObject { ["ip_is_private"] = true, ["action"] = "route", ["outbound"] = "direct" } };
        var dnsRules = new JsonArray();
        if (!measurement)
        {
            foreach (var site in settings.Sites.OrderByDescending(x => x.Key.Length))
            {
                var domain = Profiles.Domain(site.Key); var target = site.Value ? "vpn" : "direct";
                routes.Add(new JsonObject { ["domain_suffix"] = Array([domain]), ["action"] = "route", ["outbound"] = target });
                dnsRules.Add(new JsonObject { ["domain_suffix"] = Array([domain]), ["server"] = site.Value ? "private-dns" : "bootstrap" });
            }
            foreach (var app in settings.Apps)
            {
                var pattern = AppPattern(app);
                routes.Add(new JsonObject { ["process_path_regex"] = Array([pattern]), ["action"] = "route", ["outbound"] = app.Vpn ? "vpn" : "direct" });
                dnsRules.Add(new JsonObject { ["process_path_regex"] = Array([pattern]), ["server"] = app.Vpn ? "private-dns" : "bootstrap" });
            }
        }
        var inbound = proxyPort is int port ? new JsonObject { ["type"] = "mixed", ["tag"] = "test-proxy", ["listen"] = "127.0.0.1", ["listen_port"] = port,
            ["users"] = new JsonArray(new JsonObject { ["username"] = "boris", ["password"] = proxyPassword ?? throw new ArgumentNullException(nameof(proxyPassword)) }) } :
            new JsonObject { ["type"] = "tun", ["tag"] = "tun-in", ["interface_name"] = "BorisRay", ["address"] = Array(["172.19.48.1/30", "fdfe:dcba:9876::1/126"]),
                ["dns_address"] = Array(["172.19.48.2"]), ["auto_route"] = true, ["strict_route"] = true, ["stack"] = "mixed", ["mtu"] = 1400 };
        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn", ["timestamp"] = true },
            ["dns"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["type"] = "local", ["tag"] = "bootstrap" },
                new JsonObject { ["type"] = "https", ["tag"] = "private-dns", ["server"] = "1.1.1.1", ["server_port"] = 443, ["path"] = "/dns-query", ["tls"] = new JsonObject { ["server_name"] = "cloudflare-dns.com" }, ["detour"] = "vpn" }),
                ["rules"] = dnsRules, ["final"] = measurement || settings.DefaultVpn ? "private-dns" : "bootstrap", ["strategy"] = directIPv6 ? "prefer_ipv4" : "ipv4_only", ["reverse_mapping"] = true },
            ["inbounds"] = new JsonArray(inbound), ["outbounds"] = outbounds,
            ["route"] = new JsonObject { ["rules"] = routes, ["final"] = measurement || settings.DefaultVpn ? "vpn" : "direct", ["auto_detect_interface"] = true, ["find_process"] = true, ["default_domain_resolver"] = "bootstrap" },
            ["experimental"] = new JsonObject { ["clash_api"] = new JsonObject { ["external_controller"] = $"127.0.0.1:{apiPort}", ["secret"] = apiSecret, ["access_control_allow_origin"] = new JsonArray(), ["access_control_allow_private_network"] = false } }
        };
    }
}
