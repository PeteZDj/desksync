using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Desksync;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? Option(string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        using var serverService = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DesksyncSync");
        var serviceManaged = serverService != null && (Option("--data") == null || string.Equals(Option("--data"), @"C:\ProgramData\Desksync", StringComparison.OrdinalIgnoreCase));
        var data = Option("--data") ?? (serviceManaged ? @"C:\ProgramData\Desksync" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Afrosoftware", "Desksync"));
        using var singleton = new Mutex(true, "Local\\Desksync-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data.ToLowerInvariant())))[..16], out var first);
        if (!first) { MessageBox.Show("Desksync is already running. Open it from the notification area.", "Desksync"); return; }
        using var engine = new Engine(data, Option("--folder"));
        if (args.Contains("--initialize")) { engine.Initialize().GetAwaiter().GetResult(); return; }
        Application.Run(new MainWindow(engine, args.Contains("--background"), serviceManaged));
    }
}

sealed class MainWindow : Form
{
    readonly Engine engine;
    readonly bool background;
    readonly NotifyIcon tray;
    readonly Label state = new() { Text = "Starting your sync engine…", AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.FromArgb(37, 79, 46) };
    readonly Label detail = new() { AutoSize = true, MaximumSize = new Size(650, 0) };
    readonly TextBox peers = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 115, Dock = DockStyle.Fill };
    readonly FlowLayoutPanel actions = new() { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    bool ready, refreshing, paused, quitting;
    public MainWindow(Engine engine, bool background, bool serviceManaged)
    {
        this.engine = engine; this.background = background;
        Text = "Desksync · Afrosoftware"; Size = new Size(780, 650); MinimumSize = new Size(650, 570); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(246, 249, 244); Icon = SystemIcons.Application;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 10, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "DESKSYNC", Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Color.FromArgb(75, 139, 59), AutoSize = true });
        layout.Controls.Add(state); layout.Controls.Add(detail);
        var open = Button("Open my folder", () => { Directory.CreateDirectory(engine.Settings.Folder); Process.Start(new ProcessStartInfo("explorer.exe", engine.Settings.Folder) { UseShellExecute = true }); return Task.CompletedTask; });
        var pause = Button("Pause / resume", async () => { await engine.Pause(!paused); await RefreshStatus(); });
        actions.Controls.Add(open); actions.Controls.Add(pause);
        actions.Controls.Add(Button("Copy this device's code", () => { Clipboard.SetText(engine.Code.Encode()); MessageBox.Show("Pairing code copied. Paste it on the other device. A code identifies a device; both devices must accept each other before files transfer.", "Desksync pairing"); return Task.CompletedTask; }));
        actions.Controls.Add(Button("Join another folder", () => Pair(true)));
        actions.Controls.Add(Button("Approve a device", () => Pair(false)));
        actions.Controls.Add(Button("Remove device", Remove));
        layout.Controls.Add(actions);
        layout.Controls.Add(new Label { Text = "Your devices", Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true }); layout.Controls.Add(peers);
        layout.Controls.Add(new Label { Text = "Both devices must be running to exchange changes. Offline edits stay on disk and sync after reconnection. Deletions also sync. Conflicting edits are kept as separate conflict files.", AutoSize = true, MaximumSize = new Size(660, 0) });
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        links.Controls.Add(Button("Afrosoftware account", () => { Process.Start(new ProcessStartInfo("https://afrosoftware.com/desksync") { UseShellExecute = true }); return Task.CompletedTask; }));
        links.Controls.Add(Button("File versions", () => { var path = Path.Combine(engine.Settings.Folder, ".stversions"); if (!Directory.Exists(path)) throw new IOException("No older versions are stored yet. Versions are saved when another device changes or deletes a file."); Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); return Task.CompletedTask; }));
        links.Controls.Add(Button("Start at sign-in", () => { if (serviceManaged) { MessageBox.Show("This server already synchronizes automatically as the DesksyncSync Windows service, including while you are signed out.", "Desksync"); return Task.CompletedTask; } var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Desksync.cmd"); File.WriteAllText(exe, "@echo off\r\nstart \"\" \"" + Environment.ProcessPath + "\" --background\r\n"); MessageBox.Show("Desksync will start in the notification area when you sign in to Windows.", "Desksync"); return Task.CompletedTask; }));
        links.Controls.Add(Button("About", () => { MessageBox.Show("Desksync 0.1.0 — Windows preview\nAfrosoftware setup and tray application.\n\nEncrypted file synchronization by Syncthing 2.1.7 (MPL-2.0). Source and license information are included in the installation.\n\nYour Afrosoftware account protects the hosted console. Desktop file pairing uses device certificates and does not require an online account.\n\nVersion retention is 30 days for remotely replaced/deleted files. Sync is not a backup. Do not sync live database files or secrets.", "About Desksync"); return Task.CompletedTask; }));
        layout.Controls.Add(links);
        layout.Controls.Add(new Label { Text = serviceManaged ? "The server Windows service keeps syncing after this window closes." : "Close this window to keep syncing in the tray. Choose Quit Desksync to stop it.", AutoSize = true, ForeColor = Color.DimGray });
        Controls.Add(layout);
        var menu = new ContextMenuStrip(); menu.Items.Add("Open Desksync", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Open sync folder", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", engine.Settings.Folder) { UseShellExecute = true }));
        menu.Items.Add(serviceManaged ? "Close server dashboard" : "Quit Desksync", null, async (_, _) => { try { if (ready && !serviceManaged) await engine.Shutdown(); quitting = true; Close(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to stop Desksync"); } });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Desksync · starting", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += (_, e) => { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { timer.Dispose(); tray.Dispose(); };
        Shown += async (_, _) => { if (background) Hide(); try { await engine.Initialize(); ready = true; await RefreshStatus(); timer.Start(); } catch (Exception ex) { state.Text = "Desksync needs attention"; detail.Text = ex.Message; Show(); } };
        timer.Tick += async (_, _) => await RefreshStatus();
    }
    Button Button(string label, Func<Task> action)
    {
        var button = new Button { Text = label, AutoSize = true, Padding = new Padding(9, 5, 9, 5), Margin = new Padding(0, 6, 8, 6), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        button.Click += async (_, _) => { if (!ready) return; button.Enabled = false; try { await action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Desksync", MessageBoxButtons.OK, MessageBoxIcon.Warning); } finally { button.Enabled = true; } }; return button;
    }
    async Task Pair(bool join)
    {
        using var dialog = new Form { Text = join ? "Join your server's folder" : "Approve your other device", Width = 650, Height = 300, StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false, MaximizeBox = false };
        var text = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        var explanation = new Label { Dock = DockStyle.Top, Height = 85, Padding = new Padding(12), Text = join ? "Paste the server's pairing code. Your existing files stay in place and will merge with that folder. After joining, copy this device's code back to the server and approve it there." : "Paste your desktop's pairing code after it has joined this folder. Approving grants this device read, write, and delete access to the shared folder." };
        var accept = new Button { Text = "Pair device", Dock = DockStyle.Bottom, Height = 42, DialogResult = DialogResult.OK };
        dialog.Controls.Add(text); dialog.Controls.Add(explanation); dialog.Controls.Add(accept);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await engine.Pair(Pairing.Parse(text.Text), join); await RefreshStatus();
        MessageBox.Show("Device configured. File transfer begins after both sides approve each other. On a new desktop, copy its device code and approve it on the server.", "Desksync pairing");
    }
    async Task Remove()
    {
        var devices = (await engine.Get("config/devices")).AsArray().Where(d => d!["deviceID"]!.GetValue<string>() != engine.DeviceId).ToArray();
        if (devices.Length == 0) throw new IOException("There are no paired devices.");
        using var dialog = new Form { Text = "Remove a device", Width = 610, Height = 230, StartPosition = FormStartPosition.CenterParent, Font = Font };
        var select = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList }; foreach (var d in devices) select.Items.Add(d!["name"]!.GetValue<string>() + " · " + d["deviceID"]!.GetValue<string>());
        select.SelectedIndex = 0;
        var note = new Label { Text = "Removing a device stops future synchronization. It cannot erase files already copied to that device.", Dock = DockStyle.Top, Height = 60 };
        var accept = new Button { Text = "Remove device", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        dialog.Controls.Add(select); dialog.Controls.Add(note); dialog.Controls.Add(accept);
        if (dialog.ShowDialog(this) == DialogResult.OK) { await engine.Forget(devices[select.SelectedIndex]!["deviceID"]!.GetValue<string>()); await RefreshStatus(); }
    }
    async Task RefreshStatus()
    {
        if (refreshing || !ready) return; refreshing = true;
        try
        {
            var folder = await engine.Get("db/status?folder=" + Uri.EscapeDataString(engine.Settings.FolderId));
            var config = await engine.Get("config/folders/" + Uri.EscapeDataString(engine.Settings.FolderId));
            var connections = (await engine.Get("system/connections"))["connections"]!.AsObject();
            var devices = (await engine.Get("config/devices")).AsArray().Where(d => d!["deviceID"]!.GetValue<string>() != engine.DeviceId).ToArray();
            paused = config["paused"]?.GetValue<bool>() == true;
            var errors = folder["errors"]?.GetValue<int>() ?? 0;
            var connected = devices.Count(d => connections[d!["deviceID"]!.GetValue<string>()]?["connected"]?.GetValue<bool>() == true);
            var status = folder["state"]?.GetValue<string>() ?? "unknown";
            var need = folder["needTotalItems"]?.GetValue<long>() ?? 0;
            state.Text = paused ? "Sync paused" : errors > 0 ? "Some files need attention" : devices.Length == 0 ? "Ready to pair your desktop" : connected == 0 ? "Waiting for your other device" : status == "idle" && need == 0 ? "Up to date" : "Synchronizing your files";
            detail.Text = engine.Settings.Folder + "\n" + (folder["localFiles"]?.ToString() ?? "0") + " files on this device · " + connected + " of " + devices.Length + " devices connected" + (need > 0 ? " · " + need + " items to receive" : "") + (errors > 0 ? "\nCheck folder permissions and available disk space. Detailed engine errors are in the private engine.log file." : "");
            peers.Text = devices.Length == 0 ? "No other devices have access yet." : string.Join("\r\n\r\n", devices.Select(d => { var id = d!["deviceID"]!.GetValue<string>(); return d["name"] + " — " + (connections[id]?["connected"]?.GetValue<bool>() == true ? "Connected" : "Offline") + "\r\n" + id; }));
            tray.Text = "Desksync · " + state.Text;
        }
        catch (Exception ex) { state.Text = "Connection to sync engine lost"; detail.Text = ex.Message; tray.Text = "Desksync · needs attention"; }
        finally { refreshing = false; }
    }
}
