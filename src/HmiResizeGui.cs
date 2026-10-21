// HmiResizeGui - petite interface graphique qui pilote HmiResize.exe (même dossier)
// et affiche son rapport en direct.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HmiResize
{
    class MainForm : Form
    {
        static readonly string[] Resolutions =
        {
            "MTP400 (480x272)", "MTP700 (800x480)", "MTP1000 (1280x800)", "MTP1200 (1280x800)",
            "MTP1500 (1920x1080)", "MTP1900 (1920x1080)", "MTP2200 (1920x1080)"
        };

        readonly ComboBox cbFrom = new ComboBox(), cbTo = new ComboBox(), cbFont = new ComboBox();
        readonly RadioButton rbStretch = new RadioButton(), rbUniform = new RadioButton();
        readonly CheckBox chkParts = new CheckBox(), chkScripts = new CheckBox(), chkLines = new CheckBox(), chkSave = new CheckBox();
        readonly TextBox txtDevice = new TextBox(), txtScreens = new TextBox(), txtExclude = new TextBox();
        readonly TextBox txtOut = new TextBox();
        readonly List<Button> buttons = new List<Button>();
        readonly string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HmiResize.exe");
        Process running;

        public MainForm()
        {
            Text = "HmiResize - WinCC Unified screen resizer";
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            // --- Réglages ---------------------------------------------------------------------
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            cbFrom.Items.Add("auto (most common screen size)");
            cbFrom.Items.AddRange(Resolutions);
            cbFrom.SelectedIndex = 2;   // MTP700
            cbTo.Items.AddRange(Resolutions);
            cbTo.SelectedIndex = 3;     // MTP1200
            cbFont.Items.AddRange(new object[] { "auto", "none", "1.25", "1.5", "2" });
            cbFont.SelectedIndex = 0;
            foreach (ComboBox c in new[] { cbFrom, cbTo, cbFont }) c.Dock = DockStyle.Fill;

            rbStretch.Text = "Stretch (fill the whole display)"; rbStretch.Checked = true; rbStretch.AutoSize = true;
            rbUniform.Text = "Keep proportions"; rbUniform.AutoSize = true;
            var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            modes.Controls.Add(rbStretch); modes.Controls.Add(rbUniform);

            AddRow(grid, 0, "Source resolution:", cbFrom, "Target resolution:", cbTo);
            AddRow(grid, 1, "Mode:", modes, "Font factor:", cbFont);
            AddRow(grid, 2, "HMI (if several):", txtDevice, "Screens to process:", txtScreens);
            AddRow(grid, 3, "", new Label { Text = "Empty = all. Wildcards * ? and ; between names.", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left },
                    "Screens to exclude:", txtExclude);

            chkParts.Text = "Control columns and inner fonts"; chkParts.Checked = true;
            chkScripts.Text = "Rescale pop-up coordinates (scripts)"; chkScripts.Checked = true;
            chkLines.Text = "Line and border widths";
            chkSave.Text = "Save the project after resizing";
            var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            foreach (CheckBox c in new[] { chkParts, chkScripts, chkLines, chkSave }) { c.AutoSize = true; c.Margin = new Padding(3, 3, 16, 3); checks.Controls.Add(c); }
            grid.Controls.Add(checks, 0, 4);
            grid.SetColumnSpan(checks, 4);
            root.Controls.Add(grid, 0, 0);

            // --- Boutons ----------------------------------------------------------------------
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 8) };
            bar.Controls.Add(MakeButton("List screens", (s, e) => Run("--list")));
            bar.Controls.Add(MakeButton("Dry run", (s, e) => Run(BuildArgs(true))));
            Button apply = MakeButton("Resize screens", (s, e) => Apply());
            apply.Font = new Font(Font, FontStyle.Bold);
            bar.Controls.Add(apply);
            bar.Controls.Add(MakeButton("Compile HMI", (s, e) => Run("--compile" + DeviceArg())));
            bar.Controls.Add(MakeButton("Save project", (s, e) => Run("--save-only")));
            Button clear = new Button { Text = "Clear", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            clear.Click += (s, e) => txtOut.Clear();
            bar.Controls.Add(clear);
            root.Controls.Add(bar, 0, 1);

            // --- Rapport ----------------------------------------------------------------------
            txtOut.Multiline = true; txtOut.ReadOnly = true; txtOut.ScrollBars = ScrollBars.Both; txtOut.WordWrap = false;
            txtOut.Dock = DockStyle.Fill; txtOut.Font = new Font("Consolas", 9f); txtOut.BackColor = Color.White;
            root.Controls.Add(txtOut, 0, 2);

            if (!File.Exists(exe)) Append("HmiResize.exe not found next to this program: " + exe);
            else Append("The project must be open in TIA Portal. Start with \"Dry run\", then \"Resize screens\".");
        }

        static void AddRow(TableLayoutPanel grid, int row, string l1, Control c1, string l2, Control c2)
        {
            grid.Controls.Add(new Label { Text = l1, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }, 0, row);
            c1.Dock = DockStyle.Fill;
            grid.Controls.Add(c1, 1, row);
            grid.Controls.Add(new Label { Text = l2, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(16, 7, 3, 3) }, 2, row);
            c2.Dock = DockStyle.Fill;
            grid.Controls.Add(c2, 3, row);
        }

        Button MakeButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            b.Click += click;
            buttons.Add(b);
            return b;
        }

        // "MTP700 (800x480)" -> "MTP700" ; texte libre "1280x800" gardé tel quel
        static string Res(ComboBox cb)
        {
            string s = cb.Text.Trim();
            if (s.StartsWith("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
            int p = s.IndexOf(' ');
            return p > 0 ? s.Substring(0, p) : s;
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "") + "\""; }

        string DeviceArg() { return txtDevice.Text.Trim().Length > 0 ? " --device " + Quote(txtDevice.Text.Trim()) : ""; }

        string BuildArgs(bool dryRun)
        {
            var sb = new StringBuilder();
            sb.Append("--from ").Append(Res(cbFrom)).Append(" --to ").Append(Res(cbTo));
            if (rbUniform.Checked) sb.Append(" --mode uniform");
            sb.Append(" --font ").Append(cbFont.Text.Trim().Length > 0 ? cbFont.Text.Trim().Replace(',', '.') : "auto");
            if (!chkParts.Checked) sb.Append(" --no-parts");
            if (!chkScripts.Checked) sb.Append(" --no-scripts");
            if (chkLines.Checked) sb.Append(" --lines");
            sb.Append(DeviceArg());
            if (txtScreens.Text.Trim().Length > 0) sb.Append(" --screens ").Append(Quote(txtScreens.Text.Trim()));
            if (txtExclude.Text.Trim().Length > 0) sb.Append(" --exclude ").Append(Quote(txtExclude.Text.Trim()));
            if (dryRun) sb.Append(" --dry-run");
            else if (chkSave.Checked) sb.Append(" --save");
            return sb.ToString();
        }

        void Apply()
        {
            string msg = "Resize the screens from " + cbFrom.Text + " to " + cbTo.Text + "?\n\n" +
                         "A single Ctrl+Z in TIA Portal reverts the whole operation.\n" +
                         "Warning: running it again on screens already resized enlarges them again.";
            if (MessageBox.Show(this, msg, "Confirm resize", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Run(BuildArgs(false));
        }

        void Run(string args)
        {
            if (running != null) return;
            if (!File.Exists(exe)) { Append("HmiResize.exe not found: " + exe); return; }

            Append("");
            Append("> HmiResize.exe " + args);
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // HmiResize écrit dans la page de codes de la console (OEM)
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) BeginInvoke((Action)(() => Append(e.Data))); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) BeginInvoke((Action)(() => Append(e.Data))); };
            p.Exited += (s, e) => BeginInvoke((Action)(() =>
            {
                Append(p.ExitCode == 0 ? "[done]" : "[done, exit code " + p.ExitCode + "]");
                p.Dispose();
                running = null;
                SetBusy(false);
            }));
            SetBusy(true);
            running = p;
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }

        void SetBusy(bool busy)
        {
            foreach (Button b in buttons) b.Enabled = !busy;
            UseWaitCursor = busy;
        }

        void Append(string line)
        {
            txtOut.AppendText(line + Environment.NewLine);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running != null &&
                MessageBox.Show(this, "An operation is running. Close anyway?", "HmiResize", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
