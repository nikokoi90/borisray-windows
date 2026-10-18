using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using BorisRay.Core;

namespace BorisRay.Windows;

internal sealed record DeviceProfile(string Raw, string Source, string Format = "Подписка");

internal sealed class PrivateStore
{
    public string Root { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public RoutingSettings Settings { get; private set; } = new();
    public DeviceProfile? Profile { get; private set; }
    public List<Node> Nodes { get; private set; } = [];

    public PrivateStore(string? isolatedRoot = null)
    {
        Root = isolatedRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BorisRay");
        Directory.CreateDirectory(Root);
        var access = new DirectorySecurity();
        access.SetAccessRuleProtection(true, false);
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user is unavailable");
        access.SetOwner(user);
        access.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Root).SetAccessControl(access);
        var settings = Path.Combine(Root, "settings.json");
        if (File.Exists(settings)) Settings = JsonSerializer.Deserialize<RoutingSettings>(File.ReadAllBytes(settings)) ?? new();
        var profile = Path.Combine(Root, "profile.dpapi");
        if (File.Exists(profile))
        {
            try { Profile = JsonSerializer.Deserialize<DeviceProfile>(Protect(File.ReadAllBytes(profile), false)); if (Profile != null) Nodes = Profiles.Parse(Profile.Raw); }
            catch { throw new InvalidOperationException("Не удалось открыть локальную подписку. Запусти BorisRay под тем же пользователем Windows."); }
        }
    }

    public void SaveSettings() => Atomic(Path.Combine(Root, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(Settings, Json));
    public void SaveProfile(DeviceProfile profile, List<Node> nodes)
    {
        Atomic(Path.Combine(Root, "profile.dpapi"), Protect(JsonSerializer.SerializeToUtf8Bytes(profile), true));
        Profile = profile; Nodes = nodes;
        if (!nodes.Any(n => Settings.Channel == "all" || n.Channel == Settings.Channel)) Settings.Channel = "all";
        Settings.Country = "auto"; Settings.Server = "auto"; SaveSettings();
    }
    private static void Atomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp"; File.WriteAllBytes(temp, bytes); File.Move(temp, path, true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, "BorisRay device subscription", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}
