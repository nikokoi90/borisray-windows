using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using BorisRay.Core;

namespace BorisRay.Windows;

internal static class WindowsPlatform
{
    public static string SecureWorkDirectory()
    {
        // Program Files is not a user-writable parent. Low-integrity processes must
        // not be able to replace the engine or its plaintext session configuration.
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BorisRay Engine");
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(admins);
        foreach (var sid in new[] { admins, system }) security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        // CreateDirectory with this ACL prevents a writable creation window. A pre-existing
        // reparse point is rejected before any privileged binary/configuration is written.
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Папка системного движка является ссылкой. Выбери чистую установку BorisRay.");
        FileSystemAclExtensions.CreateDirectory(security, root);
        var owner = new DirectoryInfo(root).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        if (!admins.Equals(owner) && !system.Equals(owner)) throw new InvalidOperationException("У папки движка неожиданный владелец. Проверь установку BorisRay.");
        var directory = Path.Combine(root, "session-" + Guid.NewGuid().ToString("N"));
        FileSystemAclExtensions.CreateDirectory(security, directory); return directory;
    }
    public static bool HasPhysicalIPv6() => NetworkInterface.GetAllNetworkInterfaces().Any(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet &&
        !n.Name.Contains("BorisRay", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(n.Description, "VPN|Wintun|WireGuard|TAP|Hyper-V|VirtualBox", RegexOptions.IgnoreCase) &&
        n.GetIPProperties().UnicastAddresses.Any(a => Configuration.GlobalIPv6(a.Address)) &&
        n.GetIPProperties().GatewayAddresses.Any(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !a.Address.Equals(System.Net.IPAddress.IPv6Any)));

    public static string ResolveExecutable(string input)
    {
        var path = Path.GetFullPath(input);
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new ArgumentException("Не удалось прочитать ярлык. Выбери настоящий .exe.");
            dynamic shell = Activator.CreateInstance(type)!;
            try { dynamic link = shell.CreateShortcut(path); try { path = (string)link.TargetPath; } finally { Marshal.FinalReleaseComObject(link); } }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        else if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            var data = File.ReadAllText(path); var game = Regex.Match(data, @"steam://rungameid/(\d+)", RegexOptions.IgnoreCase);
            if (!game.Success) throw new ArgumentException("Это интернет-ярлык. Выбери .exe приложения.");
            path = ResolveSteam(game.Groups[1].Value);
        }
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Ярлык не указывает на доступный .exe. Добавь файл приложения вручную.");
        return path;
    }
    private static string ResolveSteam(string id)
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrWhiteSpace(steam)) throw new ArgumentException("Steam не найден. Выбери .exe установленной игры.");
        var roots = new List<string> { steam };
        var libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraries)) roots.AddRange(Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var manifest = Path.Combine(root, "steamapps", $"appmanifest_{id}.acf"); if (!File.Exists(manifest)) continue;
            var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!match.Success) continue;
            var directory = Path.GetFullPath(Path.Combine(root, "steamapps", "common", match.Groups[1].Value));
            var common = Path.GetFullPath(Path.Combine(root, "steamapps", "common")) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(common, StringComparison.OrdinalIgnoreCase)) continue;
            if (id == "570")
            { var dota = Path.Combine(directory, "game", "bin", "win64", "dota2.exe"); if (File.Exists(dota)) return dota; }
            throw new ArgumentException("Папка игры найдена. Выбери её основной .exe — случайный файл из папки не будет выбран автоматически.");
        }
        throw new ArgumentException("Игра Steam не установлена. Проверь её установку или выбери настоящий .exe.");
    }

    public static bool SendCtrlC(int processId)
    {
        if (!AttachConsole((uint)processId)) return false;
        try { SetConsoleCtrlHandler(IntPtr.Zero, true); return GenerateConsoleCtrlEvent(0, 0); }
        finally { FreeConsole(); SetConsoleCtrlHandler(IntPtr.Zero, false); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint id);
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll")] private static extern bool GenerateConsoleCtrlEvent(uint type, uint group);
}
