using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BorisRay.Core;

namespace BorisRay.Windows;

internal static class Transfers
{
    public static async Task<string> FetchSubscriptionAsync(string input, CancellationToken token = default)
    {
        if (!input.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return input;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25)); token = timeout.Token;
        var uri = Profiles.SubscriptionUri(input);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("sing-box/1.14.3 BorisRay/0.2.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain, application/json, application/yaml, text/yaml, */*");
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (redirects == 3 || response.Headers.Location == null) throw new InvalidOperationException("Слишком много перенаправлений подписки. Возьми прямую HTTPS-ссылку у провайдера.");
                uri = Profiles.SubscriptionUri(new Uri(uri, response.Headers.Location).AbsoluteUri); continue;
            }
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Сервер подписки не ответил успешно. Проверь ссылку и интернет.");
            var bytes = await ReadBoundedAsync(response.Content, Profiles.MaximumBytes, token);
            return Encoding.UTF8.GetString(bytes);
        }
        throw new InvalidOperationException("Не удалось получить подписку.");
    }
    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidOperationException("Ответ слишком большой.");
        using var result = new MemoryStream(); await using var stream = await content.ReadAsStreamAsync(token);
        var buffer = new byte[65_536]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        { if (result.Length + count > limit) throw new InvalidOperationException("Ответ слишком большой."); await result.WriteAsync(buffer.AsMemory(0, count), token); }
        return result.ToArray();
    }

    public static async Task MeasureAsync(PrivateStore store, IProgress<string> progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(120)); token = timeout.Token;
        var port = CoreProcess.FreePort(); var password = Guid.NewGuid().ToString("N");
        await using var probe = new CoreProcess(store.Root);
        progress.Report("Подключаю выбранный VPN-сервер для замера…");
        await probe.StartAsync(store.Nodes, store.Settings, port, password, true);
        using var handler = new HttpClientHandler { Proxy = new WebProxy($"http://127.0.0.1:{port}") { Credentials = new NetworkCredential("boris", password) }, UseProxy = true, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        var pings = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync("https://cp.cloudflare.com/generate_204", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode(); await ReadBoundedAsync(response.Content, 65_536, token); pings.Add(watch.Elapsed.TotalMilliseconds);
        }
        var ping = pings.Order().ElementAt(1);
        progress.Report($"Пинг: {ping:F0} мс · измеряю загрузку…");
        var downloadWatch = Stopwatch.StartNew();
        using var download = await client.GetAsync("https://speed.cloudflare.com/__down?bytes=5242880", HttpCompletionOption.ResponseHeadersRead, token);
        download.EnsureSuccessStatusCode(); var received = await ReadBoundedAsync(download.Content, 5_242_880, token);
        if (received.Length != 5_242_880) throw new InvalidOperationException("Замер загрузки не завершился. Повтори или выбери другой сервер.");
        var down = received.Length * 8 / downloadWatch.Elapsed.TotalSeconds / 1_000_000;
        progress.Report($"Пинг: {ping:F0} мс · загрузка: {down:F1} Мбит/с · измеряю отдачу…");
        var uploadWatch = Stopwatch.StartNew(); using var content = new ByteArrayContent(new byte[1_048_576]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var upload = await client.PostAsync("https://speed.cloudflare.com/__up", content, token); upload.EnsureSuccessStatusCode();
        await ReadBoundedAsync(upload.Content, 65_536, token);
        var up = 1_048_576 * 8 / uploadWatch.Elapsed.TotalSeconds / 1_000_000;
        progress.Report($"Пинг: {ping:F0} мс   ·   загрузка: {down:F1} Мбит/с   ·   отдача: {up:F1} Мбит/с");
    }

    public const string UpdatesBase = "https://fv1.borishub.com/downloads/borisray/windows/";
    public static async Task<(Version Version, Uri Download)> UpdateAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        using var response = await client.GetAsync(UpdatesBase + "latest.json", HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        var data = await ReadBoundedAsync(response.Content, 16_384, timeout.Token);
        var manifest = JsonNode.Parse(data) as JsonObject ?? throw new InvalidOperationException("Некорректный ответ сервера обновлений.");
        if (!Version.TryParse(manifest["version"]?.GetValue<string>(), out var version) || !Uri.TryCreate((manifest["installer_url"] ?? manifest["url"])?.GetValue<string>(), UriKind.Absolute, out var url))
            throw new InvalidOperationException("Некорректный ответ сервера обновлений.");
        if (manifest["platform"]?.GetValue<string>() != "win-x64" || url.Scheme != "https" || url.Host != "fv1.borishub.com" || !url.IsDefaultPort || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(url.AbsolutePath, @"^/downloads/borisray/windows/BorisRay-Windows-(?:x64-[0-9.]+\.zip|Setup-x64-[0-9.]+\.exe)$"))
            throw new InvalidOperationException("Некорректный ответ сервера обновлений.");
        return (version, url);
    }
}
