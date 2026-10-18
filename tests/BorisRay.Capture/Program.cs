using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
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
            // Real privileged staging + JobObject child validation, without starting TUN.
            var owned = new CoreProcess(fixture.Root);
            var work = owned.WorkDirectory;
            try
            {
                var acl = new DirectoryInfo(work).GetAccessControl();
                if (!acl.AreAccessRulesProtected) throw new Exception("Session ACL is inherited");
                var allowed = new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) };
                foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                    if (rule.AccessControlType == AccessControlType.Allow && !allowed.Contains(rule.IdentityReference)) throw new Exception("Unexpected principal in session ACL");
                Wait(owned.ValidateAsync(reopened.Nodes, reopened.Settings));
            }
            finally { Wait(owned.DisposeAsync().AsTask()); }
            if (Directory.Exists(work)) throw new Exception("Owned session folder survived disposal");
            var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new Exception("WScript unavailable");
            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = Path.Combine(fixture.Root, "fixture.lnk");
            try { dynamic link = shell.CreateShortcut(shortcut); try { link.TargetPath = Application.ExecutablePath; link.Save(); } finally { Marshal.FinalReleaseComObject(link); } }
            finally { Marshal.FinalReleaseComObject(shell); }
            if (!WindowsPlatform.ResolveExecutable(shortcut).Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) throw new Exception("Shortcut did not resolve to executable");
            using (var window = new MainWindow(reopened, captureOnly: true))
            {
                window.Show(); Application.DoEvents();
                foreach (var pair in new[] { ("Подключение", "connection"), ("Приложения", "apps"), ("Сайты", "sites"), ("Соединения", "connections"), ("О приложении", "about") })
                {
                    window.ClientSize = new Size(1100, 780); window.ShowPage(pair.Item1); Snap(window, pair.Item2 + "-desktop");
                    window.ClientSize = new Size(930, 640); window.ShowPage(pair.Item1); Snap(window, pair.Item2 + "-compact");
                }
                window.ShowPage("Подключение"); window.ClientSize = new Size(1100, 780);
                SendMessage(window.Handle, 0x127, 0x10002, 0); Descendants(window).OfType<Button>().Single(b => b.Text == "Подключить VPN").Focus(); Snap(window, "connection-keyboard");
                window.ShowPage("Сайты");
                var sites = Descendants(window).OfType<DataGridView>().Single(); sites.Rows[0].Cells[1].Value = "Через VPN";
                if (!reopened.Settings.Sites["gosuslugi.ru"]) throw new Exception("Native site route edit failed");
                sites.Rows[0].Cells[1].Value = "Напрямую";
                window.ShowPage("Приложения"); var apps = Descendants(window).OfType<DataGridView>().Single(); apps.Rows[0].Cells[3].Value = false;
                if (reopened.Settings.Apps[0].IncludeFolder) throw new Exception("Native executable folder edit failed");
                apps.Rows[0].Cells[3].Value = true;
                window.ShowPage("Подключение"); window.ClientSize = new Size(1100, 780);
                using var modalTimer = new System.Windows.Forms.Timer { Interval = 150 };
                modalTimer.Tick += (_, _) =>
                {
                    modalTimer.Stop(); var modal = Application.OpenForms.Cast<Form>().Single(f => f.Text == "Импорт подписки");
                    var input = Descendants(modal).OfType<TextBox>().Single(); if (!input.UseSystemPasswordChar) throw new Exception("Subscription input is not masked");
                    input.Text = "https://provider.example/subscription/synthetic"; Snap(modal, "import-dialog"); modal.Close();
                };
                modalTimer.Start(); Descendants(window).OfType<Button>().Single(b => b.Text == "Заменить подписку…").PerformClick();
                using var ruleTimer = new System.Windows.Forms.Timer { Interval = 150 };
                ruleTimer.Tick += (_, _) => { ruleTimer.Stop(); var modal = Application.OpenForms.Cast<Form>().Single(f => f.Text == "Маршрут приложения"); Snap(modal, "app-rule-dialog"); modal.Close(); };
                ruleTimer.Start(); window.AddAppPath(Application.ExecutablePath);
                Finish(window);
            }
            using (var empty = new MainWindow(new PrivateStore(Path.Combine(temporary, "empty")), captureOnly: true))
            { empty.Show(); Application.DoEvents(); Snap(empty, "connection-empty"); Finish(empty); }
            File.WriteAllText(Path.Combine(output, "native-capture.json"), JsonSerializer.Serialize(new
            {
                os = RuntimeInformation.OSDescription, runtime = RuntimeInformation.FrameworkDescription,
                method = "Native Windows Forms DrawToBitmap on standard GitHub Windows runner; synthetic fixtures; no VPN started",
                dpapiReload = "passed", protectedSessionAcl = "passed", ownedChildValidationAndCleanup = "passed", executableShortcut = "passed", nativeRuleEditing = "passed", maskedImportDialog = "passed", captures
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS native Windows Forms capture and isolated DPAPI round trip"); return 0;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }

        void Snap(Form window, string name)
        {
            window.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(window.Width, window.Height); window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var distinct = new HashSet<int>(); for (var y = 0; y < bitmap.Height; y += 11) for (var x = 0; x < bitmap.Width; x += 11) distinct.Add(bitmap.GetPixel(x, y).ToArgb());
            if (distinct.Count < 20) throw new Exception("Blank capture " + name);
            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            captures.Add(new { name, bitmap.Width, bitmap.Height, sampledColors = distinct.Count, dpi = window.DeviceDpi });
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, int wParam, int lParam);
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Finish(MainWindow window)
    { window.Close(); var limit = DateTime.UtcNow.AddSeconds(10); while (!window.IsDisposed && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(10); } if (!window.IsDisposed) throw new Exception("Window failed to close"); }
    private static void Wait(Task task)
    { var limit = DateTime.UtcNow.AddSeconds(25); while (!task.IsCompleted && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(10); } if (!task.IsCompleted) throw new Exception("Native check timed out"); task.GetAwaiter().GetResult(); }
}
