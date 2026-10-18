using System.Text;
using System.Text.Json.Nodes;
using BorisRay.Core;

internal static class SubscriptionChecks
{
    public static List<List<Node>> Run(Action<bool, string> check, Action<Action, string> reject)
    {
        const string id = "00000000-0000-4000-8000-000000000001";
        const string reality = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        string Url64(string s) => B64(s).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var vmess = new JsonObject { ["v"] = "2", ["ps"] = "Germany VMess", ["add"] = "vmess.example", ["port"] = "443", ["id"] = id, ["aid"] = "0", ["net"] = "ws", ["type"] = "none", ["tls"] = "tls", ["host"] = "cdn.example", ["path"] = "/socket?ed=2048", ["sni"] = "cdn.example", ["scy"] = "auto" };
        var links = new[]
        {
            $"vless://{id}@vless.example:443?security=tls&type=ws&host=cdn.example&path=%2Fws&sni=cdn.example#France",
            $"vless://{id}@vless.example:443?security=reality&type=grpc&serviceName=service&pbk={reality}&sid=12&sni=www.example#Germany",
            $"vless://{id}@vless.example:443?security=tls&type=httpupgrade&host=cdn.example&path=%2Fupgrade#Netherlands",
            $"vless://{id}@vless.example:443?security=tls&type=http&host=cdn.example&path=%2Fh2#Poland",
            $"vless://{id}@vless.example:8080?security=none&type=tcp#NoTLS",
            "vmess://" + B64(vmess.ToJsonString()),
            "trojan://synthetic%3Apassword@trojan.example:443?sni=cdn.example&type=grpc&serviceName=trojan#Japan",
            "ss://" + Url64("aes-128-gcm:synthetic-password") + "@ss.example:8388#Sweden",
            "ss://" + B64("chacha20-ietf-poly1305:synthetic-password@legacy.example:8388") + "#Legacy",
            "ss://2022-blake3-aes-128-gcm:AAAAAAAAAAAAAAAAAAAAAA%3D%3D@ss2022.example:8388#SS2022",
            "hy2://synthetic%3Apassword@hy2.example?sni=hy2.example&obfs=salamander&obfs-password=synthetic-obfs&mport=443,4500-4510#Finland",
            $"tuic://{id}:synthetic-password@tuic.example:443?congestion_control=bbr&udp_relay_mode=native&alpn=h3#Singapore",
            "socks://user:synthetic-password@socks.example:1080#SOCKS5",
            "socks://" + B64("user:synthetic-password") + "@socks.example:1080#SOCKS5Base64",
            "trojan://synthetic-password@[2001:db8::1]:443?sni=cdn.example#IPv6"
        };
        var configs = new List<List<Node>>();
        foreach (var link in links) { var nodes = Profiles.Parse(link); check(nodes.Count == 1, "imports common link variant " + configs.Count); configs.Add(nodes); }
        check(configs[0][0].Outbound["transport"]?["headers"]?["Host"]?.ToString() == "cdn.example", "WebSocket Host preserved");
        check(configs[5][0].Outbound["transport"]?["path"]?.ToString() == "/socket" && configs[5][0].Outbound["transport"]?["max_early_data"]?.ToString() == "2048", "VMess early-data mapping preserves transport semantics");
        check(configs[10][0].Outbound["password"]?.ToString() == "synthetic:password" && configs[10][0].Channel == "udp", "Hysteria2 userpass and UDP channel");
        configs.Add(Profiles.Parse(Url64("#profile-title: Synthetic provider\n" + string.Join('\n', links))));
        check(configs[^1].Count == links.Length, "URL-safe unpadded Base64 and metadata comments");
        check(SubscriptionParser.Input("happ://add/https%3A%2F%2Fprovider.example%2Fsubscription%3Ftoken%3Dsynthetic") == "https://provider.example/subscription?token=synthetic", "plain Happ add wrapper extracts exact subscription URL");
        reject(() => SubscriptionParser.Input("happ://cryptov2/synthetic"), "encrypted Happ link rejected with actionable guidance");
        reject(() => Profiles.Parse(links[0].Replace("security=tls", "security=tls&allowInsecure=true")), "boolean insecure TLS never enabled implicitly");
        reject(() => Profiles.Parse(links[0].Replace("type=ws", "type=ws&fragment=1-10,5-20,tlshello")), "unsupported fragmentation does not silently disappear");
        reject(() => Profiles.Parse(links[0].Replace("type=ws", "type=ws&type=tcp")), "duplicate query rejected");
        reject(() => Profiles.Parse(links[0].Replace("type=ws", "type=ws&flow=xtls-rprx-vision")), "Vision cannot silently become WebSocket");
        reject(() => Profiles.Parse(links[0].Replace("security=tls", "encryption=unsupported&security=tls")), "new VLESS encryption is explicit unsupported");
        reject(() => Profiles.Parse(links[7] + "\nss://" + Url64("aes-128-gcm:synthetic-password") + "@ss.example:8388?plugin=external"), "unsupported final plugin preserves atomic import");
        reject(() => Profiles.Parse("<html>Login required</html>"), "HTML subscription landing page is not accepted as a profile");
        try { Profiles.Parse("trojan://never-print-this-password@private.example:443?insecure=true"); }
        catch (ArgumentException e) { check(!e.Message.Contains("never-print-this-password") && !e.Message.Contains("private.example"), "parse error excludes server credentials and address"); }

        var native = new JsonObject { ["outbounds"] = new JsonArray(configs[0][0].Outbound.DeepClone(), configs[10][0].Outbound.DeepClone(), new JsonObject { ["type"] = "selector", ["tag"] = "select", ["outbounds"] = new JsonArray("x") }, new JsonObject { ["type"] = "direct", ["tag"] = "direct" }), ["inbounds"] = new JsonArray(new JsonObject { ["type"] = "mixed", ["listen"] = "0.0.0.0" }), ["experimental"] = new JsonObject { ["clash_api"] = new JsonObject { ["external_controller"] = "0.0.0.0:9090" } }, ["route"] = new JsonObject { ["final"] = "direct" } };
        configs.Add(Profiles.Parse(native.ToJsonString()));
        var normalized = Configuration.Build(configs[^1], new RoutingSettings(), false, 17991, "synthetic");
        check(configs[^1].Count == 2 && normalized["inbounds"]?[0]?["type"]?.ToString() == "tun" && normalized["route"]?["final"]?.ToString() == "vpn" && normalized["experimental"]?["clash_api"]?["external_controller"]?.ToString() == "127.0.0.1:17991", "sing-box JSON extracts servers without exposing provider controller/routes/listener");
        var unsafeNative = (JsonObject)configs[0][0].Outbound.DeepClone(); unsafeNative["detour"] = "other";
        reject(() => Profiles.Parse(unsafeNative.ToJsonString()), "dependent sing-box detour is never silently changed");
        unsafeNative.Remove("detour"); unsafeNative["tls"]!["certificate_path"] = "C:\\private.pem";
        reject(() => Profiles.Parse(unsafeNative.ToJsonString()), "subscription cannot read arbitrary certificate file");
        unsafeNative["tls"]!.AsObject().Remove("certificate_path"); unsafeNative["tls"]!["insecure"] = true;
        reject(() => Profiles.Parse(unsafeNative.ToJsonString()), "JSON insecure TLS rejected");
        const string xray = """
        [{"remarks":"Poland Xray","inbounds":[{"listen":"0.0.0.0","port":1080,"protocol":"socks"}],"outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"xray.example","port":443,"users":[{"id":"00000000-0000-4000-8000-000000000001","encryption":"none","flow":"xtls-rprx-vision"}]}]},"streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"www.example","fingerprint":"chrome","publicKey":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","shortId":"12"}}},{"protocol":"freedom","tag":"direct"}]}]
        """;
        configs.Add(Profiles.Parse(xray)); check(configs[^1][0].Country == "pl" && configs[^1][0].Outbound["flow"]?.ToString() == "xtls-rprx-vision", "Happ-style Xray JSON array preserves Reality and explicit remarks");
        reject(() => Profiles.Parse(xray.Replace("\"network\":\"tcp\"", "\"network\":\"xhttp\"")), "Xray XHTTP explicitly rejected");
        var xrayWs = JsonNode.Parse(xray)![0]!.AsObject();
        var outbound = xrayWs["outbounds"]![0]!.AsObject(); outbound["protocol"] = "vmess"; outbound["settings"]!["vnext"]![0]!["users"]![0]!.AsObject().Remove("flow");
        outbound["streamSettings"] = new JsonObject { ["network"] = "ws", ["security"] = "tls", ["tlsSettings"] = new JsonObject { ["serverName"] = "cdn.example" }, ["wsSettings"] = new JsonObject { ["path"] = "/ws", ["headers"] = new JsonObject { ["Host"] = "cdn.example" } } };
        configs.Add(Profiles.Parse(xrayWs.ToJsonString())); check(configs[^1][0].Outbound["type"]?.ToString() == "vmess", "Xray VMess WebSocket TLS normalized");
        const string yaml = """
        mixed-port: 7890
        external-controller: 0.0.0.0:9090
        defaults: &common
          type: trojan
          server: clash.example
          port: 443
          password: synthetic-password
          sni: cdn.example
        proxies:
          - <<: *common
            name: 'Japan Trojan'
            network: ws
            ws-opts:
              path: /ws
              headers: { Host: cdn.example }
          - name: Finland HY2
            type: hysteria2
            server: hy2.example
            port: 443
            password: synthetic-password
            sni: hy2.example
            obfs: salamander
            obfs-password: synthetic-obfs
        rules: [MATCH,DIRECT]
        """;
        configs.Add(Profiles.Parse(yaml)); check(configs[^1].Count == 2 && configs[^1][0].Country == "jp" && configs[^1][0].Outbound["transport"]?["headers"]?["Host"]?.ToString() == "cdn.example", "Clash YAML safe anchor/merge import preserves host and countries");
        reject(() => Profiles.Parse(yaml.Replace("sni: cdn.example", "skip-cert-verify: true\n  sni: cdn.example")), "Clash insecure TLS rejected");
        reject(() => Profiles.Parse("proxies: &cycle [*cycle]"), "cyclic YAML alias rejected without recursion");
        reject(() => Profiles.Parse("proxies: []\nproxies: []"), "duplicate YAML key rejected");
        reject(() => Profiles.Parse("proxy-providers:\n  provider:\n    url: https://private.example/list"), "external proxy-provider is never fetched implicitly");
        reject(() => Profiles.Parse(string.Join('\n', Enumerable.Repeat(links[0], 257))), "node count limit enforced across formats");
        check(SubscriptionParser.Parse(yaml).Format == "Clash YAML" && SubscriptionParser.Parse(xray).Format == "JSON", "detected format available to UI");
        return configs;
    }
}
