using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using BorisRay.Core;
using BorisRay.Windows;

internal static class Capture
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var output = Path.GetFullPath(args.Single()); Directory.CreateDirectory(output);
        var temporary = Path.Combine(Path.GetTempPath(), "BorisRay-capture-" + Guid.NewGuid().ToString("N"));
        var captures = new List<object>();
        try
        {
            var fixture = new PrivateStore(Path.Combine(temporary, "fixture"));
            const string raw = "vless://00000000-0000-4000-8000-000000000001@entry.example:443?security=reality&type=tcp&sni=example.com&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=12#Poland%20TCP\nhysteria2://synthetic-password@udp.example:443?sni=example.com#Finland%20UDP";
            fixture.SaveProfile(new DeviceProfile(raw, ""), Profiles.Parse(raw)); fixture.Settings.Channel = "all";
            fixture.Settings.Sites["gosuslugi.ru"] = false;
            fixture.Settings.Apps.Add(new AppRule(@"C:\Program Files\Example application\example.exe", false, true)); fixture.SaveSettings();
            // Exercise DPAPI persistence in the same Windows identity, never a real user subscription.
            var reopened = new PrivateStore(fixture.Root);
            if (reopened.Nodes.Count != 2 || reopened.Settings.Apps.Count != 1) throw new Exception("DPAPI/settings reload failed");
            using (var window = new MainWindow(reopened, captureOnly: true))
            {
                window.Show(); Application.DoEvents();
                foreach (var pair in new[] { ("Подключение", "connection"), ("Приложения", "apps"), ("Сайты", "sites"), ("Соединения", "connections"), ("О приложении", "about") })
                {
                    window.ClientSize = new Size(1100, 780); window.ShowPage(pair.Item1); Snap(window, pair.Item2 + "-desktop");
                    window.ClientSize = new Size(930, 640); window.ShowPage(pair.Item1); Snap(window, pair.Item2 + "-compact");
                }
                Finish(window);
            }
            using (var empty = new MainWindow(new PrivateStore(Path.Combine(temporary, "empty")), captureOnly: true))
            { empty.Show(); Application.DoEvents(); Snap(empty, "connection-empty"); Finish(empty); }
            File.WriteAllText(Path.Combine(output, "native-capture.json"), JsonSerializer.Serialize(new
            {
                os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
                method = "Native Windows Forms DrawToBitmap on standard GitHub Windows runner; synthetic fixtures; no VPN started",
                dpapiReload = "passed", captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS native Windows Forms capture and isolated DPAPI round trip"); return 0;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }

        void Snap(MainWindow window, string name)
        {
            window.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var distinct = new HashSet<int>(); for (var y = 0; y < bitmap.Height; y += 11) for (var x = 0; x < bitmap.Width; x += 11) distinct.Add(bitmap.GetPixel(x, y).ToArgb());
            if (distinct.Count < 20) throw new Exception("Blank capture " + name);
            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            captures.Add(new { name, bitmap.Width, bitmap.Height, sampledColors = distinct.Count, dpi = window.DeviceDpi });
        }
    }
    private static void Finish(MainWindow window)
    { window.Close(); var limit = DateTime.UtcNow.AddSeconds(10); while (!window.IsDisposed && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(10); } if (!window.IsDisposed) throw new Exception("Window failed to close"); }
}
