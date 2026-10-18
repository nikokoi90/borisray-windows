namespace BorisRay.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var mutex = new Mutex(true, "Local\\BorisRay.Windows.Client", out var first);
        if (!first) { MessageBox.Show("BorisRay уже открыт. Проверь панель задач.", "BorisRay"); return; }
        try { Application.Run(new MainWindow()); }
        catch { MessageBox.Show("BorisRay не удалось запустить. Проверь, что архив полностью распакован в отдельную папку.", "BorisRay", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
