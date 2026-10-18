using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BorisRay.Core;

public sealed record SubscriptionResult(List<Node> Nodes, string Format);

// Imports server definitions only. Provider listeners, DNS, routes and controllers
// never become part of the privileged engine configuration.
public static class SubscriptionParser
{
    private sealed class Unsupported(string message) : Exception(message);
    private static Unsupported Fail(string reason) => new(reason);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> Protocols = ["vless", "vmess", "trojan", "shadowsocks", "hysteria2", "tuic", "socks"];
    private static readonly HashSet<string> Helpers = ["direct", "block", "dns", "selector", "urltest", "freedom", "blackhole"];

    public static string Input(string input)
    {
        var value = input.Trim().TrimStart('\ufeff');
        if (value.StartsWith("happ://crypto", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Это зашифрованная ссылка Happ. Попроси у провайдера обычную HTTPS-подписку или экспорт списка серверов.");
        const string prefix = "happ://add/";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) value = Uri.UnescapeDataString(value[prefix.Length..]);
        return value;
    }

    public static SubscriptionResult Parse(string input)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(input) > Profiles.MaximumBytes) throw Fail("Подписка больше 1 МиБ.");
            var raw = Input(input);
            if (raw.Length == 0) throw Fail("Подписка пуста.");
            if (!raw.Contains("://", StringComparison.Ordinal) && raw[0] is not ('{' or '[') && !raw.Contains(':')) raw = Base64(raw).Trim().TrimStart('\ufeff');
            if (raw.Length == 0) throw Fail("Подписка пуста.");
            var result = new List<Node>(); string format;
            if (raw[0] is '{' or '[')
            {
                format = "JSON";
                var json = JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { MaxDepth = 48, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) ?? throw Fail("Пустой JSON.");
                ReadJson(json, result);
            }
            else if (Regex.IsMatch(raw, @"(?m)^\s*(?:proxies|proxy-providers)\s*:"))
            {
                format = "Clash YAML";
                var yaml = BoundedYaml.Parse(raw);
                if (yaml["proxies"] is not JsonArray proxies) throw Fail("В Clash YAML нет списка proxies. Нужен экспорт серверов, а не только proxy-providers.");
                foreach (var proxy in proxies) Add(result, () => Clash(Obj(proxy)));
            }
            else
            {
                format = "Список ссылок";
                var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => !l.StartsWith('#')).ToArray();
                if (lines.Length > 256) throw Fail("В подписке больше 256 серверов.");
                foreach (var line in lines) Add(result, () => Link(line));
            }
            if (result.Count is < 1 or > 256) throw Fail("В подписке должно быть от 1 до 256 поддерживаемых серверов.");
            return new(result, format);
        }
        catch (Unsupported e) { throw new ArgumentException(e.Message + " Текущая подписка сохранена."); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or DecoderFallbackException or UriFormatException or YamlDotNet.Core.YamlException)
        { throw new ArgumentException("Не удалось прочитать подписку. Нужен список ссылок, Xray/sing-box JSON или Clash YAML. Текущая подписка сохранена."); }
    }

    private static void Add(List<Node> nodes, Func<(JsonObject Outbound, string Name)> factory)
    {
        if (nodes.Count >= 256) throw Fail("В подписке больше 256 серверов.");
        try
        {
            var (outbound, name) = factory(); Validate(outbound);
            nodes.Add(Profiles.Named(outbound, name, nodes.Count));
        }
        catch (Unsupported e) { throw Fail($"Сервер {nodes.Count + 1}: {e.Message}"); }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or JsonException)
        { throw Fail($"Сервер {nodes.Count + 1}: проверь адрес, порт и параметры доступа."); }
    }

    private static string Base64(string text)
    {
        var s = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Replace('-', '+').Replace('_', '/');
        if (s.Length % 4 == 1) throw Fail("Некорректный Base64.");
        return Utf8.GetString(Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '=')));
    }
    private static JsonObject Obj(JsonNode? n) => n as JsonObject ?? throw Fail("Ожидался объект с настройками сервера.");
    private static string S(JsonObject n, string key, string fallback = "") => n[key] is JsonValue v ? v.ToString() : fallback;
    private static int I(JsonObject n, string key, int fallback = 0) => int.TryParse(S(n, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
    private static bool B(JsonObject n, string key) => S(n, key).Equals("true", StringComparison.OrdinalIgnoreCase) || S(n, key) == "1";
    private static JsonObject Copy(JsonObject n, string keys) { var r = new JsonObject(); foreach (var k in keys.Split(' ')) if (n[k] != null) r[k] = n[k]!.DeepClone(); return r; }
    private static void Only(JsonObject n, string allowed, string reason)
    { var keys = allowed.Split(' ').ToHashSet(StringComparer.Ordinal); if (n.Any(p => !keys.Contains(p.Key))) throw Fail(reason); }
    private static void Set(JsonObject n, string key, string value) { if (value.Length > 0) n[key] = value; }
    private static JsonArray Strings(string value) => new(value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
    private static JsonObject Server(string type, string host, int port) => new() { ["type"] = type, ["server"] = host.Trim('[', ']'), ["server_port"] = port };

    private static (JsonObject, string) Link(string value)
    {
        if (value.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return Vmess(Obj(JsonNode.Parse(Base64(value[8..].Split('#')[0]))));
        if (value.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
        {
            var tail = value[5..];
            if (!tail.Split('#', '?')[0].Contains('@'))
            {
                var suffix = tail.IndexOfAny(['#', '?']);
                value = "ss://" + Base64(suffix < 0 ? tail : tail[..suffix]) + (suffix < 0 ? "" : tail[suffix..]);
            }
        }
        if (value.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) && !value[8..].Split('#', '?')[0].Contains('@'))
            value = "socks://" + Base64(value[8..].Split('#')[0]) + (value.Contains('#') ? value[value.IndexOf('#')..] : "");
        var uri = new Uri(value, UriKind.Absolute);
        var type = uri.Scheme switch { "hy2" => "hysteria2", "ss" => "shadowsocks", "socks5" => "socks", _ => uri.Scheme };
        if (!Protocols.Contains(type)) throw Fail("Этот протокол пока не поддерживается. Доступны VLESS, VMess, Trojan, Shadowsocks, Hysteria2, TUIC и SOCKS5.");
        var q = new JsonObject();
        foreach (var item in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2); var key = Uri.UnescapeDataString(parts[0]);
            if (q.ContainsKey(key)) throw Fail("Повторяющийся параметр ссылки.");
            q[key] = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
        }
        if (B(q, "insecure") || B(q, "allowInsecure") || B(q, "allow-insecure") || B(q, "skip-cert-verify")) throw Fail("Отключение проверки TLS не поддерживается. Нужен профиль с корректным сертификатом.");
        if (new[] { "fragment", "noises", "pinSHA256", "ech", "extra" }.Any(k => S(q, k).Length > 0)) throw Fail("В ссылке есть дополнительные параметры, которые эта версия пока не поддерживает.");
        var port = uri.Port < 0 && type is "trojan" or "hysteria2" or "tuic" ? 443 : uri.Port;
        var n = Server(type, uri.Host, port); var credential = Uri.UnescapeDataString(uri.UserInfo);
        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
        switch (type)
        {
            case "vless":
                n["uuid"] = credential; Set(n, "flow", S(q, "flow"));
                if (S(q, "encryption", "none") != "none") throw Fail("Новый режим шифрования VLESS пока не поддерживается.");
                Transport(n, S(q, "type", "tcp"), S(q, "host"), S(q, "path"), S(q, "serviceName"));
                if (S(q, "headerType", "none") != "none") throw Fail("Этот TCP-заголовок пока не поддерживается.");
                Tls(n, S(q, "security", "none"), S(q, "sni", S(q, "peer", uri.Host)), S(q, "fp"), S(q, "pbk"), S(q, "sid"), q["alpn"] is null ? null : Strings(S(q, "alpn")));
                break;
            case "trojan":
                n["password"] = credential;
                Transport(n, S(q, "type", "tcp"), S(q, "host"), S(q, "path"), S(q, "serviceName"));
                Tls(n, S(q, "security", "tls"), S(q, "sni", S(q, "peer", uri.Host)), S(q, "fp"), S(q, "pbk"), S(q, "sid"), q["alpn"] is null ? null : Strings(S(q, "alpn")));
                break;
            case "shadowsocks":
                if (!credential.Contains(':')) credential = Base64(credential);
                var ss = credential.Split(':', 2); if (ss.Length != 2) throw Fail("Неполный Shadowsocks-профиль.");
                n["method"] = ss[0]; n["password"] = ss[1];
                if (S(q, "plugin").Length > 0) throw Fail("Shadowsocks с внешним плагином пока не поддерживается.");
                break;
            case "hysteria2":
                n["password"] = credential.Length > 0 ? credential : S(q, "auth");
                Tls(n, "tls", S(q, "sni", S(q, "peer", uri.Host)), "", "", "", q["alpn"] is null ? null : Strings(S(q, "alpn")));
                if (S(q, "obfs").Length > 0) n["obfs"] = new JsonObject { ["type"] = S(q, "obfs"), ["password"] = S(q, "obfs-password") };
                Ports(n, S(q, "mport", S(q, "ports")));
                if (S(q, "mportHopInt").Length > 0) n["hop_interval"] = S(q, "mportHopInt") + "s";
                break;
            case "tuic":
                var tuic = credential.Split(':', 2); n["uuid"] = tuic[0]; n["password"] = tuic.Length > 1 ? tuic[1] : "";
                Set(n, "congestion_control", S(q, "congestion_control")); Set(n, "udp_relay_mode", S(q, "udp_relay_mode"));
                n["zero_rtt_handshake"] = B(q, "zero_rtt_handshake");
                Tls(n, "tls", S(q, "sni", uri.Host), "", "", "", q["alpn"] is null ? null : Strings(S(q, "alpn")));
                break;
            case "socks":
                if (credential.Length > 0 && !credential.Contains(':')) credential = Base64(credential);
                var socks = credential.Split(':', 2); n["version"] = "5"; Set(n, "username", socks[0]); if (socks.Length > 1) n["password"] = socks[1];
                break;
        }
        if (S(q, "packetEncoding").Length > 0) n["packet_encoding"] = S(q, "packetEncoding");
        return (n, name);
    }

    private static (JsonObject, string) Vmess(JsonObject v)
    {
        if (B(v, "allowInsecure") || B(v, "insecure")) throw Fail("VMess отключает проверку TLS. Нужен корректный сертификат.");
        var n = Server("vmess", S(v, "add"), I(v, "port")); n["uuid"] = S(v, "id"); n["alter_id"] = I(v, "aid"); n["security"] = S(v, "scy", "auto");
        if (S(v, "type", "none") != "none") throw Fail("Этот VMess TCP-заголовок пока не поддерживается.");
        Transport(n, S(v, "net", "tcp"), S(v, "host"), S(v, "path"), S(v, "serviceName", S(v, "path")));
        Tls(n, S(v, "tls", "none"), S(v, "sni", S(v, "host", S(v, "add"))), S(v, "fp"), "", "", v["alpn"] is null ? null : Strings(S(v, "alpn")));
        return (n, S(v, "ps"));
    }

    private static void Transport(JsonObject n, string type, string host, string path, string service, JsonObject? headers = null)
    {
        type = type switch { "raw" => "tcp", "websocket" => "ws", "h2" => "http", "" => "tcp", _ => type };
        if (type == "tcp") return;
        if (type is not ("ws" or "grpc" or "http" or "httpupgrade")) throw Fail("Этот транспорт не поддерживается: XHTTP, mKCP и QUIC V2Ray пока недоступны. Возьми профиль TCP, WebSocket, gRPC или HTTPUpgrade.");
        var t = new JsonObject { ["type"] = type };
        if (type == "grpc") { t["service_name"] = service; }
        else
        {
            t["path"] = path.Length == 0 ? "/" : path;
            if (headers != null) t["headers"] = headers.DeepClone();
            if (type == "http") t["host"] = Strings(host);
            else if (type == "httpupgrade") Set(t, "host", host);
            else if (host.Length > 0) { t["headers"] ??= new JsonObject(); t["headers"]!["Host"] = host; }
            if (type == "ws")
            {
                var match = Regex.Match(path, @"(?:\?|&)ed=(\d+)(?:&|$)");
                if (match.Success)
                {
                    t["max_early_data"] = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture); t["early_data_header_name"] = "Sec-WebSocket-Protocol";
                    var p = path.Remove(match.Index, match.Length); t["path"] = p.Length == 0 ? "/" : p;
                }
            }
        }
        n["transport"] = t;
    }
    private static void Tls(JsonObject n, string mode, string sni, string fp, string publicKey, string shortId, JsonNode? alpn)
    {
        if (mode is "" or "none" or "false") return;
        if (mode is not ("tls" or "reality" or "true")) throw Fail("Этот режим TLS пока не поддерживается.");
        var t = new JsonObject { ["enabled"] = true, ["server_name"] = sni };
        if (alpn != null) t["alpn"] = alpn.DeepClone();
        if (fp.Length > 0 || mode == "reality") t["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = fp.Length > 0 ? fp : "chrome" };
        if (mode == "reality") t["reality"] = new JsonObject { ["enabled"] = true, ["public_key"] = publicKey, ["short_id"] = shortId };
        n["tls"] = t;
    }
    private static void Ports(JsonObject n, string value)
    {
        if (value.Length == 0) return;
        if (!Regex.IsMatch(value, "^[0-9,:-]+$")) throw Fail("Некорректный диапазон портов.");
        var ranges = new JsonArray();
        foreach (var part in value.Split(','))
        {
            var ends = part.Replace('-', ':').Split(':');
            if (ends.Length is < 1 or > 2 || !int.TryParse(ends[0], out var first) || first is < 1 or > 65535 ||
                !int.TryParse(ends[^1], out var last) || last < first || last > 65535) throw Fail("Некорректный диапазон портов.");
            ranges.Add($"{first}:{last}");
        }
        n["server_ports"] = ranges;
    }

    private static void ReadJson(JsonNode json, List<Node> result)
    {
        if (json is JsonArray array) { foreach (var entry in array) ReadJson(entry ?? throw Fail("Пустой JSON-профиль."), result); return; }
        var root = Obj(json);
        if (root["outbounds"] is JsonArray outbounds)
        {
            foreach (var item in outbounds)
            {
                var outbound = Obj(item); var type = S(outbound, "type", S(outbound, "protocol"));
                if (Helpers.Contains(type)) continue;
                Add(result, () => outbound.ContainsKey("protocol") ? Xray(outbound, S(root, "remarks")) : Native(outbound));
            }
        }
        else if (root["proxies"] is JsonArray proxies) foreach (var item in proxies) Add(result, () => Clash(Obj(item)));
        else Add(result, () => root.ContainsKey("protocol") ? Xray(root, "") : Native(root));
    }

    private static (JsonObject, string) Xray(JsonObject x, string label)
    {
        var type = S(x, "protocol");
        if (x["proxySettings"] != null || B(x["mux"] as JsonObject ?? new(), "enabled")) throw Fail("Xray-цепочки и Xray Mux пока не поддерживаются. Нужен обычный профиль сервера.");
        var settings = x["settings"] as JsonObject ?? new();
        var entries = settings[type is "vless" or "vmess" ? "vnext" : "servers"] as JsonArray;
        if (entries?.Count != 1) throw Fail("В Xray outbound нужен один сервер.");
        var server = Obj(entries[0]); var n = Server(type == "shadowsocks" ? "shadowsocks" : type, S(server, "address"), I(server, "port"));
        if (type is "vless" or "vmess")
        {
            var users = server["users"] as JsonArray; if (users?.Count != 1) throw Fail("В Xray сервере нужен один пользователь.");
            var user = Obj(users[0]); n["uuid"] = S(user, "id");
            if (type == "vless") { Set(n, "flow", S(user, "flow")); if (S(user, "encryption", "none") != "none") throw Fail("Этот режим шифрования VLESS пока не поддерживается."); }
            else { n["security"] = S(user, "security", "auto"); n["alter_id"] = I(user, "alterId"); }
        }
        else if (type == "shadowsocks") { n["method"] = S(server, "method"); n["password"] = S(server, "password"); }
        else if (type == "trojan") n["password"] = S(server, "password");
        else if (type == "socks")
        {
            n["version"] = "5";
            if (server["users"] is JsonArray users && users.Count > 0) { if (users.Count != 1) throw Fail("В SOCKS нужен один пользователь."); n["username"] = S(Obj(users[0]), "user"); n["password"] = S(Obj(users[0]), "pass"); }
        }
        else throw Fail("Этот Xray-протокол пока не поддерживается.");
        var stream = x["streamSettings"] as JsonObject ?? new();
        if (stream["sockopt"] is JsonObject options && options.Count > 0) throw Fail("Профиль требует дополнительные Xray sockopt. Нужен экспорт без специальных сетевых опций.");
        var network = S(stream, "network", "tcp"); var transport = stream[network switch { "h2" or "http" => "httpSettings", "raw" => "rawSettings", _ => network + "Settings" }] as JsonObject ?? new();
        if (network is "tcp" or "raw" && S(transport["header"] as JsonObject ?? new(), "type", "none") != "none") throw Fail("Этот Xray TCP-заголовок пока не поддерживается.");
        if (B(transport, "multiMode")) throw Fail("gRPC multiMode пока не поддерживается.");
        var host = transport["host"] is JsonArray hosts ? string.Join(',', hosts.Select(v => v?.ToString())) : S(transport, "host");
        Transport(n, network, host, S(transport, "path"), S(transport, "serviceName"), transport["headers"] as JsonObject);
        var security = S(stream, "security", "none"); var tls = stream[security == "reality" ? "realitySettings" : "tlsSettings"] as JsonObject ?? new();
        if (B(tls, "allowInsecure") || tls["certificates"] != null || tls["pinnedPeerCertSha256"] != null || S(tls, "echConfigList").Length > 0) throw Fail("Нестандартная проверка сертификата Xray пока не поддерживается.");
        Tls(n, security, S(tls, "serverName", S(server, "address")), S(tls, "fingerprint"), S(tls, "publicKey", S(tls, "password")), S(tls, "shortId"), tls["alpn"]);
        return (n, label.Length > 0 ? label : S(x, "tag"));
    }

    private static (JsonObject, string) Native(JsonObject source)
    {
        var type = S(source, "type");
        if (!Protocols.Contains(type)) throw Fail("Этот sing-box протокол пока не поддерживается.");
        var common = "type tag server server_port network tls multiplex ";
        var specific = type switch
        {
            "vless" => "uuid flow packet_encoding transport",
            "vmess" => "uuid security alter_id global_padding authenticated_length packet_encoding transport",
            "trojan" => "password transport",
            "shadowsocks" => "method password udp_over_tcp",
            "hysteria2" => "password server_ports hop_interval hop_interval_max up_mbps down_mbps obfs",
            "tuic" => "uuid password congestion_control udp_relay_mode udp_over_stream zero_rtt_handshake heartbeat",
            "socks" => "version username password", _ => ""
        };
        Only(source, common + specific, "В sing-box сервере есть неподдерживаемые параметры или зависимость от другого outbound. Нужен обычный профиль без цепочек и внешних файлов.");
        var n = Copy(source, common + specific);
        if (n["tls"] is JsonObject tls)
        {
            Only(tls, "enabled server_name disable_sni insecure alpn min_version max_version cipher_suites certificate_public_key_sha256 utls reality", "TLS с внешними файлами, ECH или нестандартными параметрами пока не поддерживается.");
            if (B(tls, "insecure")) throw Fail("Отключение проверки TLS не поддерживается.");
            if (tls["utls"] is JsonObject utls) Only(utls, "enabled fingerprint", "Неподдерживаемый uTLS.");
            if (tls["reality"] is JsonObject reality) Only(reality, "enabled public_key short_id", "Неподдерживаемый Reality.");
        }
        if (n["transport"] is JsonObject t)
        {
            var allowed = S(t, "type") switch
            {
                "ws" => "type path headers max_early_data early_data_header_name",
                "grpc" => "type service_name idle_timeout ping_timeout permit_without_stream",
                "http" => "type host path method headers idle_timeout ping_timeout",
                "httpupgrade" => "type host path headers", _ => throw Fail("Этот транспорт sing-box пока не поддерживается.")
            };
            Only(t, allowed, "Дополнительные параметры транспорта пока не поддерживаются.");
        }
        if (n["multiplex"] is JsonObject m) Only(m, "enabled protocol max_connections min_streams max_streams padding", "Нестандартный multiplex пока не поддерживается.");
        if (n["udp_over_tcp"] is JsonObject u) Only(u, "enabled version", "Нестандартный UDP over TCP.");
        if (n["obfs"] is JsonObject o) Only(o, "type password", "Нестандартная обфускация.");
        return (n, S(source, "tag"));
    }

    private static (JsonObject, string) Clash(JsonObject c)
    {
        var type = S(c, "type") switch { "ss" => "shadowsocks", "hy2" => "hysteria2", "socks5" => "socks", var t => t };
        if (!Protocols.Contains(type)) throw Fail("Этот Clash-протокол пока не поддерживается.");
        if (B(c, "skip-cert-verify")) throw Fail("Clash-профиль отключает проверку TLS. Нужен корректный сертификат.");
        if (S(c, "dialer-proxy").Length > 0 || S(c, "plugin").Length > 0 || c["smux"] != null || c["ech-opts"] != null || c["certificate"] != null || c["private-key"] != null || S(c, "fingerprint").Length > 0) throw Fail("Clash-цепочки, плагины, ECH и клиентские сертификаты пока не поддерживаются.");
        var n = Server(type, S(c, "server"), I(c, "port"));
        if (type is "vless" or "vmess" or "tuic") n["uuid"] = S(c, "uuid");
        if (type is "trojan" or "shadowsocks" or "hysteria2" or "tuic") n["password"] = S(c, "password");
        if (type == "vmess") { n["security"] = S(c, "cipher", "auto"); n["alter_id"] = I(c, "alterId"); }
        if (type == "vless") { Set(n, "flow", S(c, "flow")); if (S(c, "encryption", "none") != "none") throw Fail("Этот режим VLESS пока не поддерживается."); }
        if (type == "shadowsocks") n["method"] = S(c, "cipher");
        if (type == "socks") { n["version"] = "5"; Set(n, "username", S(c, "username")); Set(n, "password", S(c, "password")); }
        if (type == "tuic") { Set(n, "congestion_control", S(c, "congestion-controller")); Set(n, "udp_relay_mode", S(c, "udp-relay-mode")); n["zero_rtt_handshake"] = B(c, "reduce-rtt"); }
        var reality = c["reality-opts"] as JsonObject;
        if (type is not ("shadowsocks" or "socks")) Tls(n, reality != null ? "reality" : B(c, "tls") || type is "trojan" or "hysteria2" or "tuic" ? "tls" : "none", S(c, "servername", S(c, "sni", S(c, "server"))), S(c, "client-fingerprint"), reality is null ? "" : S(reality, "public-key"), reality is null ? "" : S(reality, "short-id"), c["alpn"]);
        if (type is "vless" or "vmess" or "trojan")
        {
            var network = S(c, "network", "tcp"); var opts = c[network + "-opts"] as JsonObject ?? new();
            var headers = opts["headers"] as JsonObject;
            var host = opts["host"] is JsonArray hosts ? string.Join(',', hosts.Select(v => v?.ToString())) : S(opts, "host");
            Transport(n, network, host, S(opts, "path"), S(opts, "grpc-service-name"), headers);
            if (n["transport"] is JsonObject t && network == "ws")
            { if (opts["max-early-data"] != null) t["max_early_data"] = I(opts, "max-early-data"); Set(t, "early_data_header_name", S(opts, "early-data-header-name")); }
        }
        if (type == "hysteria2")
        {
            if (S(c, "obfs").Length > 0) n["obfs"] = new JsonObject { ["type"] = S(c, "obfs"), ["password"] = S(c, "obfs-password") };
            Ports(n, S(c, "ports"));
        }
        if (c["udp"] != null && !B(c, "udp")) n["network"] = "tcp";
        return (n, S(c, "name"));
    }

    private static void Validate(JsonObject n)
    {
        var type = S(n, "type"); var host = S(n, "server");
        if (!Protocols.Contains(type) || Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Any(char.IsControl) || I(n, "server_port") is < 1 or > 65535) throw Fail("Некорректный адрес или порт сервера.");
        if (type is "vless" or "vmess" or "tuic" && !Guid.TryParseExact(S(n, "uuid"), "D", out _)) throw Fail("Некорректный UUID сервера.");
        if (type is "trojan" or "shadowsocks" or "hysteria2" or "tuic" && S(n, "password").Length == 0) throw Fail("Не указан пароль сервера.");
        if (type == "vless" && S(n, "flow") is not ("" or "xtls-rprx-vision")) throw Fail("Этот VLESS flow пока не поддерживается.");
        if (S(n, "flow") == "xtls-rprx-vision" && (n["transport"] != null || !B(n["tls"] as JsonObject ?? new(), "enabled"))) throw Fail("Vision требует TCP с TLS или Reality.");
        if (type == "vmess" && S(n, "security") is not ("auto" or "none" or "zero" or "aes-128-gcm" or "chacha20-poly1305" or "aes-128-cfb")) throw Fail("Этот VMess cipher пока не поддерживается.");
        if (type == "shadowsocks" && S(n, "method") is not ("aes-128-gcm" or "aes-192-gcm" or "aes-256-gcm" or "chacha20-ietf-poly1305" or "xchacha20-ietf-poly1305" or "2022-blake3-aes-128-gcm" or "2022-blake3-aes-256-gcm" or "2022-blake3-chacha20-poly1305")) throw Fail("Поддерживаются AEAD и Shadowsocks 2022; этот cipher недоступен.");
        var tls = n["tls"] as JsonObject;
        if (type is "trojan" or "hysteria2" or "tuic" && (tls == null || !B(tls, "enabled"))) throw Fail("Для этого протокола требуется TLS.");
        if (tls?["reality"] is JsonObject reality && B(reality, "enabled"))
        {
            if (!B(tls, "enabled") || type != "vless" || S(reality, "public_key").Length == 0 || !Regex.IsMatch(S(reality, "short_id"), "^(?:[0-9a-fA-F]{2}){0,8}$")) throw Fail("Некорректные параметры Reality.");
            var key = S(reality, "public_key").Replace('-', '+').Replace('_', '/');
            if (Convert.FromBase64String(key.PadRight((key.Length + 3) / 4 * 4, '=')).Length != 32) throw Fail("Некорректный открытый ключ Reality.");
        }
        if (n["obfs"] is JsonObject obfs && (S(obfs, "type") != "salamander" || S(obfs, "password").Length == 0)) throw Fail("Поддерживается обфускация Hysteria2 salamander с паролем.");
    }
}
