using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NetSim {
 public sealed class PolicyEditor : GroupBox {
  readonly NumericUpDown delay = Number(125, 60000), jitter = Number(4, 60000), loss = Number(0, 100), retry = Number(200, 60000);
  readonly ComboBox distribution = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
  public PolicyEditor(string title) {
   Text = title; Dock = DockStyle.Fill; Padding = new Padding(12); Height = 150;
   distribution.Items.AddRange(new object[] { "Gaussian", "Uniform", "Fixed" }); distribution.SelectedIndex = 0; loss.DecimalPlaces = 1; retry.Minimum = 1;
   TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
   table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
   Add(table, "Delay type", distribution, 0, 0); Add(table, "Delay (ms)", delay, 2, 0); Add(table, "Loss (%)", loss, 0, 1); Add(table, "Jitter (ms)", jitter, 2, 1); Add(table, "TCP retry (ms)", retry, 0, 2);
   Controls.Add(table);
  }
  static NumericUpDown Number(int value, int max) { return new NumericUpDown { Maximum = max, Value = value, Width = 100, ThousandsSeparator = true }; }
  static void Add(TableLayoutPanel t, string label, Control input, int col, int row) { t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, col, row); input.Anchor = AnchorStyles.Left | AnchorStyles.Right; t.Controls.Add(input, col + 1, row); }
  public Policy Read() { return new Policy { Delay = (int)delay.Value, Jitter = (int)jitter.Value, Loss = (double)loss.Value, Retry = (int)retry.Value, Distribution = distribution.Text }; }
 }
 public sealed class MainWindow : Form {
  readonly TextBox bind = new TextBox { Text = "127.0.0.1", Width = 110 }, target = new TextBox { Text = "127.0.0.1", Width = 170 };
  readonly NumericUpDown listenPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 8443, Width = 80 }, targetPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 443, Width = 80 };
  readonly ComboBox protocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 75 }, captureFormat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
  readonly CheckBox order = new CheckBox { Text = "Preserve UDP order", Checked = true, AutoSize = true }, capture = new CheckBox { Text = "Capture traffic", AutoSize = true };
  readonly PolicyEditor up = new PolicyEditor("Client → Target"), down = new PolicyEditor("Target → Client");
  readonly Button start = new Button { Text = "Start", Width = 100, Height = 34 }, stop = new Button { Text = "Stop", Width = 100, Height = 34, Enabled = false };
  readonly Label status = new Label { Text = "Stopped • Point your client at 127.0.0.1:8443", AutoSize = true, Margin = new Padding(15, 10, 0, 0) };
  readonly Label totals = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, note = new Label { Dock = DockStyle.Fill, AutoSize = true };
  readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
  readonly TextBox log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(245, 247, 250) };
  readonly TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
  readonly Timer timer = new Timer { Interval = 500 };
  readonly Stopwatch elapsed = new Stopwatch(); Engine engine; long previousBytes; double previousTime; bool busy;
  public MainWindow() {
   Text = "NetSim — Network Condition Simulator"; MinimumSize = new Size(1040, 760); Size = new Size(1180, 850); StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9F); BackColor = Color.White; AutoScaleMode = AutoScaleMode.Dpi;
   TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
   root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 292)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
   root.Controls.Add(new Label { Text = "Network Condition Simulator", Font = new Font("Segoe UI", 18F, FontStyle.Bold), AutoSize = true }, 0, 0); root.Controls.Add(settings, 0, 1);
   FlowLayoutPanel connection = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
   protocol.Items.AddRange(new object[] { "TCP", "UDP" }); protocol.SelectedIndex = 0;
   Add(connection, "Protocol", protocol); Add(connection, "Listen IP", bind); Add(connection, "Port", listenPort); Add(connection, "Target host", target); Add(connection, "Port", targetPort);
   settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 156)); settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); settings.Controls.Add(connection, 0, 0);
   TableLayoutPanel policies = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; policies.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); policies.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); policies.Controls.Add(up, 0, 0); policies.Controls.Add(down, 1, 0); settings.Controls.Add(policies, 0, 1);
   FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) }; captureFormat.Items.AddRange(new object[] { "Hex", "UTF-8 text" }); captureFormat.SelectedIndex = 0; options.Controls.Add(order); options.Controls.Add(capture); options.Controls.Add(captureFormat); settings.Controls.Add(options, 0, 2); settings.Controls.Add(note, 0, 3);
   FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; actions.Controls.Add(start); actions.Controls.Add(stop); Button help = new Button { Text = "Help", Width = 80, Height = 34 }; actions.Controls.Add(help); actions.Controls.Add(status); root.Controls.Add(actions, 0, 2); root.Controls.Add(totals, 0, 3); root.Controls.Add(grid, 0, 4); root.Controls.Add(log, 0, 5);
   root.Controls.Add(new Label { Text = "Local proxy • No administrator rights required • Settings take effect on Start", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 6);
   string[] names = { "ID", "Client", "State", "↑ Received", "↑ Sent", "↑ Units", "↑ Loss", "↓ Received", "↓ Sent", "↓ Units", "↓ Loss", "Overflow" };
   foreach (string name in names) grid.Columns.Add(name, name); grid.Columns[0].FillWeight = 35; grid.Columns[1].FillWeight = 160; grid.Columns[2].FillWeight = 85;
   Controls.Add(root); protocol.SelectedIndexChanged += (s, e) => UpdateNote(); UpdateNote();
   start.Click += async (s, e) => {
    if (busy) return; string path = null;
    if (capture.Checked) using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Traffic capture (*.tsv)|*.tsv", FileName = "netsim-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".tsv", OverwritePrompt = true }) {
     if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.FileName;
     if (File.Exists(path)) { MessageBox.Show(this, "Choose a new filename. Existing captures are never overwritten.", "Capture filename"); return; }
    }
    busy = true; settings.Enabled = false; start.Enabled = false;
    try {
     Settings config = new Settings { Bind = bind.Text.Trim(), Host = target.Text.Trim(), Protocol = protocol.Text, ListenPort = (int)listenPort.Value, TargetPort = (int)targetPort.Value, Up = up.Read(), Down = down.Read(), PreserveOrder = order.Checked, CapturePath = path, CaptureHex = captureFormat.SelectedIndex == 0 };
     if (engine != null) engine.Dispose(); engine = new Engine(config); grid.Rows.Clear(); log.Clear(); previousBytes = 0; previousTime = 0;
     await engine.StartAsync(); elapsed.Restart(); stop.Enabled = true; status.Text = "Running • " + config.Protocol + " " + config.Bind + ":" + config.ListenPort; timer.Start();
     if (path != null) log.AppendText("Capture: " + path + Environment.NewLine);
    } catch (Exception ex) { if (engine != null) engine.Dispose(); settings.Enabled = true; start.Enabled = true; status.Text = "Could not start"; MessageBox.Show(this, ex.Message, "NetSim", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    finally { busy = false; }
   };
   stop.Click += async (s, e) => { if (busy) return; busy = true; stop.Enabled = false; try { if (engine != null) await engine.StopAsync(); } catch (Exception ex) { log.AppendText(ex.Message + Environment.NewLine); } finally { elapsed.Stop(); RefreshStats(); timer.Stop(); settings.Enabled = true; start.Enabled = true; status.Text = "Stopped"; busy = false; } };
   timer.Tick += (s, e) => RefreshStats(); FormClosing += (s, e) => { if (busy) { e.Cancel = true; return; } timer.Stop(); if (engine != null) engine.Dispose(); };
   help.Click += (s, e) => { string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.txt"); try { Process.Start(path); } catch { MessageBox.Show(this, "Open README.txt beside NetSim.exe for setup and simulation details."); } };
  }
  void UpdateNote() { bool tcp = protocol.Text == "TCP"; order.Enabled = !tcp; note.Text = tcp ? "TCP: loss adds one retry delay per selected read chunk; stream order is always preserved. Units = read chunks, not network packets." : "UDP: loss drops datagrams. Disable Preserve UDP order to allow jitter to reorder them. Units = datagrams."; }
  static void Add(FlowLayoutPanel p, string label, Control control) { p.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(7, 5, 4, 0) }); p.Controls.Add(control); }
  void RefreshStats() {
   if (engine == null) return;
   Flow[] flows = engine.Flows.Values.OrderBy(f => f.Id).ToArray(); long bytes = 0, units = 0, losses = 0; int active = 0;
   // At most 500 retained rows; keep existing rows to avoid scroll jumps on refresh.
   var ids = new System.Collections.Generic.HashSet<int>(flows.Select(f => f.Id));
   for (int i = grid.Rows.Count - 1; i >= 0; i--) if (!ids.Contains((int)grid.Rows[i].Cells[0].Value)) grid.Rows.RemoveAt(i);
   var rows = grid.Rows.Cast<DataGridViewRow>().ToDictionary(r => (int)r.Cells[0].Value);
   foreach (Flow f in flows) {
    object[] values = { f.Id, f.Client, f.State, f.UpRx, f.UpTx, f.UpUnits, f.UpLoss, f.DownRx, f.DownTx, f.DownUnits, f.DownLoss, f.Overflow };
    DataGridViewRow row; if (rows.TryGetValue(f.Id, out row)) row.SetValues(values); else grid.Rows.Add(values);
    bytes += f.UpTx + f.DownTx; units += f.UpUnits + f.DownUnits; losses += f.UpLoss + f.DownLoss; if (f.State == "Connected" || f.State == "Connecting") active++;
   }
   double now = elapsed.Elapsed.TotalSeconds, rate = now > previousTime ? Math.Max(0, bytes - previousBytes) / (now - previousTime) : 0; previousTime = now; previousBytes = bytes;
   totals.Text = String.Format("Active: {0}    Forwarded: {1:N0} bytes    Current rate: {2:N1} KiB/s    Received units: {3:N0}    {4}: {5:N0}  (retained rows)", active, bytes, rate / 1024, units, engine.Config.Protocol == "TCP" ? "Retry events" : "Dropped", losses);
   string message; while (engine.Events.TryDequeue(out message)) log.AppendText(message + Environment.NewLine); if (log.TextLength > 30000) log.Text = log.Text.Substring(log.TextLength - 20000);
  }
 }
 static class Program {
  static string logPath;
  static bool reporting;
  static void WriteLog(string message) {
   try { File.AppendAllText(logPath, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); } catch { }
  }
  static void Report(Exception exception, bool interactive) {
   WriteLog(exception.ToString());
   if (!interactive || reporting) return;
   reporting = true;
   try { MessageBox.Show("NetSim encountered an error and will close.\r\n\r\n" + exception.Message + "\r\n\r\nDetails were written to:\r\n" + logPath, "NetSim error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
   finally { Application.Exit(); }
  }
  [STAThread] static int Main(string[] args) {
   bool diagnostics = args.Length == 2 && args[0] == "--diagnostics";
   try {
    string folder = diagnostics ? Path.GetFullPath(args[1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSim", "Logs");
    Directory.CreateDirectory(folder);
    logPath = Path.Combine(folder, "NetSim-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
   } catch { logPath = Path.Combine(Path.GetTempPath(), "NetSim-error.log"); }
   AppDomain.CurrentDomain.UnhandledException += (s, e) => { WriteLog("Unhandled exception: " + e.ExceptionObject); };
   Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
   Application.ThreadException += (s, e) => { Report(e.Exception, !diagnostics); if (diagnostics) Application.Exit(); };
   try {
    WriteLog("Starting NetSim 1.0.1; CLR " + Environment.Version + "; OS " + Environment.OSVersion + "; 64 bit " + Environment.Is64BitProcess);
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    using (MainWindow window = new MainWindow()) {
     Timer check = null;
     if (diagnostics) {
      window.Shown += (s, e) => {
       WriteLog("Main window shown");
       check = new Timer { Interval = 1500 };
       check.Tick += (sender, ev) => {
        check.Stop();
        try { using (Bitmap bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(Path.GetDirectoryName(logPath), "window.png")); } WriteLog("UI render succeeded"); }
        catch (Exception ex) { Report(ex, false); }
        window.Close();
       }; check.Start();
      };
     }
     Application.Run(window);
     if (check != null) check.Dispose();
    }
    WriteLog("Application exited normally"); return 0;
   } catch (Exception ex) { Report(ex, !diagnostics); return 1; }
  }
 }
}
