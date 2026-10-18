using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using BorisRay.Core;

namespace BorisRay.Windows;

internal sealed class MainWindow : Form
{
    private static readonly Color Ground = Color.FromArgb(16, 16, 20), PanelColor = Color.FromArgb(25, 25, 30), Input = Color.FromArgb(32, 32, 36), Gold = Color.FromArgb(241, 194, 125), TextColor = Color.FromArgb(244, 241, 236), Secondary = Color.FromArgb(183, 183, 192), Error = Color.FromArgb(255, 158, 158);
    private readonly PrivateStore store;
    private readonly CoreProcess engine;
    private readonly bool captureOnly;
    private readonly Panel content = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ground };
    private readonly Label state = new() { AutoSize = true, ForeColor = Secondary, Text = "VPN выключен", MaximumSize = new Size(205, 0) };
    private readonly Label banner = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(18, 12, 18, 12), BackColor = Color.FromArgb(50, 28, 31), ForeColor = Error, Visible = false };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly List<Button> navigation = [];
    private readonly HashSet<Task> operations = [];
    private readonly Dictionary<Control, int> preferredWidths = [];
    private bool adapting;
    private string page = "Подключение";
    private bool busy, refreshing, dirty, closing, closed;
    private Label? statusLabel, pendingLabel, trafficLabel, speedLabel, connectionsStatus;
    private Button? connect, apply, import, refresh, speed;
    private DataGridView? connections;
    private CancellationTokenSource? measurement;
    private JsonObject? snapshot;
    private string speedText = "Пинг: — мс   ·   загрузка: —   ·   отдача: —";
    private readonly Image logo;
    public MainWindow(PrivateStore? isolatedStore = null, bool captureOnly = false)
    {
        store = isolatedStore ?? new PrivateStore();
        this.captureOnly = captureOnly;
        Text = captureOnly ? "BorisRay — тестовые данные · VPN отключён" : "BorisRay"; ClientSize = new Size(1100, 780); MinimumSize = new Size(930, 640);
        BackColor = Ground; ForeColor = TextColor; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); StartPosition = FormStartPosition.CenterScreen;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BorisRay.Icon.png")!;
        using var original = Image.FromStream(stream); logo = new Bitmap(original);
        engine = new CoreProcess(store.Root, captureOnly); engine.Exited += () => { if (!IsDisposed && IsHandleCreated && !closing) BeginInvoke(() => { if (!busy) UpdateState(); }); };
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 232, Padding = new Padding(16, 22, 16, 18), BackColor = PanelColor };
        var brand = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, WrapContents = false, BackColor = PanelColor };
        brand.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Width = 48, Height = 48, Margin = new Padding(0, 0, 7, 0) });
        brand.Controls.Add(new Label { Text = "BorisRay", AutoSize = true, Font = new Font("Georgia", 20, FontStyle.Bold), ForeColor = Gold, Padding = new Padding(0, 4, 0, 0) });
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
        foreach (var name in new[] { "Подключение", "Приложения", "Сайты", "Соединения", "О приложении" })
        { var button = Button(name, () => ShowPage(name)); button.Width = 198; button.Height = 44; button.TextAlign = ContentAlignment.MiddleLeft; ((ActionButton)button).Borderless = true; navigation.Add(button); nav.Controls.Add(button); }
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 78, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2, 8, 0, 0) }; footer.Controls.Add(state); footer.Controls.Add(new Label { Text = "0.1.0 · Бесплатный клиент", AutoSize = true, ForeColor = Secondary, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 8, 0, 0) });
        sidebar.Controls.Add(nav); sidebar.Controls.Add(brand); sidebar.Controls.Add(footer);
        var center = new Panel { Dock = DockStyle.Fill }; center.Controls.Add(content); center.Controls.Add(banner);
        Controls.Add(center); Controls.Add(sidebar);
        content.SizeChanged += (_, _) => AdaptContent();
        content.Layout += (_, _) => AdaptContent(); SizeChanged += (_, _) => AdaptContent();
        timer.Tick += async (_, _) => await PollAsync(); timer.Start();
        FormClosing += ClosingAsync; Shown += (_, _) => { var dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); };
        ShowPage(page);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int length);
    private static Button Button(string text, Action action, bool primary = false)
    {
        var button = new ActionButton { Text = text, AutoSize = true, MinimumSize = new Size(0, 38), FlatStyle = FlatStyle.Flat, ForeColor = primary ? Ground : TextColor, BackColor = primary ? Gold : Input, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 10, 8), AccessibleName = text };
        button.FlatAppearance.BorderSize = primary ? 0 : 1; button.FlatAppearance.BorderColor = Color.FromArgb(75, 75, 82); button.Click += (_, _) => action(); return button;
    }
    private static Label Label(string text, float size = 10, bool strong = false, bool secondary = false) => new()
    { Text = text, AutoSize = true, MaximumSize = new Size(750, 0), Font = new Font("Segoe UI", size, strong ? FontStyle.Bold : FontStyle.Regular), ForeColor = secondary ? Secondary : TextColor, Margin = new Padding(0, 0, 0, 12) };
    private FlowLayoutPanel Screen(string title)
    {
        foreach (Control old in content.Controls.Cast<Control>().ToArray()) old.Dispose();
        content.Controls.Clear(); preferredWidths.Clear(); connections = null; statusLabel = pendingLabel = trafficLabel = speedLabel = connectionsStatus = null;
        connect = apply = import = refresh = speed = null;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28, 24, 24, 24) };
        panel.Controls.Add(Label(title, 23, true)); content.Controls.Add(panel); return panel;
    }
    private static FlowLayoutPanel Row(params Control[] controls)
    { var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, MaximumSize = new Size(780, 0), Margin = new Padding(0, 0, 0, 8) }; row.Controls.AddRange(controls); return row; }
    private static ComboBox Combo(IEnumerable<string> values, string selected, Action<string> changed, int width = 300)
    {
        var box = new ComboBox { FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 27, DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Input, ForeColor = TextColor, Margin = new Padding(0, 0, 14, 10), AccessibleName = "Выбор: " + selected };
        box.DrawItem += (_, e) => { if (e.Index < 0 || e.Graphics == null) return; var chosen = (e.State & DrawItemState.Selected) != 0; using var brush = new SolidBrush(chosen ? Color.FromArgb(65, 52, 38) : Input); e.Graphics.FillRectangle(brush, e.Bounds); TextRenderer.DrawText(e.Graphics, box.Items[e.Index]?.ToString() ?? "", box.Font, new Rectangle(e.Bounds.X + 9, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height), TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); e.DrawFocusRectangle(); };
        foreach (var value in values) box.Items.Add(value); box.SelectedItem = selected;
        box.SelectedIndexChanged += (_, _) => { if (box.SelectedItem is string value) changed(value); }; return box;
    }
    internal void ShowPage(string name)
    {
        page = name; foreach (var button in navigation) { button.BackColor = button.Text == name ? Color.FromArgb(65, 52, 38) : PanelColor; button.ForeColor = button.Text == name ? Gold : Secondary; }
        switch (name) { case "Подключение": ConnectionPage(); break; case "Приложения": AppsPage(); break; case "Сайты": SitesPage(); break; case "Соединения": ConnectionsPage(); break; default: AboutPage(); break; }
        AdaptContent(); UpdateState();
    }
    private void AdaptContent()
    {
        if (adapting || content.Controls.Count == 0 || content.Controls[0] is not FlowLayoutPanel panel) return;
        adapting = true;
        try
        {
            panel.SuspendLayout();
            var viewport = Math.Min(content.ClientSize.Width, Math.Max(240, Width - 232 - SystemInformation.FrameBorderSize.Width * 2 - 16));
            var available = Math.Max(240, viewport - panel.Padding.Horizontal - 8);
            void Fit(Control child, int width)
            {
                if (child is Label) child.MaximumSize = new Size(width, 0);
                else if (child is FlowLayoutPanel flow)
                {
                    flow.MaximumSize = new Size(width, 0);
                    if (flow.FlowDirection == FlowDirection.LeftToRight) flow.Width = width;
                    foreach (Control nested in flow.Controls) Fit(nested, width);
                }
                else if (child is DataGridView || child is TextBox { Multiline: true }) child.Width = width;
                else if (child is ComboBox) { preferredWidths.TryAdd(child, child.Width); child.Width = Math.Min(preferredWidths[child], width); }
            }
            foreach (Control child in panel.Controls) Fit(child, available);
            panel.ResumeLayout(true);
        }
        finally { adapting = false; }
    }
    private sealed class ActionButton : Button
    {
        public bool Borderless;
        public bool Power;
        private bool hover, pressed;
        public ActionButton() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); UseVisualStyleBackColor = false; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Ground); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var color = Enabled ? BackColor : Input;
            if (Enabled && hover) color = ControlPaint.Light(color, .08f);
            if (Enabled && pressed) color = ControlPaint.Dark(color, .1f);
            using var path = new GraphicsPath(); var r = new RectangleF(.5f, .5f, Width - 1, Height - 1); const float d = 16;
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
            using var fill = new SolidBrush(color); e.Graphics.FillPath(fill, path);
            if (!Borderless && BackColor != Gold) { using var edge = new Pen(Color.FromArgb(65, 65, 73)); e.Graphics.DrawPath(edge, path); }
            var ink = Enabled ? ForeColor : Secondary;
            var bounds = ClientRectangle; if (Power) { bounds.X += 22; bounds.Width -= 22; using var pen = new Pen(ink, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round }; var cy = Height / 2f; e.Graphics.DrawArc(pen, 18, cy - 8, 16, 16, -48, 276); e.Graphics.DrawLine(pen, 26, cy - 10, 26, cy - 1); }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
            if (TextAlign == ContentAlignment.MiddleLeft) { bounds.X += 16; bounds.Width -= 24; }
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ink, flags);
            if (Focused && ShowFocusCues) { using var focus = new Pen(Gold, 2) { DashStyle = DashStyle.Dot }; e.Graphics.DrawRectangle(focus, 4, 4, Width - 9, Height - 9); }
        }
    }
    private static FlowLayoutPanel Field(string title, Control input)
    {
        var field = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 16, 0) };
        var label = Label(title, strong: true); label.Margin = new Padding(0, 0, 0, 6); field.Controls.Add(label); field.Controls.Add(input); return field;
    }
    private void Changed()
    { store.SaveSettings(); dirty = true; UpdateState(); }
    private void ConnectionPage()
    {
        var panel = Screen("Подключение");
        connect = Button("Подключить VPN", () => _ = RunAsync(ConnectOrStopAsync), true); connect.MinimumSize = new Size(208, 54); connect.Font = new Font(Font, FontStyle.Bold); ((ActionButton)connect).Power = true;
        statusLabel = Label("VPN выключен", 18, true); statusLabel.Padding = new Padding(8, 9, 0, 0);
        apply = Button("Применить маршруты", () => _ = RunAsync(async () => { await engine.StopAsync(); await StartAsync(); }));
        panel.Controls.Add(Row(connect, statusLabel, apply));
        pendingLabel = Label("", secondary: true); panel.Controls.Add(pendingLabel);
        panel.Controls.Add(Label(store.Nodes.Count == 0 ? "Добавь свою подписку" : $"Профилей в подписке: {store.Nodes.Count}", 14, true));
        import = Button(store.Nodes.Count == 0 ? "Добавить по ссылке…" : "Заменить подписку…", ImportProfile, store.Nodes.Count == 0);
        refresh = Button("Обновить", () => _ = RunAsync(RefreshProfileAsync));
        panel.Controls.Add(Row(import, Button("Из файла…", ImportFile), refresh));
        if (store.Nodes.Count == 0) panel.Controls.Add(Label("Ссылка от твоего VPN-провайдера, VLESS Reality или Hysteria2. Клиент бесплатный; серверы и подписку выбираешь сам.", secondary: true));
        var countries = new Dictionary<string, string> { ["auto"] = "Все страны" };
        foreach (var code in store.Nodes.Select(n => n.Country).Where(c => c != "auto").Distinct().OrderBy(Profiles.CountryName)) countries[code] = Profiles.CountryName(code);
        var countryValues = countries.Values.ToList();
        var country = Combo(countryValues, countries.GetValueOrDefault(store.Settings.Country, "Все страны"), value => { store.Settings.Country = countries.First(x => x.Value == value).Key; store.Settings.Server = "auto"; Changed(); ShowPage(page); }, 270);
        var channel = Combo(["Авто · TCP + UDP", "TCP · VLESS Reality", "UDP · Hysteria2"], store.Settings.Channel switch { "tcp" => "TCP · VLESS Reality", "udp" => "UDP · Hysteria2", _ => "Авто · TCP + UDP" }, value => { store.Settings.Channel = value.StartsWith("TCP", StringComparison.Ordinal) ? "tcp" : value.StartsWith("UDP", StringComparison.Ordinal) ? "udp" : "all"; store.Settings.Server = "auto"; Changed(); ShowPage(page); }, 270);
        panel.Controls.Add(Row(Field("Страна", country), Field("Канал", channel)));
        var nodes = Configuration.Selected(store.Nodes, store.Settings);
        if (store.Nodes.Count > 0 && nodes.Count == 0) panel.Controls.Add(Label("В этой стране нет профиля выбранного канала. Выбери другой канал или «Все страны».", secondary: true));
        var choices = new[] { "Автовыбор · по доступности и задержке" }.Concat(nodes.Select(n => n.Tag)).ToList();
        panel.Controls.Add(Field("Сервер", Combo(choices, choices.Contains(store.Settings.Server, StringComparer.Ordinal) ? store.Settings.Server : choices[0], value => { store.Settings.Server = value == choices[0] ? "auto" : value; Changed(); }, 590)));
        panel.Controls.Add(Field("Остальной трафик", Combo(["Через VPN", "Напрямую"], store.Settings.DefaultVpn ? "Через VPN" : "Напрямую", value => { store.Settings.DefaultVpn = value == "Через VPN"; Changed(); }, 270)));
        panel.Controls.Add(Row(Button($"Приложения · {store.Settings.Apps.Count}", () => ShowPage("Приложения")), Button($"Сайты · {store.Settings.Sites.Count}", () => ShowPage("Сайты"))));
        panel.Controls.Add(Label("Правила сайтов имеют приоритет, затем применяются правила приложений и общий маршрут.", secondary: true));
        speed = Button("Измерить", Measure); var speedHeading = Label("Скорость и пинг", 14, true); speedHeading.Padding = new Padding(0, 8, 12, 0); panel.Controls.Add(Row(speedHeading, speed));
        speedLabel = Label(speedText); panel.Controls.Add(speedLabel);
        trafficLabel = Label("Получено: 0 Б · отправлено: 0 Б", secondary: true); panel.Controls.Add(trafficLabel);
        panel.Controls.Add(Label("Замер через выбранный сервер: 5 МиБ загрузки, 1 МиБ отдачи. Пинг измеряется HTTPS-запросами.", secondary: true));
    }
    private void ImportFile()
    {
        using var picker = new OpenFileDialog { Title = "Список профилей", Filter = "Текстовые подписки|*.txt;*.conf|Все файлы|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _ = RunAsync(async () => { if (new FileInfo(picker.FileName).Length > Profiles.MaximumBytes) throw new ArgumentException("Файл больше 1 МиБ."); await ImportAsync(await File.ReadAllTextAsync(picker.FileName)); ShowPage("Подключение"); });
    }
    private async Task ConnectOrStopAsync()
    { if (engine.Running) await engine.StopAsync(); else await StartAsync(); }
    private async Task StartAsync()
    {
        foreach (var app in store.Settings.Apps) if (!File.Exists(app.Path)) throw new ArgumentException($"{Path.GetFileName(app.Path)} не найден. Удали его правило в «Приложениях» или выбери установленный .exe.");
        await engine.ValidateAsync(store.Nodes, store.Settings); await engine.StartAsync(store.Nodes, store.Settings); dirty = false;
    }
    private async Task RefreshProfileAsync()
    { if (string.IsNullOrWhiteSpace(store.Profile?.Source)) throw new ArgumentException("Добавь HTTPS-ссылку подписки для обновления."); await ImportAsync(store.Profile.Source); }
    private void ImportProfile()
    {
        using var dialog = new Form { Text = "Импорт подписки", Size = new Size(620, 260), StartPosition = FormStartPosition.CenterParent, BackColor = Ground, ForeColor = TextColor, Font = Font, MinimizeBox = false, MaximizeBox = false };
        var explanation = Label("HTTPS-ссылка от твоего провайдера или один VLESS Reality / Hysteria2-профиль. Новая подписка заменит текущую; правила сохранятся."); explanation.MaximumSize = new Size(dialog.ClientSize.Width - 16, 0); explanation.Dock = DockStyle.Top; explanation.Padding = new Padding(16);
        var input = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, MaxLength = Profiles.MaximumBytes, BackColor = Input, ForeColor = TextColor, AccessibleName = "Приватная ссылка подписки" };
        var show = new CheckBox { Text = "Показать ссылку", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 8, 0, 8) }; show.CheckedChanged += (_, _) => input.UseSystemPasswordChar = !show.Checked;
        var buttons = Row(Button("Добавить", () => { dialog.DialogResult = DialogResult.OK; dialog.Close(); }, true), Button("Отмена", () => dialog.Close())); buttons.Dock = DockStyle.Bottom;
        dialog.Controls.Add(show); dialog.Controls.Add(input); dialog.Controls.Add(explanation); dialog.Controls.Add(buttons); dialog.AcceptButton = (IButtonControl)buttons.Controls[0]; dialog.CancelButton = (IButtonControl)buttons.Controls[1];
        if (dialog.ShowDialog(this) == DialogResult.OK) { var value = input.Text; _ = RunAsync(async () => { await ImportAsync(value); ShowPage("Подключение"); }); }
    }
    private async Task ImportAsync(string input)
    {
        var raw = await Transfers.FetchSubscriptionAsync(input); var nodes = Profiles.Parse(raw);
        var settings = new RoutingSettings { Channel = nodes.Any(n => n.Channel == store.Settings.Channel) ? store.Settings.Channel : "all", Sites = store.Settings.Sites, Apps = store.Settings.Apps, DefaultVpn = store.Settings.DefaultVpn };
        await engine.ValidateAsync(nodes, settings);
        store.SaveProfile(new DeviceProfile(raw, input.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Profiles.SubscriptionUri(input).AbsoluteUri : ""), nodes); dirty = true;
    }
    private DataGridView Grid(params string[] headings)
    {
        var grid = new DataGridView { Width = 780, Height = 400, BackgroundColor = Ground, BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EnableHeadersVisualStyles = false, ForeColor = TextColor, GridColor = Color.FromArgb(60, 60, 67), AccessibleName = page };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Ground, ForeColor = TextColor, SelectionBackColor = Color.FromArgb(65, 52, 38), SelectionForeColor = TextColor, Padding = new Padding(4), Font = Font };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = PanelColor, ForeColor = TextColor, Font = new Font(Font, FontStyle.Bold) };
        grid.RowTemplate.Height = 40; grid.ColumnHeadersHeight = 36; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        foreach (var heading in headings) { var index = grid.Columns.Add(heading, heading); grid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable; }
        grid.DataError += (_, e) => { e.ThrowException = false; };
        return grid;
    }
    private void AppsPage()
    {
        var panel = Screen("Приложения"); panel.Controls.Add(Label("Выбери маршрут для программы. Сайты имеют приоритет; остальные приложения используют общий маршрут.", secondary: true));
        panel.Controls.Add(Row(Button("Добавить приложение…", AddApplication, true), Button("Из запущенных…", PickRunningApplication)));
        var grid = Grid("Приложение", "Путь", "Маршрут", "Папка"); grid.Height = 300; grid.Columns[0].ReadOnly = grid.Columns[1].ReadOnly = true;
        grid.Columns.RemoveAt(3); grid.Columns.Insert(3, new DataGridViewCheckBoxColumn { HeaderText = "Подпапки", Name = "Папка", FlatStyle = FlatStyle.Flat, FillWeight = 55 });
        grid.Columns.RemoveAt(2); grid.Columns.Insert(2, new DataGridViewComboBoxColumn { HeaderText = "Маршрут", Name = "Маршрут", DataSource = new[] { "Через VPN", "Напрямую" }, FlatStyle = FlatStyle.Flat, FillWeight = 85 });
        grid.Columns[0].FillWeight = 90; grid.Columns[0].MinimumWidth = 90; grid.Columns[1].FillWeight = 170; grid.Columns[1].MinimumWidth = 150; grid.Columns[2].MinimumWidth = 115; grid.Columns[3].FillWeight = 85; grid.Columns[3].MinimumWidth = 95;
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (var rule in store.Settings.Apps) { var i = grid.Rows.Add(Path.GetFileName(rule.Path), rule.Path, rule.Vpn ? "Через VPN" : "Напрямую", rule.IncludeFolder); grid.Rows[i].Tag = rule; }
        var pathLabel = new TextBox { Multiline = true, ReadOnly = true, Width = 780, Height = 54, BackColor = Input, ForeColor = TextColor, ScrollBars = ScrollBars.Vertical, AccessibleName = "Полный путь выбранного приложения" };
        void Selection() { pathLabel.Text = grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is AppRule rule ? rule.Path : "Выбери приложение, чтобы увидеть полный путь."; }
        grid.SelectionChanged += (_, _) => Selection();
        var changing = false;
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) =>
        {
            if (changing || e.RowIndex < 0 || e.ColumnIndex is not (2 or 3) || grid.Rows[e.RowIndex].Tag is not AppRule rule) return;
            var row = grid.Rows[e.RowIndex]; var next = rule with { Vpn = row.Cells[2].Value?.ToString() == "Через VPN", IncludeFolder = row.Cells[3].Value is true };
            try { Configuration.AppPattern(next); var index = store.Settings.Apps.IndexOf(rule); store.Settings.Apps[index] = next; row.Tag = next; Changed(); Selection(); }
            catch (ArgumentException error) { changing = true; row.Cells[3].Value = rule.IncludeFolder; changing = false; ShowError(error.Message); }
        };
        panel.Controls.Add(grid); panel.Controls.Add(pathLabel); Selection();
        var remove = Button("Удалить правило", () => { if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is AppRule rule) { store.Settings.Apps.Remove(rule); Changed(); ShowPage("Приложения"); } }); remove.Enabled = store.Settings.Apps.Count > 0; panel.Controls.Add(remove);
        panel.Controls.Add(Label(store.Settings.Apps.Count == 0 ? "Здесь появятся твои правила. Добавь .exe или ярлык — BorisRay найдёт настоящую программу." : "«Подпапки» включает все .exe в папке приложения и ниже. Отдельное правило точного .exe имеет приоритет.", secondary: true));
    }
    private void AddApplication()
    {
        using var picker = new OpenFileDialog { Title = "Приложение или ярлык", Filter = "Приложения и ярлыки|*.exe;*.lnk;*.url|Все файлы|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) == DialogResult.OK) AddAppPath(picker.FileName);
    }
    internal void AddAppPath(string input)
    {
        try
        {
            var path = WindowsPlatform.ResolveExecutable(input);
            using var dialog = new Form { Text = "Маршрут приложения", Size = new Size(620, 270), StartPosition = FormStartPosition.CenterParent, BackColor = Ground, ForeColor = TextColor, Font = Font, MinimizeBox = false, MaximizeBox = false };
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(18) };
            body.Controls.Add(Label(Path.GetFileName(path), 13, true)); var fullPath = Label(path, secondary: true); fullPath.MaximumSize = new Size(dialog.ClientSize.Width - 36, 0); body.Controls.Add(fullPath);
            var folder = new CheckBox { Text = "Также .exe в этой папке и её подпапках", AutoSize = true, Margin = new Padding(0, 0, 0, 12) }; body.Controls.Add(folder);
            bool vpn = false; body.Controls.Add(Row(Button("Напрямую", () => { dialog.DialogResult = DialogResult.OK; dialog.Close(); }, true), Button("Через VPN", () => { vpn = true; dialog.DialogResult = DialogResult.OK; dialog.Close(); }), Button("Отмена", () => dialog.Close()))); dialog.Controls.Add(body);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var rule = new AppRule(path, vpn, folder.Checked); Configuration.AppPattern(rule);
            store.Settings.Apps.RemoveAll(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase)); store.Settings.Apps.Add(rule); Changed(); ShowPage("Приложения");
        }
        catch (Exception e) { ShowError(e.Message); }
    }
    private void PickRunningApplication()
    {
        using var dialog = new Form { Text = "Запущенные приложения", Size = new Size(760, 520), StartPosition = FormStartPosition.CenterParent, BackColor = Ground, ForeColor = TextColor, Font = Font };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = Input, ForeColor = TextColor, HorizontalScrollbar = true };
        var paths = new List<string>();
        foreach (var process in Process.GetProcesses()) using (process) { try { var path = process.MainModule?.FileName; if (path != null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path); } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { } }
        foreach (var path in paths.OrderBy(Path.GetFileName)) list.Items.Add(path);
        var pick = Button("Выбрать .exe", () => { if (list.SelectedItem != null) { dialog.DialogResult = DialogResult.OK; dialog.Close(); } }); pick.Enabled = false; list.SelectedIndexChanged += (_, _) => pick.Enabled = list.SelectedItem != null; pick.Dock = DockStyle.Bottom; dialog.AcceptButton = pick; dialog.Controls.Add(list); dialog.Controls.Add(pick);
        if (dialog.ShowDialog(this) == DialogResult.OK && list.SelectedItem is string selected) AddAppPath(selected);
    }
    private void SitesPage()
    {
        var panel = Screen("Сайты"); panel.Controls.Add(Label("Домен и все его поддомены идут по выбранному маршруту. Более точный домен имеет приоритет, затем — правила приложений.", secondary: true));
        var input = new TextBox { Width = 300, BackColor = Input, ForeColor = TextColor, PlaceholderText = "Домен или ссылка на сайт", AccessibleName = "Домен или ссылка на сайт", Margin = new Padding(0, 4, 12, 8) };
        var vpn = false; var route = Combo(["Напрямую", "Через VPN"], "Напрямую", value => vpn = value == "Через VPN", 150);
        var add = Button("Добавить", () => { try { var domain = input.Text.Trim(); if (Uri.TryCreate(domain, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") domain = uri.IdnHost; store.Settings.Sites[Profiles.Domain(domain)] = vpn; Changed(); ShowPage("Сайты"); } catch (ArgumentException e) { ShowError(e.Message); } }, true); add.Enabled = false; input.TextChanged += (_, _) => add.Enabled = !string.IsNullOrWhiteSpace(input.Text); panel.Controls.Add(Row(input, route, add));
        var grid = Grid("Домен", "Маршрут"); grid.Height = 300; grid.Columns[0].ReadOnly = true;
        grid.Columns.RemoveAt(1); grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Маршрут", Name = "Маршрут", DataSource = new[] { "Напрямую", "Через VPN" }, FlatStyle = FlatStyle.Flat });
        foreach (var rule in store.Settings.Sites.OrderBy(x => x.Key)) { var i = grid.Rows.Add(rule.Key, rule.Value ? "Через VPN" : "Напрямую"); grid.Rows[i].Tag = rule.Key; }
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) => { if (e.RowIndex < 0 || e.ColumnIndex != 1 || grid.Rows[e.RowIndex].Tag is not string domain) return; store.Settings.Sites[domain] = grid.Rows[e.RowIndex].Cells[1].Value?.ToString() == "Через VPN"; Changed(); };
        panel.Controls.Add(grid); var remove = Button("Удалить правило", () => { if (grid.SelectedRows.Count > 0) { store.Settings.Sites.Remove(grid.SelectedRows[0].Cells[0].Value!.ToString()!); Changed(); ShowPage("Сайты"); } }); remove.Enabled = store.Settings.Sites.Count > 0; panel.Controls.Add(remove);
        if (store.Settings.Sites.Count == 0) panel.Controls.Add(Label("Отдельных правил пока нет. Все сайты используют общий маршрут.", secondary: true));
    }
    private void ConnectionsPage()
    {
        var panel = Screen("Соединения"); connectionsStatus = Label(engine.Running ? "Получаю соединения…" : "VPN выключен. Активных соединений нет.", secondary: true); panel.Controls.Add(connectionsStatus);
        connections = Grid("Процесс", "Сайт / IP", "Сеть", "Маршрут", "Получено", "Отправлено"); connections.ReadOnly = true; connections.Height = 300;
        var weights = new[] { 80f, 125f, 55f, 90f, 90f, 90f }; var minimums = new[] { 80, 90, 50, 75, 90, 90 };
        for (var index = 0; index < 6; index++) { connections.Columns[index].FillWeight = weights[index]; connections.Columns[index].MinimumWidth = minimums[index]; }
        panel.Controls.Add(connections);
        var detail = new TextBox { Multiline = true, ReadOnly = true, Width = 780, Height = 100, BackColor = Input, ForeColor = TextColor, ScrollBars = ScrollBars.Vertical, AccessibleName = "Подробности выбранного соединения" };
        connections.SelectionChanged += (_, _) => { if (connections.SelectedRows.Count > 0 && connections.SelectedRows[0].Tag is JsonObject row) detail.Text = Details(row); }; panel.Controls.Add(detail);
        panel.Controls.Add(Label("Обновляется раз в две секунды. Очень короткие соединения могут не попасть в таблицу. «Напрямую» означает выход через провайдера; правило выполняется внутри локального туннеля.", secondary: true)); RenderConnections();
    }
    private static string Value(JsonNode? node, string fallback = "—") => node?.ToString() is string text && text.Length > 0 ? text : fallback;
    private static string Route(JsonObject row)
    { var chain = row["chains"] as JsonArray; if (chain == null || chain.Count == 0) return "Не определён"; return string.Join(" → ", chain.Select(c => Value(c) switch { "direct" => "Напрямую", "vpn" => "VPN", "auto" => "Автовыбор", var v => v })); }
    private static string Details(JsonObject row)
    { var m = row["metadata"]; return $"Процесс: {Value(m?["processPath"], Value(m?["process"]))}\r\nАдрес: {Value(m?["host"], Value(m?["destinationIP"]))}:{Value(m?["destinationPort"])} · {Value(m?["network"])}\r\nПравило: {Value(row["rule"])} {Value(row["rulePayload"], "")}\r\nЦепочка: {Route(row)}"; }
    private void RenderConnections()
    {
        if (connections == null) return;
        var selected = connections.SelectedRows.Count > 0 && connections.SelectedRows[0].Tag is JsonObject current ? Value(current["id"], "") : "";
        connections.Rows.Clear();
        var rows = snapshot?["connections"] as JsonArray;
        if (!engine.Running || rows == null) return;
        foreach (var item in rows.Take(1000).OfType<JsonObject>())
        {
            var m = item["metadata"]; var i = connections.Rows.Add(Value(m?["process"], Path.GetFileName(Value(m?["processPath"], "Не определён"))), Value(m?["host"], Value(m?["destinationIP"])), Value(m?["network"]).ToUpperInvariant(), Route(item), Bytes(item["download"]?.GetValue<long>() ?? 0), Bytes(item["upload"]?.GetValue<long>() ?? 0)); connections.Rows[i].Tag = item;
            if (selected.Length > 0 && Value(item["id"], "") == selected) connections.CurrentCell = connections.Rows[i].Cells[0];
        }
        if (connectionsStatus != null) connectionsStatus.Text = rows.Count == 0 ? "Туннель запущен. Сейчас нет активных соединений." : $"Активных: {rows.Count}" + (rows.Count > 1000 ? " · показаны первые 1000" : "");
    }
    private void AboutPage()
    {
        var panel = Screen("О приложении"); panel.Controls.Add(Label("BorisRay 0.1.0 · Windows x64", 16, true));
        panel.Controls.Add(Label("Бесплатный клиент для твоих VPN-подписок", 14, true));
        panel.Controls.Add(Label("Подключай своих провайдеров: HTTPS-подписки, списки ссылок и текстовые файлы с VLESS Reality / Hysteria2. Другие протоколы, JSON и Clash YAML пока не поддерживаются.", secondary: true));
        panel.Controls.Add(Label("Подписка хранится на этом компьютере и шифруется средствами Windows. Клиент не содержит готовых доступов к VPN-сервису.", secondary: true));
        panel.Controls.Add(Label("Обновления BorisRay", 14, true));
        var updateStatus = Label("Проверка доступна вручную. Новая версия устанавливается распаковкой архива после закрытия приложения.", secondary: true); panel.Controls.Add(updateStatus);
        panel.Controls.Add(Button("Проверить обновления", () => _ = RunAsync(async () => { var update = await Transfers.UpdateAsync(); if (update.Version <= new Version(0, 1, 0)) updateStatus.Text = "Установлена последняя версия"; else { updateStatus.Text = "Доступна версия " + update.Version; panel.Controls.Add(Button("Скачать обновление", () => OpenUrl(update.Download.AbsoluteUri), true)); } })));
        panel.Controls.Add(Button("Исходники BorisRay", () => OpenUrl(Transfers.UpdatesBase + "BorisRay-Windows-0.1.0-source.zip")));
        panel.Controls.Add(Button("Лицензии и авторы", () => OpenFile(Path.Combine(AppContext.BaseDirectory, "LICENSES.txt"))));
        panel.Controls.Add(Button("Сохранить диагностику…", SaveDiagnostics));
        panel.Controls.Add(Label("Ядро sing-box 1.14.3, © nekohasekai и участники SagerNet, GPLv3+. Независимый клиент, без связи с авторами исходного проекта. Подпись издателя Windows для этой сборки не выпускалась.", secondary: true));
    }
    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private static void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void SaveDiagnostics()
    {
        using var picker = new SaveFileDialog { FileName = "BorisRay-diagnostics.txt", Filter = "Текст|*.txt" }; if (picker.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(picker.FileName, $"BorisRay-Windows0.1.0\r\nCore1.14.3\r\nRunning={engine.Running}\r\nProfiles={store.Nodes.Count}\r\nPhysicalIPv6={WindowsPlatform.HasPhysicalIPv6()}\r\n{engine.Journal()}");
    }
    private void Measure()
    {
        if (captureOnly) { ShowError("Замер отключён в режиме проверки интерфейса."); return; }
        if (measurement != null) { measurement.Cancel(); return; }
        measurement = new CancellationTokenSource(); var cancel = measurement; UpdateState();
        _ = RunAsync(async () => { try { await Transfers.MeasureAsync(store, new Progress<string>(text => { speedText = text; if (speedLabel != null) speedLabel.Text = text; }), cancel.Token); }
            catch (OperationCanceledException) { speedText = "Замер отменён"; }
            finally { measurement = null; cancel.Dispose(); if (speedLabel != null) speedLabel.Text = speedText; UpdateState(); } }, false);
    }
    private async Task RunAsync(Func<Task> operation, bool exclusive = true)
    {
        if (closing || (exclusive && busy)) return;
        if (exclusive) { busy = true; banner.Visible = false; UpdateState(); }
        Task? pending = null;
        try { pending = operation(); operations.Add(pending); await pending; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { ShowError(e.Message); }
        catch (OperationCanceledException) { ShowError("Операция не завершилась вовремя. Проверь интернет и повтори."); }
        catch (Exception e) when (e is HttpRequestException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { ShowError("Операция не завершилась. Проверь интернет, доступ к файлам и повтори. Подробности можно сохранить в диагностике."); }
        finally { if (pending != null) operations.Remove(pending); if (exclusive) busy = false; UpdateState(); }
    }
    private void ShowError(string text) { banner.Text = text; banner.Visible = true; }
    private void UpdateState()
    {
        if (closing || IsDisposed) return;
        content.Enabled = !busy;
        var running = engine.Running; state.Text = busy ? "Выполняю действие…" : running ? "VPN запущен" : "VPN выключен"; state.ForeColor = running ? Gold : Secondary;
        if (statusLabel != null) statusLabel.Text = state.Text;
        if (connect != null) { connect.Text = running ? "Отключить VPN" : "Подключить VPN"; connect.Enabled = !busy && (running || Configuration.Selected(store.Nodes, store.Settings).Count > 0); }
        if (apply != null) { apply.Visible = running && dirty; apply.Enabled = !busy; }
        if (import != null) import.Enabled = !busy;
        if (refresh != null) refresh.Enabled = !busy && !string.IsNullOrWhiteSpace(store.Profile?.Source);
        if (speed != null) { speed.Text = measurement != null ? "Отменить замер" : "Измерить"; speed.Enabled = !busy && store.Nodes.Count > 0; }
        if (pendingLabel != null) { pendingLabel.Visible = dirty; pendingLabel.Text = dirty ? running ? "Правила сохранены. Нажми «Применить маршруты», чтобы переподключиться." : "Правила применятся при следующем подключении." : ""; }
    }
    private async Task PollAsync()
    {
        if (refreshing || busy || closing || !engine.Running) { UpdateState(); return; }
        refreshing = true;
        try
        {
            snapshot = await engine.ConnectionsAsync();
            if (trafficLabel != null) trafficLabel.Text = $"Получено: {Bytes(snapshot["downloadTotal"]?.GetValue<long>() ?? 0)} · отправлено: {Bytes(snapshot["uploadTotal"]?.GetValue<long>() ?? 0)}";
            if (WindowsPlatform.HasPhysicalIPv6() != engine.PhysicalIPv6 && !dirty) { dirty = true; UpdateState(); }
            RenderConnections();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        { if (connectionsStatus != null) connectionsStatus.Text = "Данные соединений временно недоступны. Проверь состояние VPN."; }
        finally { refreshing = false; UpdateState(); }
    }
    private static string Bytes(long bytes) => bytes < 1024 ? $"{bytes} Б" : bytes < 1_048_576 ? $"{bytes / 1024d:F1} КБ" : bytes < 1_073_741_824 ? $"{bytes / 1_048_576d:F1} МБ" : $"{bytes / 1_073_741_824d:F2} ГБ";
    private async void ClosingAsync(object? sender, FormClosingEventArgs e)
    {
        if (closed) return; e.Cancel = true; if (closing) return; closing = true; timer.Stop(); measurement?.Cancel(); state.Text = "Закрываю подключения…";
        try { try { await Task.WhenAll(operations.ToArray()); } catch { } await engine.DisposeAsync(); }
        finally { closed = true; logo.Dispose(); Close(); }
    }
}
