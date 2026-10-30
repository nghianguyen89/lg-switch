using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;

[assembly: AssemblyTitle("LG Switch")]
[assembly: AssemblyDescription("LG UP600 input selection and NUC display layout")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace LgSwitch
{
    internal static class Program
    {
        internal const string AppMutex = "Local\\Nghia.LgSwitch.Release101.App";
        internal const string ShowEvent = "Local\\Nghia.LgSwitch.Release101.Show";
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                bool trayOnly = args.Length == 1 && args[0] == "--tray";
                if (args.Length > 0)
                {
                    if (args[0] == "--ddc" && args.Length == 4)
                    {
                        string id = Encoding.UTF8.GetString(Convert.FromBase64String(args[1])); string result;
                        using (Core.DdcLock())
                        {
                            if (args[3] == "standard") result = Native.Ddc(id, args[2]);
                            else if (args[3] == "intel") result = args[2] == "intel-probe" ? IntelDdc.Diagnose(id) : args[2] == "intel-transport" ? IntelDdc.TransportDiagnostics(id) : IntelDdc.SetInput(id, args[2]);
                            else throw new ArgumentException("Unknown control method.");
                        }
                        using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))) output.WriteLine(result);
                        return 0;
                    }
                    if (args[0] == "--guard" && args.Length == 4)
                    { Guard.Run(args[1], Int32.Parse(args[2]), Int64.Parse(args[3])); return 0; }
                    if (args[0] == "--diagnose" && args.Length == 1)
                    { File.WriteAllText(Path.Combine(Files.Root, "diagnostics.txt"), Core.Diagnose(), Encoding.UTF8); return 0; }
                    if (args[0] == "--self-test" && args.Length == 1)
                    { File.WriteAllText(Path.Combine(Files.Root, "self-test.txt"), SelfTests.Run(), Encoding.UTF8); return 0; }
                    if (args[0] == "--render-preview" && args.Length == 1)
                    {
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                        using (var preview = new MainForm(false, true)) preview.RenderPreview(Path.Combine(Files.Root, "preview.png"));
                        return 0;
                    }
                    if (args[0] == "--watchdog-self-test" && args.Length == 1)
                    { File.WriteAllText(Path.Combine(Files.Root, "watchdog-self-test.txt"), SelfTests.Watchdog(), Encoding.UTF8); return 0; }
                    if (args.Length != 1 || (args[0] != "--tray" && args[0] != "--setup")) throw new ArgumentException("Use --setup, --tray, --diagnose, --self-test, or --watchdog-self-test.");
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var instance = new Mutex(false, AppMutex))
                {
                    bool acquired;
                    try { acquired = instance.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                    {
                        if (!trayOnly) using (var signal = EventWaitHandle.OpenExisting(ShowEvent)) signal.Set();
                        return 0;
                    }
                    try { using (var form = new MainForm(trayOnly)) Application.Run(form); }
                    finally { instance.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Files.Log("ERROR " + ex);
                if (args.Length > 0 && args[0] == "--ddc") Console.Error.WriteLine(ex.Message);
                else if (args.Length > 0 && (args[0] == "--guard" || args[0] == "--diagnose" || args[0].EndsWith("self-test")))
                    File.WriteAllText(Path.Combine(Files.Root, "last-error.txt"), ex.ToString(), Encoding.UTF8);
                else MessageBox.Show(ex.Message, "LG Switch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal sealed class MainForm : Form
    {
        readonly SourceButton dp = new SourceButton(), hdmi1 = new SourceButton(), hdmi2 = new SourceButton();
        readonly Label current = new Label(), status = new Label(), state = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly EventWaitHandle show;
        readonly System.Windows.Forms.Timer signals = new System.Windows.Forms.Timer();
        readonly ToolTip hints = new ToolTip();
        readonly Font bodyFont = UiTheme.Font(10), titleFont = UiTheme.Font(23, true), smallFont = UiTheme.Font(9);
        readonly bool startHidden, preview;
        Settings settings;
        bool busy, quitting;

        internal MainForm(bool startHidden, bool preview = false)
        {
            SuspendLayout();
            this.startHidden = startHidden; this.preview = preview;
            Text = "LG Switch 1.0.1"; Font = bodyFont;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = SystemInformation.HighContrast ? SystemColors.Control : UiTheme.Background;
            ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : UiTheme.Text;
            ClientSize = new Size(740, 440); FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 16), ColumnCount = 1, RowCount = 5, BackColor = BackColor };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 174));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            Controls.Add(panel);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = BackColor };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            var brand = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = BackColor };
            brand.Controls.Add(new Label { Text = "LG Switch", Font = titleFont, AutoSize = true, Margin = new Padding(0), ForeColor = ForeColor });
            brand.Controls.Add(new Label { Text = "LG 27UP600  /  NUC + ASUS", Font = smallFont, AutoSize = true, Margin = new Padding(1, 0, 0, 0), ForeColor = Muted });
            var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 5, 0, 0), BackColor = BackColor };
            AddQuiet(actions, "Tuỳ chọn", 92, delegate { Options(); });
            AddQuiet(actions, "Làm mới", 90, async delegate { await RefreshInput(); });
            header.Controls.Add(brand, 0, 0); header.Controls.Add(actions, 1, 0); panel.Controls.Add(header, 0, 0);
            panel.Controls.Add(new Label { Text = "Chọn nguồn cho màn hình LG", AutoSize = true, ForeColor = Muted, Margin = new Padding(2, 0, 0, 0) }, 0, 1);
            var cards = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = BackColor };
            for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            SetupButton(dp, "DisplayPort", "NUC · LG + ASUS", "dp");
            SetupButton(hdmi1, "HDMI-1", "Thiết bị ngoài · Chỉ ASUS", "hdmi1");
            SetupButton(hdmi2, "HDMI-2", "Thiết bị ngoài · Chỉ ASUS", "hdmi2");
            dp.Margin = new Padding(0, 0, 6, 0); hdmi1.Margin = new Padding(6, 0, 6, 0); hdmi2.Margin = new Padding(6, 0, 0, 0);
            cards.Controls.Add(dp, 0, 0); cards.Controls.Add(hdmi1, 1, 0); cards.Controls.Add(hdmi2, 2, 0); panel.Controls.Add(cards, 0, 2);
            var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(2, 13, 2, 3), BackColor = BackColor };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); info.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            current.Text = "Đang nhận diện màn hình…"; current.Dock = DockStyle.Fill; current.ForeColor = ForeColor; current.AutoEllipsis = true;
            status.Text = "Giữ ASUS để điều khiển NUC khi LG dùng nguồn HDMI."; status.Dock = DockStyle.Fill; status.Font = smallFont; status.ForeColor = Muted; status.AutoEllipsis = true;
            info.Controls.Add(current, 0, 0); info.Controls.Add(status, 0, 1); panel.Controls.Add(info, 0, 3);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = BackColor };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            state.Text = "●  Đang khởi động"; state.AutoSize = true; state.Dock = DockStyle.Fill; state.Font = smallFont; state.TextAlign = ContentAlignment.MiddleLeft; state.ForeColor = Muted;
            var footerActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), BackColor = BackColor };
            AddQuiet(footerActions, "Thoát", 65, delegate { if (!busy) { quitting = true; Close(); } });
            AddQuiet(footerActions, "Ẩn xuống khay", 132, delegate { Hide(); });
            footer.Controls.Add(state, 0, 0); footer.Controls.Add(footerActions, 1, 0); panel.Controls.Add(footer, 0, 4);
            if (!preview)
            {
                show = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEvent);
                var menu = new ContextMenuStrip();
                menu.Items.Add("DisplayPort · Về NUC / Extend", null, delegate { SelectInput("dp"); });
                menu.Items.Add("HDMI-1 · Chỉ ASUS", null, delegate { SelectInput("hdmi1"); });
                menu.Items.Add("HDMI-2 · Chỉ ASUS", null, delegate { SelectInput("hdmi2"); });
                menu.Items.Add("Mở bảng chọn", null, delegate { ShowPanel(); });
                menu.Items.Add("Thoát", null, delegate { if (!busy) { quitting = true; Close(); } });
                tray.Icon = Icon; tray.Text = "LG Switch"; tray.ContextMenuStrip = menu; tray.Visible = true;
                tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowPanel(); };
                signals.Interval = 250; signals.Tick += delegate { if (show.WaitOne(0)) ShowPanel(); }; signals.Start();
                Shown += delegate { Initialize(); };
            }
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!quitting && !preview) { e.Cancel = true; Hide(); } };
            ResumeLayout(false); PerformAutoScale(); PerformLayout();
        }
        Color Muted { get { return SystemInformation.HighContrast ? SystemColors.ControlText : UiTheme.Muted; } }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!SystemInformation.HighContrast)
            {
                int dark = 1, round = 2, caption = ColorTranslator.ToWin32(BackColor), text = ColorTranslator.ToWin32(ForeColor);
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(Handle, 33, ref round, 4);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref text, 4);
            }
        }
        void SetupButton(SourceButton button, string heading, string caption, string input)
        {
            button.Heading = heading; button.Caption = caption; button.InputKey = input; button.Dock = DockStyle.Fill;
            button.Click += delegate { SelectInput(input); }; button.Enabled = false;
            hints.SetToolTip(button, input == "dp" ? "Quay về NUC: bật Extend và chọn DisplayPort." : "Chọn " + heading + " trên LG và cho NUC chỉ dùng ASUS.");
        }
        void AddQuiet(FlowLayoutPanel panel, string text, int width, EventHandler click)
        {
            var button = new QuietButton { Text = text, Width = width, Height = 32, Font = smallFont, Margin = new Padding(2, 0, 0, 0) };
            button.Click += click; panel.Controls.Add(button);
        }
        void ShowPanel()
        {
            if (settings != null && !preview)
            {
                try
                {
                    foreach (var endpoint in Native.Endpoints(Native.Query()))
                        if (endpoint.Id == settings.AsusId)
                            foreach (var screen in Screen.AllScreens) if (screen.DeviceName == endpoint.Gdi)
                            {
                                var area = screen.WorkingArea;
                                Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
                            }
                }
                catch (Exception ex) { Files.Log("Position window: " + ex.Message); }
            }
            Show(); WindowState = FormWindowState.Normal; Activate();
        }
        void PaintInput(string actual)
        {
            string selected = actual ?? (settings == null ? null : settings.LastRequested);
            foreach (var button in new[] { dp, hdmi1, hdmi2 })
            {
                button.Selected = button.InputKey == selected;
                button.Observed = actual != null && button.InputKey == actual;
            }
            current.Text = actual != null ? "LG đang nhận " + Inputs.Label(actual) : selected != null ? "Yêu cầu gần nhất: " + Inputs.Label(selected) : "DisplayPort cho NUC · HDMI cho thiết bị ngoài";
        }
        async void Initialize()
        {
            bool ok = await Run(delegate { settings = Core.OpenSettings(); Core.ResolveProvider(settings); }, "Đang nhận diện LG và ASUS…");
            if (!ok) return;
            await RefreshInput();
            if (startHidden) Hide(); else ShowPanel();
        }
        async Task RefreshInput()
        {
            if (busy || preview) return;
            string actual = null; bool asusOnly = false;
            bool ok = await Run(delegate {
                if (settings == null) { settings = Core.OpenSettings(); Core.ResolveProvider(settings); }
                asusOnly = Core.IsLaptop(settings);
                try { actual = Core.ReadInput(settings); } catch (Exception ex) { Files.Log("Read current input: " + ex.Message); }
            }, "Đang cập nhật màn hình…");
            if (!ok) return;
            PaintInput(actual);
            status.Text = asusOnly ? "NUC đang dùng ASUS. Bấm DisplayPort để trở lại hai màn hình." : "NUC đang dùng LG + ASUS ở chế độ Extend.";
            if (actual == null) status.Text += " LG không trả trạng thái input.";
            hints.SetToolTip(status, status.Text);
        }
        async void SelectInput(string input)
        {
            if (busy || preview) return;
            if (settings == null) { status.Text = "Chọn Extend trong Windows rồi bấm Làm mới để nhận diện cặp màn."; return; }
            ShowPanel();
            bool ok = await Run(delegate { Core.Select(settings, input); }, "Đang chuyển sang " + Inputs.Label(input) + "…");
            if (ok) await RefreshInput(); else PaintInput(null);
        }
        void Options()
        {
            if (busy || preview) return;
            if (settings == null) { Details(); return; }
            using (var dialog = new OptionsDialog(settings.Provider))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.Provider != settings.Provider)
                {
                    settings.Provider = dialog.Provider; settings.LastRequested = null;
                    try { Files.Save(settings, Files.Config); status.Text = "Đã lưu cách điều khiển LG."; PaintInput(null); }
                    catch (Exception ex) { status.Text = ex.Message; }
                }
                if (dialog.Diagnostic) Details();
            }
        }
        async void Details()
        {
            if (preview) return;
            string text = null;
            bool ok = await Run(delegate { text = Core.Diagnose(); File.WriteAllText(Path.Combine(Files.Root, "diagnostics.txt"), text, Encoding.UTF8); }, "Đang đọc chẩn đoán…");
            if (!ok) return;
            using (var form = new Form { Text = "Chẩn đoán LG Switch", Size = new Size(800, 560), StartPosition = FormStartPosition.CenterParent, Font = bodyFont, BackColor = BackColor })
            using (var mono = new Font("Consolas", 9))
            {
                form.Controls.Add(new TextBox { Text = text, ReadOnly = true, Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = mono, BackColor = UiTheme.Surface, ForeColor = ForeColor, BorderStyle = BorderStyle.None });
                form.ShowDialog(this);
            }
        }
        async Task<bool> Run(Action action, string message)
        {
            if (busy) return false;
            bool completed = false;
            busy = true; UseWaitCursor = true; SetEnabled(false); status.Text = message; state.Text = "●  Đang thực hiện"; state.ForeColor = UiTheme.Accent;
            try { await Task.Run(action); completed = true; return true; }
            catch (Exception ex)
            {
                Files.Log("Operation failed: " + ex); ShowPanel();
                status.Text = ex.Message.Length > 220 ? ex.Message.Substring(0, 217) + "…" : ex.Message;
                hints.SetToolTip(status, ex.Message); return false;
            }
            finally
            {
                busy = false; UseWaitCursor = false; SetEnabled(settings != null);
                state.Text = completed ? "●  Sẵn sàng" : "●  Cần kiểm tra";
                state.ForeColor = completed ? Color.FromArgb(127, 212, 175) : UiTheme.Muted;
            }
        }
        void SetEnabled(bool enabled) { dp.Enabled = hdmi1.Enabled = hdmi2.Enabled = enabled; }
        internal void PreparePreview()
        {
            if (!preview) throw new InvalidOperationException("Preview requires an isolated form.");
            Text = "LG Switch 1.0.1 · UI preview";
            settings = new Settings { LastRequested = "dp", Provider = "intel" };
            PaintInput(null); SetEnabled(true); state.Text = "Xem trước giao diện";
            status.Text = "NUC dùng LG + ASUS · Extend. Dữ liệu minh hoạ, không điều khiển màn hình.";
        }
        internal void RenderPreview(string path)
        {
            PreparePreview();
            ShowInTaskbar = false; Opacity = 0; StartPosition = FormStartPosition.Manual; Location = new Point(-30000, -30000);
            Show(); PerformLayout();
            using (var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                Controls[0].DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
                bitmap.Save(path, ImageFormat.Png);
            }
            Hide();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                signals.Stop(); signals.Dispose(); tray.Visible = false; tray.Dispose(); hints.Dispose();
                if (show != null) show.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) { bodyFont.Dispose(); titleFont.Dispose(); smallFont.Dispose(); }
        }
    }

    internal sealed class OptionsDialog : Form
    {
        readonly ComboBox method = new ComboBox();
        readonly Font bodyFont = UiTheme.Font(10);
        internal string Provider { get { return method.SelectedIndex == 0 ? "intel" : "standard"; } }
        internal bool Diagnostic;
        internal OptionsDialog(string provider)
        {
            SuspendLayout();
            Text = "Tuỳ chọn LG Switch"; Font = bodyFont; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
            ClientSize = new Size(480, 230); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); Controls.Add(panel);
            panel.Controls.Add(new Label { Text = "Cách điều khiển LG", AutoSize = true }, 0, 0);
            method.DropDownStyle = ComboBoxStyle.DropDownList; method.Dock = DockStyle.Fill; method.FlatStyle = FlatStyle.Flat; method.BackColor = UiTheme.Surface; method.ForeColor = UiTheme.Text;
            method.Items.AddRange(new object[] { "LG qua driver Intel", "DDC/CI chuẩn" }); method.SelectedIndex = provider == "intel" ? 0 : 1; panel.Controls.Add(method, 0, 1);
            panel.Controls.Add(new Label { Text = "Chuyển input trực tiếp, không hỏi xác nhận hình ảnh.\r\nNếu cần quay lại NUC, bấm DisplayPort.\r\nỨng dụng yêu cầu quyền quản trị ngay khi mở.", AutoSize = true, ForeColor = UiTheme.Muted, Padding = new Padding(0, 14, 0, 0) }, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var save = new QuietButton { Text = "Lưu", DialogResult = DialogResult.OK, Width = 80, Height = 34 };
            var diagnostic = new QuietButton { Text = "Chẩn đoán", Width = 110, Height = 34 }; diagnostic.Click += delegate { Diagnostic = true; DialogResult = DialogResult.OK; Close(); };
            buttons.Controls.Add(save); buttons.Controls.Add(diagnostic); panel.Controls.Add(buttons, 0, 3); AcceptButton = save;
            ResumeLayout(false); PerformAutoScale(); PerformLayout();
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) bodyFont.Dispose(); }
    }
}
