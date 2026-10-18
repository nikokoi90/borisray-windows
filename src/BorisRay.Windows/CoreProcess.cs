using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BorisRay.Core;

namespace BorisRay.Windows;

internal sealed class CoreProcess : IAsyncDisposable
{
    private static string DistributedCore => Path.Combine(AppContext.BaseDirectory, "core");
    private readonly string executable;
    private Process? process;
    private readonly string directory;
    private readonly bool captureOnly;
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly int apiPort = FreePort();
    private HttpClient? controller;
    private readonly IntPtr job;
    private readonly Queue<string> journal = new();
    private readonly object journalLock = new();
    public bool Running => process is { HasExited: false };
    public bool PhysicalIPv6 { get; private set; }
    internal string WorkDirectory => directory;
    public event Action? Exited;
    public CoreProcess(string root, bool captureOnly = false)
    {
        this.captureOnly = captureOnly;
        directory = captureOnly ? Directory.CreateDirectory(Path.Combine(root, "capture-core")).FullName : WindowsPlatform.SecureWorkDirectory();
        executable = Path.Combine(directory, "sing-box.exe");
        try
        {
            CopyVerified(Path.Combine(DistributedCore, "sing-box.exe"), executable, BuildInfo.CoreSha256);
            CopyVerified(Path.Combine(DistributedCore, "libcronet.dll"), Path.Combine(directory, "libcronet.dll"), BuildInfo.CoreDllSha256);
            job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var info = new JobInfo { Basic = new JobBasic { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref info, (uint)Marshal.SizeOf<JobInfo>())) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { if (job != IntPtr.Zero) CloseHandle(job); try { Directory.Delete(directory, true); } catch (IOException) { } throw; }
    }
    public static int FreePort()
    { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static void CopyVerified(string source, string target, string expected)
    {
        if (!File.Exists(source)) throw new InvalidOperationException("Ядро BorisRay отсутствует. Полностью распакуй архив приложения.");
        var bytes = File.ReadAllBytes(source);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expected) throw new InvalidOperationException("Файл ядра изменён. Полностью распакуй официальный архив приложения заново.");
        File.WriteAllBytes(target, bytes);
    }
    private void VerifyCore()
    {
        if (!File.Exists(executable) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))).ToLowerInvariant() != BuildInfo.CoreSha256)
            throw new InvalidOperationException("Ядро BorisRay отсутствует или изменено. Полностью распакуй официальный архив приложения заново.");
    }
    public async Task ValidateAsync(IReadOnlyList<Node> nodes, RoutingSettings settings)
    {
        VerifyCore(); var file = Path.Combine(directory, "check.json");
        File.WriteAllText(file, Configuration.Build(nodes, settings, WindowsPlatform.HasPhysicalIPv6(), apiPort, secret).ToJsonString());
        try
        {
            using var check = new Process { StartInfo = Info("check", file, true) };
            check.Start(); Assign(check); var stderr = check.StandardError.ReadToEndAsync();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await check.WaitForExitAsync(cancel.Token); } catch { if (!check.HasExited) check.Kill(true); throw; }
            var error = await stderr;
            if (check.ExitCode != 0) { Record(error); throw new InvalidOperationException("Ядро отклонило профиль. Проверь формат подписки и правила приложений."); }
        }
        finally { File.Delete(file); }
    }
    private ProcessStartInfo Info(string command, string config, bool noWindow) => new(executable)
    { UseShellExecute = false, CreateNoWindow = noWindow, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true,
        WorkingDirectory = directory, ArgumentList = { command, "-c", config } };
    public async Task StartAsync(IReadOnlyList<Node> nodes, RoutingSettings settings, int? proxyPort = null, string? password = null, bool measurement = false)
    {
        if (captureOnly) throw new InvalidOperationException("VPN отключён в режиме проверки интерфейса.");
        if (Running) throw new InvalidOperationException("Сначала отключи текущее соединение.");
        VerifyCore(); PhysicalIPv6 = WindowsPlatform.HasPhysicalIPv6();
        var config = Configuration.Build(nodes, settings, PhysicalIPv6, apiPort, secret, proxyPort, password, measurement);
        var file = Path.Combine(directory, "active.json"); File.WriteAllText(file, config.ToJsonString());
        controller = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{apiPort}"), Timeout = TimeSpan.FromSeconds(2) };
        controller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        process = new Process { StartInfo = Info("run", file, false), EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Record(e.Data); };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Record(e.Data); };
        process.Exited += (_, _) => Exited?.Invoke();
        try
        {
            process.Start(); Assign(process); process.BeginErrorReadLine(); process.BeginOutputReadLine();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            for (var i = 0; i < 40 && !startup.IsCancellationRequested; i++)
            {
                if (process.HasExited) throw new InvalidOperationException("Туннель не запустился. Проверь права администратора, другой VPN и журнал диагностики.");
                try { using var response = await controller.GetAsync("/version", startup.Token); if (response.IsSuccessStatusCode) return; } catch (HttpRequestException) { } catch (TaskCanceledException) { }
                await Task.Delay(150);
            }
            throw new InvalidOperationException("Туннель не ответил. Отключи другой VPN и повтори подключение.");
        }
        catch { await StopAsync(); throw; }
    }
    public async Task<JsonObject> ConnectionsAsync()
    {
        if (!Running || controller == null) throw new InvalidOperationException("VPN выключен");
        using var response = await controller.GetAsync("/connections"); response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }
    public async Task StopAsync()
    {
        var owned = process; process = null;
        if (owned != null)
        {
            try
            {
                if (!owned.HasExited)
                {
                    WindowsPlatform.SendCtrlC(owned.Id);
                    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await owned.WaitForExitAsync(cancel.Token); } catch (OperationCanceledException) { if (!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync(); }
                }
            }
            finally { owned.Dispose(); }
        }
        controller?.Dispose(); controller = null;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json")) File.Delete(file);
    }
    private void Record(string text)
    {
        text = Regex.Replace(text, @"[a-zA-Z0-9+.-]+://\S+", "[ссылка скрыта]");
        text = Regex.Replace(text, @"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}", "[ключ скрыт]");
        text = Regex.Replace(text, @"(?i)(password|secret|token|uuid)\s*[:=]\s*\S+", "$1=[скрыто]");
        lock (journalLock) { journal.Enqueue(text.Length > 1000 ? text[..1000] : text); while (journal.Count > 120) journal.Dequeue(); }
    }
    public string Journal() { lock (journalLock) return string.Join(Environment.NewLine, journal); }
    private void Assign(Process child)
    { if (!AssignProcessToJobObject(job, child.Handle)) { child.Kill(true); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); } }
    public async ValueTask DisposeAsync()
    { await StopAsync(); CloseHandle(job); try { Directory.Delete(directory, true); } catch (IOException) { } }
    [StructLayout(LayoutKind.Sequential)] private struct JobBasic
    { public long PerProcessTime, PerJobTime; public uint LimitFlags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct JobInfo { public JobBasic Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref JobInfo info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
