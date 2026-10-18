using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BorisRay.Core;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { passed++; Console.WriteLine("PASS " + name); return; } throw new Exception("Accepted: " + name); }
var tcp = "vless://00000000-0000-4000-8000-000000000001@entry.example:443?security=reality&type=tcp&sni=example.com&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=12#Poland%20TCP";
var udp = "hysteria2://synthetic-password@udp.example:443?sni=example.com#Finland%20UDP";
var raw = tcp + "\n" + udp;
Check(new RoutingSettings().Sites.Count == 0, "new public client has no hidden provider-specific bypasses");
Check(Profiles.Parse(tcp.Replace("Poland%20TCP", "Germany%20TCP"))[0].Country == "de", "additional explicit countries supported");
var nodes = Profiles.Parse(raw);
Check(nodes.Count == 2 && nodes[0].Country == "pl" && nodes[1].Country == "fi", "explicit country and transport labels");
Check(Profiles.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))).Count == 2, "base64 list imports atomically");
Reject(() => Profiles.Parse(raw + "\nunsupported://secret"), "unsupported trailing node rejects whole import");
Reject(() => Profiles.Parse(udp.Replace("sni=", "insecure=1&sni=")), "insecure TLS rejected");
Reject(() => Profiles.Parse(tcp.Replace("type=tcp", "type=xhttp")), "unsupported XHTTP transport rejected explicitly");
Reject(() => Profiles.Parse("hysteria2://bad"), "missing host/port rejected");
Reject(() => Profiles.Parse(new string('x', Profiles.MaximumBytes + 1)), "bounded subscription");
Check(Profiles.SubscriptionUri("https://provider.example/j/synthetic-token").AbsolutePath == "/j/synthetic-token", "provider subscription URL is preserved");
Check(Profiles.SubscriptionUri("https://other.example/j/synthetic-token").AbsolutePath == "/j/synthetic-token", "external subscription path untouched");
Reject(() => Profiles.SubscriptionUri("https://user:secret@borishub.com/s/token"), "userinfo forbidden");
Reject(() => Profiles.SubscriptionUri("http://borishub.com/s/token"), "plaintext subscription forbidden");
Check(Profiles.Domain("ПРИМЕР.РФ.") == "xn--e1afmkfd.xn--p1ai", "IDN and trailing-dot normalization");
Reject(() => Profiles.Domain("https://yandex.ru/path"), "website path rejected");
Reject(() => Profiles.Domain("127.0.0.1"), "domain does not pretend to be an IP rule");
var app = new AppRule(@"C:\Program Files (x86)\Steam\steamapps\common\dota 2 beta\game\bin\win64\dota2.exe", false, true);
var pattern = Configuration.AppPattern(app);
Check(Regex.IsMatch(@"c:\PROGRAM FILES (x86)\Steam\steamapps\common\dota 2 beta\game\bin\win64\helper\child.exe", pattern), "case-insensitive real application folder and helpers");
Check(!Regex.IsMatch(@"C:\Program Files (x86)\Steam\steam.exe", pattern), "folder rule does not match parent Steam");
Check(!pattern.Contains(@"\ ", StringComparison.Ordinal), "RE2 pattern keeps spaces literal");
Reject(() => Configuration.AppPattern(new AppRule(@"C:\Users\Boris\Desktop\Dota 2.lnk", false)), "shortcut is never a process rule");
Reject(() => Configuration.AppPattern(new AppRule(@"C:\app.exe", false, true)), "drive-root folder rule rejected");
Check(Configuration.GlobalIPv6(IPAddress.Parse("2001:db8::1")) && !Configuration.GlobalIPv6(IPAddress.Parse("fe80::1")) && !Configuration.GlobalIPv6(IPAddress.Parse("fd00::1")), "physical IPv6 excludes link-local and ULA");
var settings = new RoutingSettings { Channel = "all", Apps = [app] };
settings.Sites["example.com"] = false; settings.Sites["sub.example.com"] = true;
var config = Configuration.Build(nodes, settings, false, 17991, "synthetic-api-secret");
Check(config["dns"]!["strategy"]!.GetValue<string>() == "ipv4_only", "IPv4-only physical DNS policy");
Check(Configuration.Build(nodes, settings, true, 17991, "synthetic-api-secret")["dns"]!["strategy"]!.GetValue<string>() == "prefer_ipv4", "global IPv6 policy");
var routes = config["route"]!["rules"]!.AsArray();
var suffix = routes.OfType<JsonObject>().Where(r => r.ContainsKey("domain_suffix")).Select(r => r["domain_suffix"]![0]!.GetValue<string>()).ToList();
Check(suffix.IndexOf("sub.example.com") < suffix.IndexOf("example.com"), "specific domain precedes parent");
var siteIndex = routes.Select((r, i) => (r, i)).First(x => x.r?["domain_suffix"] != null).i;
var appIndex = routes.Select((r, i) => (r, i)).First(x => x.r?["process_path_regex"] != null).i;
Check(siteIndex < appIndex, "domains precede process routing");
Check(config["inbounds"]![0]!["type"]!.GetValue<string>() == "tun" && config["experimental"]!["clash_api"]!["external_controller"]!.GetValue<string>().StartsWith("127.0.0.1:", StringComparison.Ordinal), "system TUN and authenticated loopback controller");
var measurement = Configuration.Build(nodes, settings, false, 17991, "synthetic-api-secret", 17992, "synthetic-proxy-password", true);
Check(measurement["route"]!["final"]!.GetValue<string>() == "vpn" && !measurement["route"]!["rules"]!.AsArray().OfType<JsonObject>().Any(r => r.ContainsKey("process_path_regex") || r.ContainsKey("domain_suffix")), "speed probe cannot inherit direct site/app rules");
settings.Country = "pl"; settings.Channel = "udp"; Reject(() => Configuration.Build(nodes, settings, false, 17991, "test"), "unavailable country/channel does not silently fall back");
var hierarchy = new RoutingSettings { Channel = "all", Apps = [app, new AppRule(@"C:\Program Files (x86)\Steam\steamapps\common\dota 2 beta\game\bin\win64\child.exe", true)] };
var ordered = Configuration.Build(nodes, hierarchy, false, 17991, "synthetic-api-secret")["route"]!["rules"]!.AsArray().OfType<JsonObject>().Where(r => r.ContainsKey("process_path_regex")).ToList();
Check(ordered[0]["outbound"]!.GetValue<string>() == "vpn", "exact executable rule precedes containing folder rule");
settings.Country = "auto"; settings.Channel = "all";
var output = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "borisray-windows-config-checks"); Directory.CreateDirectory(output);
for (var ipv6 = 0; ipv6 < 2; ipv6++)
{
    File.WriteAllText(Path.Combine(output, $"tun-{ipv6}.json"), Configuration.Build(nodes, settings, ipv6 == 1, 17991, "synthetic-api-secret").ToJsonString());
    File.WriteAllText(Path.Combine(output, $"probe-{ipv6}.json"), Configuration.Build(nodes, settings, ipv6 == 1, 17991, "synthetic-api-secret", 17992, "synthetic-proxy-password", true).ToJsonString());
}
var compatibility = SubscriptionChecks.Run(Check, Reject);
var importedIndex = 0;
foreach (var fixture in compatibility)
    File.WriteAllText(Path.Combine(output, $"subscription-{importedIndex++}.json"), Configuration.Build(fixture, new RoutingSettings(), false, 17991, "synthetic-api-secret").ToJsonString());
Console.WriteLine($"{passed} checks passed. Synthetic core configurations: {output}");
