using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

ApplicationConfiguration.Initialize();
Application.Run(new SetupWindow());

sealed class SetupWindow : Form
{
    readonly CheckBox startup = new() { Text = "Start Desksync when I sign in to Windows", Checked = true, AutoSize = true };
    readonly Label status = new() { Text = "Windows x64 preview · No account required for private device pairing.", AutoSize = true, MaximumSize = new Size(540, 0) };
    readonly Button install = new() { Text = "Install Desksync", AutoSize = true, Padding = new Padding(18, 8, 18, 8) };
    public SetupWindow()
    {
        Text = "Desksync Setup"; Size = new Size(640, 470); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(246, 249, 244);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(30), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { Text = "Your files. Your server.", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new Label { Text = "Desksync keeps C:\\Desksync in sync with the devices you approve. Files stay on disk for offline use. Only this folder is shared.\n\nInstall on both Windows devices, join the server's folder, then approve the desktop on the server. Changes and deletions travel both ways.", AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 18, 0, 18) });
        layout.Controls.Add(startup); layout.Controls.Add(status); layout.Controls.Add(install); Controls.Add(layout);
        install.Click += async (_, _) =>
        {
            install.Enabled = false;
            try
            {
                if (Process.GetProcessesByName("Desksync").Length > 0) throw new IOException("Quit Desksync from its tray menu before installing an update.");
                var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Afrosoftware", "Desksync"); Directory.CreateDirectory(target);
                status.Text = "Installing Desksync and its encrypted sync engine…";
                await Task.Run(() => { using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Desksync.Payload")!; using var zip = new ZipArchive(stream); zip.ExtractToDirectory(target, true); });
                var exe = Path.Combine(target, "Desksync.exe");
                var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Afrosoftware"); Directory.CreateDirectory(programs);
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                WriteShortcut(Path.Combine(programs, "Desksync.lnk"), exe);
                WriteShortcut(Path.Combine(desktop, "Desksync.lnk"), exe);
                var startupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Desksync.lnk");
                if (startup.Checked) WriteShortcut(startupPath, exe, "--background"); else if (File.Exists(startupPath)) File.Delete(startupPath);
                status.Text = "Installed. Your existing files have been kept. Pair your other device in Desksync to begin.";
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); install.Text = "Installed";
            }
            catch (Exception ex) { status.Text = ex.Message; install.Enabled = true; }
        };
    }
    static void WriteShortcut(string path, string exe, string arguments = "")
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcut creation is unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        try { dynamic shortcut = shell.CreateShortcut(path); try { shortcut.TargetPath = exe; shortcut.Arguments = arguments; shortcut.WorkingDirectory = Path.GetDirectoryName(exe); shortcut.Description = "Desksync by Afrosoftware"; shortcut.Save(); } finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); } }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
}
