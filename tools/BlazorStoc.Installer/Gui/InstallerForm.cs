using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using BlazorStoc.Updater;

namespace BlazorStoc.Installer.Gui;

/// <summary>What the window does when the person presses Install: runs the installer with these arguments and says the exit code. The checks and the preview give their own.</summary>
public delegate Task<int> InstallAction(IReadOnlyList<string> arguments, Action<string> log, CancellationToken cancellationToken);

/// <summary>What the window knows about the server before it asks anything.</summary>
public sealed record ServerState(string Version, string? InstallRoot, string? CurrentVersion, string DefaultRoot);

/// <summary>
/// The window of BlazorStoc-Actualizare-&lt;version&gt;.exe: a short wizard. A new server: where to install and the ports (advanced), the administrator account of the application (user name,
/// password, password again with a line that says after every key whether they match), a summary, then the progress with the log. A server that has an installation: update (or repair, when the
/// version is the same) with a summary and the progress. The window only collects the answers and builds the same command line the console takes; the work is the installer's.
/// </summary>
public sealed class InstallerForm : Form
{
    private enum Step { Welcome, Account, Summary, Progress }

    private readonly ServerState server;
    private readonly InstallAction install;
    private readonly IReadOnlyList<string> presets;
    private readonly bool existing;
    private readonly bool repair;
    private Step step = Step.Welcome;
    private bool running;
    private int? exitCode;
    private string? finalPort;

    private readonly Panel header = new() { Dock = DockStyle.Top, Height = 74, BackColor = Color.FromArgb(24, 53, 38) };
    private readonly Label title = new() { Name = "lblTitle", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Location = new Point(24, 12) };
    private readonly Label subtitle = new() { Name = "lblSubtitle", ForeColor = Color.FromArgb(200, 225, 210), Font = new Font("Segoe UI", 9.5f), AutoSize = true, Location = new Point(26, 44) };
    private readonly Panel footer = new() { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.FromArgb(244, 244, 242) };
    private readonly Button back = new() { Name = "btnBack", Text = "‹ Înapoi", Size = new Size(110, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    private readonly Button next = new() { Name = "btnNext", Text = "Continuă ›", Size = new Size(130, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    private readonly Button cancel = new() { Name = "btnCancel", Text = "Anulează", Size = new Size(110, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
    private readonly Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 8) };

    // welcome
    private readonly Panel welcome = new() { Dock = DockStyle.Fill, Name = "pageWelcome" };
    private readonly Label welcomeText = new() { Name = "lblWelcome", AutoSize = false, Location = new Point(0, 0), Size = new Size(610, 120), Font = new Font("Segoe UI", 10f) };
    private readonly TextBox root = new() { Name = "txtRoot", Location = new Point(0, 148), Size = new Size(500, 27) };
    private readonly Button browse = new() { Name = "btnBrowse", Text = "Alege…", Location = new Point(510, 146), Size = new Size(100, 30) };
    private readonly Label rootLabel = new() { Text = "Folderul instalării", AutoSize = true, Location = new Point(0, 126), Font = new Font("Segoe UI Semibold", 9.5f) };
    private readonly Button advancedToggle = new() { Name = "btnAdvanced", Text = "Setări avansate ▾", Location = new Point(0, 190), Size = new Size(150, 30), FlatStyle = FlatStyle.Flat };
    private readonly Button uninstallButton = new() { Name = "btnUninstall", Text = "Dezinstalează…", Location = new Point(0, 330), Size = new Size(200, 34), Visible = false, ForeColor = Color.FromArgb(170, 40, 40) };
    private bool removing;
    private readonly Panel advanced = new() { Name = "pnlAdvanced", Location = new Point(0, 226), Size = new Size(610, 180), Visible = false };
    private readonly TextBox appPort = new() { Name = "txtAppPort", Text = ServerProvisionerDefaults.ApplicationPort, Location = new Point(0, 24), Size = new Size(110, 27) };
    private readonly TextBox dbPort = new() { Name = "txtDbPort", Text = ServerProvisionerDefaults.DatabasePort, Location = new Point(150, 24), Size = new Size(110, 27) };
    private readonly RadioButton dbInstall = new() { Name = "rbDbInstall", Text = "Instalează serverul de baze de date MariaDB (recomandat)", Checked = true, AutoSize = true, Location = new Point(0, 74) };
    private readonly RadioButton dbExternal = new() { Name = "rbDbExternal", Text = "Baza de date există deja în altă parte (nu instala nimic)", AutoSize = true, Location = new Point(0, 100) };
    private readonly Label portNote = new() { AutoSize = false, Size = new Size(610, 40), Location = new Point(0, 132), ForeColor = Color.FromArgb(90, 90, 90), Text = "Dacă un port este ocupat pe acest server, se alege automat următorul port liber și este scris în setări." };

    // account
    private readonly Panel account = new() { Dock = DockStyle.Fill, Name = "pageAccount", Visible = false };
    private readonly TextBox user = new() { Name = "txtUser", Location = new Point(0, 70), Size = new Size(300, 27) };
    private readonly TextBox display = new() { Name = "txtDisplay", Location = new Point(0, 134), Size = new Size(300, 27) };
    private readonly TextBox password = new() { Name = "txtPassword", UseSystemPasswordChar = true, Location = new Point(0, 198), Size = new Size(300, 27) };
    private readonly TextBox password2 = new() { Name = "txtPassword2", UseSystemPasswordChar = true, Location = new Point(0, 262), Size = new Size(300, 27) };
    private readonly Label match = new() { Name = "lblMatch", AutoSize = true, Location = new Point(312, 266), Font = new Font("Segoe UI Semibold", 9.5f) };
    private readonly Label accountProblem = new() { Name = "lblAccountProblem", AutoSize = false, Size = new Size(610, 40), Location = new Point(0, 298), ForeColor = Color.FromArgb(170, 40, 40) };

    // summary / progress
    private readonly Panel summary = new() { Dock = DockStyle.Fill, Name = "pageSummary", Visible = false };
    private readonly Label summaryText = new() { Name = "lblSummary", AutoSize = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10f) };
    private readonly Panel progress = new() { Dock = DockStyle.Fill, Name = "pageProgress", Visible = false };
    private readonly ProgressBar bar = new() { Name = "barProgress", Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Height = 18 };
    private readonly Label result = new() { Name = "lblResult", Dock = DockStyle.Top, Height = 54, Font = new Font("Segoe UI Semibold", 10.5f), Padding = new Padding(0, 8, 0, 0) };
    private readonly TextBox log = new() { Name = "txtLog", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = Color.White, WordWrap = true };
    private readonly Button openApp = new() { Name = "btnOpen", Text = "Deschide aplicația", Size = new Size(160, 34), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
    private readonly Button copyLog = new() { Name = "btnCopyLog", Text = "Copiază jurnalul", Size = new Size(150, 34), Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

    public InstallerForm(ServerState server, InstallAction install, IReadOnlyList<string> presets)
    {
        this.server = server;
        this.install = install;
        this.presets = presets;
        existing = server.CurrentVersion is not null;
        repair = existing && string.Equals(server.CurrentVersion, server.Version, StringComparison.Ordinal);
        Text = "Instalare BlazorStoc";
        ClientSize = new Size(660, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;
        Name = "InstallerForm";
        Build();
        ApplyPresets();
        ShowStep(Step.Welcome);
    }

    public int? ExitCode => exitCode;

    private void Build()
    {
        header.Controls.AddRange([title, subtitle]);
        footer.Controls.AddRange([back, next, cancel, openApp, copyLog]);
        Controls.AddRange([body, footer, header]);
        body.Controls.AddRange([welcome, account, summary, progress]);

        welcome.Controls.AddRange([welcomeText, rootLabel, root, browse, advancedToggle, advanced, uninstallButton]);
        advanced.Controls.AddRange([new Label { Text = "Port aplicație", AutoSize = true, Location = new Point(0, 2) }, new Label { Text = "Port bază de date", AutoSize = true, Location = new Point(150, 2) }, appPort, dbPort, dbInstall, dbExternal, portNote]);
        advancedToggle.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 190);

        account.Controls.AddRange([
            new Label { Text = "Contul de administrator al aplicației. Cu el te autentifici după instalare; mai târziu poți face alte conturi în Utilizatori.", AutoSize = false, Size = new Size(610, 36), Location = new Point(0, 0) },
            new Label { Text = "Nume utilizator (3–100 de caractere: litere, cifre, punct, cratimă, underscore)", AutoSize = true, Location = new Point(0, 50) }, user,
            new Label { Text = "Nume afișat (opțional)", AutoSize = true, Location = new Point(0, 114) }, display,
            new Label { Text = "Parola (minimum 8 caractere)", AutoSize = true, Location = new Point(0, 178) }, password,
            new Label { Text = "Repetă parola", AutoSize = true, Location = new Point(0, 242) }, password2, match, accountProblem]);

        summary.Controls.Add(summaryText);
        progress.Controls.AddRange([log, result, bar]);

        Layout += (_, _) => PlaceButtons();
        PlaceButtons();
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Alege folderul în care se instalează BlazorStoc", UseDescriptionForTitle = true, SelectedPath = root.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                root.Text = Path.GetFileName(dialog.SelectedPath.TrimEnd(Path.DirectorySeparatorChar)).Equals("BlazorStoc", StringComparison.OrdinalIgnoreCase) ? dialog.SelectedPath : Path.Combine(dialog.SelectedPath, "BlazorStoc");
        };
        advancedToggle.Click += (_, _) => { advanced.Visible = !advanced.Visible; advancedToggle.Text = advanced.Visible ? "Setări avansate ▴" : "Setări avansate ▾"; };
        password.TextChanged += (_, _) => UpdateAccount();
        password2.TextChanged += (_, _) => UpdateAccount();
        user.TextChanged += (_, _) => UpdateAccount();
        dbInstall.CheckedChanged += (_, _) => dbPort.Enabled = dbInstall.Checked;
        back.Click += (_, _) => ShowStep(step == Step.Summary && !NeedsAccount ? Step.Welcome : step - 1);
        next.Click += async (_, _) => await OnNextAsync();
        uninstallButton.Click += async (_, _) => await UninstallAsync();
        cancel.Click += (_, _) => Close();
        openApp.Click += (_, _) => { if (finalPort is not null) Process.Start(new ProcessStartInfo($"http://127.0.0.1:{finalPort}/") { UseShellExecute = true }); };
        copyLog.Click += (_, _) => { try { Clipboard.SetText(log.Text); } catch (System.Runtime.InteropServices.ExternalException) { } };
        FormClosing += (_, e) =>
        {
            if (!running) return;
            MessageBox.Show(this, "Instalarea este în curs: așteaptă să se termine.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
        };
        AcceptButton = next;
    }

    private bool NeedsAccount => !existing;

    private void PlaceButtons()
    {
        var y = (footer.Height - 34) / 2;
        cancel.Location = new Point(footer.Width - cancel.Width - 20, y);
        next.Location = new Point(cancel.Left - next.Width - 10, y);
        back.Location = new Point(next.Left - back.Width - 10, y);
        openApp.Location = new Point(24, y);
        copyLog.Location = new Point(24, y);
    }

    private void ApplyPresets()
    {
        root.Text = server.InstallRoot ?? server.DefaultRoot;
        root.ReadOnly = existing;
        browse.Enabled = !existing;
        // The options given on the command line next to --gui fill the window (and the ones the window has no field for are passed on as they are).
        for (var index = 0; index < presets.Count; index++)
        {
            var value = index + 1 < presets.Count && !presets[index + 1].StartsWith("--") ? presets[index + 1] : null;
            switch (presets[index].ToLowerInvariant())
            {
                case "--install-root" when value is not null: root.Text = value; break;
                case "--port" when value is not null: appPort.Text = value; break;
                case "--db-port" when value is not null: dbPort.Text = value; break;
                case "--no-database": dbExternal.Checked = true; break;
            }
        }
        if (existing || presets.Contains("--no-database", StringComparer.OrdinalIgnoreCase)) advancedToggle.Visible = !existing;
    }

    private void ShowStep(Step target)
    {
        step = target;
        welcome.Visible = target == Step.Welcome;
        account.Visible = target == Step.Account;
        summary.Visible = target == Step.Summary;
        progress.Visible = target == Step.Progress;
        back.Visible = target is Step.Account or Step.Summary;
        cancel.Text = target == Step.Progress && exitCode is not null ? "Închide" : "Anulează";
        openApp.Visible = copyLog.Visible = target == Step.Progress && exitCode is not null;
        copyLog.Visible = target == Step.Progress && exitCode is not null;
        openApp.Visible = target == Step.Progress && exitCode == 0 && finalPort is not null;
        switch (target)
        {
            case Step.Welcome:
                title.Text = existing ? (repair ? "Repararea BlazorStoc" : "Actualizare BlazorStoc") : "Instalare BlazorStoc";
                subtitle.Text = $"Versiunea {server.Version}";
                welcomeText.Text = existing
                    ? repair
                        ? $"BlazorStoc {server.CurrentVersion} este deja instalat în {server.InstallRoot}. Poți repara instalarea: aceeași versiune se instalează din nou peste cea existentă (datele nu se ating)."
                        : $"BlazorStoc {server.CurrentVersion} este instalat în {server.InstallRoot}. Se va instala versiunea {server.Version}. Aplicația se oprește câteva minute, baza de date este salvată înainte, iar dacă noua versiune nu pornește se revine singură la cea veche."
                    : "Pe acest server nu a fost găsită o instalare BlazorStoc. Installerul instalează aplicația, serverul de baze de date MariaDB (serviciu Windows), dependențele care lipsesc (.NET 9, Visual C++), alege porturile libere și face setările inițiale, inclusiv contul de administrator.";
                next.Text = NeedsAccount ? "Continuă ›" : (repair ? "Repară" : "Actualizează");
                rootLabel.Visible = root.Visible = browse.Visible = true;
                advancedToggle.Visible = !existing;
                uninstallButton.Visible = existing;
                advanced.Visible = !existing && advanced.Visible;
                break;
            case Step.Account:
                title.Text = "Contul de administrator";
                subtitle.Text = "Primul utilizator al aplicației";
                next.Text = "Continuă ›";
                UpdateAccount();
                break;
            case Step.Summary:
                title.Text = "Gata de instalare";
                subtitle.Text = $"BlazorStoc {server.Version}";
                summaryText.Text = SummaryText();
                next.Text = existing ? (repair ? "Repară" : "Actualizează") : "Instalează";
                break;
            case Step.Progress:
                title.Text = removing ? "Se dezinstalează…" : existing ? (repair ? "Se repară…" : "Se actualizează…") : "Se instalează…";
                subtitle.Text = "Nu închide fereastra";
                next.Visible = false;
                break;
        }
        if (target != Step.Progress) next.Visible = true;
        if (target == Step.Welcome) next.Enabled = true;
    }

    private string SummaryText()
    {
        if (existing)
            return $"Folder: {root.Text}\r\n\r\n" + (repair ? $"Se reinstalează versiunea {server.Version} peste cea existentă." : $"Se actualizează de la {server.CurrentVersion} la {server.Version}.") +
                "\r\n\r\nAplicația se oprește câteva minute. Baza de date se salvează înainte; dacă noua versiune nu pornește, se revine automat la cea veche.";
        var lines = new List<string>
        {
            $"Folder: {root.Text}", "",
            "Se vor face, în ordine:",
            "  •  dependențele care lipsesc (.NET 9, ASP.NET Core 9, Visual C++), din fișierul installerului",
            dbInstall.Checked ? $"  •  serverul de baze de date MariaDB, serviciu Windows, pe portul {dbPort.Text} (sau următorul liber)" : "  •  baza de date NU se instalează (există în altă parte)",
            $"  •  aplicația BlazorStoc, pe portul {appPort.Text} (sau următorul liber)",
            "  •  supervizorul care ține aplicația pornită",
            $"  •  contul de administrator „{user.Text.Trim()}”",
            "", "Porturile alese, dacă cele cerute sunt ocupate, se arată în jurnal și se scriu în setări."
        };
        return string.Join("\r\n", lines);
    }

    // The same rules the application has for its users (AdministratorRules); the line under the second password says after every key whether the two are the same.
    private bool UpdateAccount()
    {
        string? problem = null;
        if (password2.Text.Length == 0) { match.Text = ""; }
        else if (password.Text == password2.Text) { match.Text = "✓ Parolele coincid"; match.ForeColor = Color.FromArgb(30, 130, 60); }
        else { match.Text = "✗ Parolele nu coincid încă"; match.ForeColor = Color.FromArgb(190, 50, 50); }
        if (user.Text.Length > 0) problem = AdministratorRules.UsernameProblem(user.Text);
        if (problem is null && password.Text.Length > 0) problem = AdministratorRules.PasswordProblem(password.Text);
        accountProblem.Text = problem is null ? "" : Translate(problem);
        var valid = AdministratorRules.UsernameProblem(user.Text) is null && AdministratorRules.PasswordProblem(password.Text) is null && password.Text == password2.Text;
        if (step == Step.Account) next.Enabled = valid;
        return valid;
    }

    private static string Translate(string problem) => problem switch
    {
        _ when problem.Contains("3 to 100") => "Numele de utilizator are între 3 și 100 de caractere.",
        _ when problem.Contains("reserved") => "Numele „admin” este rezervat (administratorul din configurare). Alege alt nume.",
        _ when problem.Contains("letters") => "Numele poate conține litere, cifre, punct, cratimă și underscore.",
        _ when problem.Contains("at least") => $"Parola are cel puțin {AdministratorRules.MinimumPasswordLength} caractere.",
        _ => problem
    };

    private async Task OnNextAsync()
    {
        switch (step)
        {
            case Step.Welcome:
                if (Problem() is { } problem) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                ShowStep(NeedsAccount ? Step.Account : Step.Summary);
                break;
            case Step.Account:
                if (UpdateAccount()) ShowStep(Step.Summary);
                break;
            case Step.Summary:
                await RunAsync();
                break;
        }
    }

    private string? Problem()
    {
        if (existing) return null;
        if (string.IsNullOrWhiteSpace(root.Text) || !Path.IsPathRooted(root.Text)) return "Alege un folder complet pentru instalare (de exemplu C:\\BlazorStoc).";
        if (Directory.Exists(root.Text) && Directory.EnumerateFileSystemEntries(root.Text).Any() && !File.Exists(Path.Combine(root.Text, "updater", "settings.json")))
            return "Folderul ales nu este gol. Alege un folder nou sau gol.";
        foreach (var (text, name) in new[] { (appPort.Text, "aplicației"), (dbPort.Text, "bazei de date") })
            if (!int.TryParse(text, out var port) || port is < 1024 or > 65535) return $"Portul {name} este un număr între 1024 și 65535.";
        if (dbInstall.Checked && appPort.Text == dbPort.Text) return "Portul aplicației și al bazei de date trebuie să fie diferite.";
        return null;
    }

    private IReadOnlyList<string> Arguments()
    {
        var list = new List<string>();
        if (existing)
        {
            if (repair) list.Add("--repair");
            list.AddRange(["--yes", "--install-root", server.InstallRoot ?? root.Text, "--reason", "installer grafic", "--no-pause"]);
            return list;
        }
        list.AddRange(["--init", "--yes", "--install-root", root.Text.Trim(), "--port", appPort.Text, "--admin-user", user.Text.Trim(), "--no-pause"]);
        if (dbInstall.Checked) list.AddRange(["--db-port", dbPort.Text]); else list.Add("--no-database");
        if (display.Text.Trim().Length > 0) list.AddRange(["--admin-name", display.Text.Trim()]);
        // Options given next to --gui that the window has no field for (for example --no-service for a trial) go on as they are.
        for (var index = 0; index < presets.Count; index++)
        {
            if (presets[index] is "--install-root" or "--port" or "--db-port") { index++; continue; }
            if (presets[index] is "--no-database" or "--gui" or "--gui-preview") continue;
            list.Add(presets[index]);
        }
        return list;
    }

    private async Task UninstallAsync()
    {
        using var dialog = new UninstallDialog(server.InstallRoot ?? root.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var choice = dialog.Choice;
        removing = true;
        var arguments = new List<string> { "--uninstall", "--yes", "--install-root", server.InstallRoot ?? root.Text, "--no-pause" };
        if (choice.RemoveData) arguments.Add("--remove-data");
        if (choice.BackupFolder is not null) arguments.AddRange(["--backup-to", choice.BackupFolder]);
        await RunAsync(arguments);
    }

    private async Task RunAsync(IReadOnlyList<string>? custom = null)
    {
        ShowStep(Step.Progress);
        running = true;
        cancel.Enabled = false;
        bar.Visible = true;
        result.Text = "";
        var arguments = custom ?? Arguments();
        // The password goes to the installer in the environment of this process, never in an argument or a file.
        if (!existing && custom is null) Environment.SetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD", password.Text);
        int code;
        try { code = await Task.Run(() => install(arguments, AppendLog, CancellationToken.None)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            AppendLog("EROARE: " + exception.Message);
            code = 1;
        }
        finally { Environment.SetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD", null); }
        running = false;
        exitCode = code;
        bar.Visible = false;
        cancel.Enabled = true;
        finalPort = ReadPort();
        if (code == 0)
        {
            title.Text = "Instalarea s-a încheiat";
            subtitle.Text = "BlazorStoc este instalat și răspunde";
            result.ForeColor = Color.FromArgb(30, 130, 60);
            result.Text = removing ? "BlazorStoc a fost dezinstalat de pe acest server." : existing ? "Gata. Versiunea nouă rulează." : $"Gata. Autentifică-te în aplicație cu utilizatorul „{user.Text.Trim()}” și parola aleasă.";
        }
        else
        {
            title.Text = "Instalarea nu s-a terminat";
            subtitle.Text = $"Cod de ieșire {code}";
            result.ForeColor = Color.FromArgb(190, 50, 50);
            result.Text = $"Instalarea s-a oprit (cod {code}). Citește mesajele de mai jos; ce s-a făcut până aici se păstrează, iar installerul se poate rula din nou.";
        }
        ShowStep(Step.Progress);
        title.Text = removing ? (code == 0 ? "Dezinstalare încheiată" : "Dezinstalarea nu s-a terminat") : code == 0 ? "Instalarea s-a încheiat" : "Instalarea nu s-a terminat";
        subtitle.Text = code == 0 ? (removing ? "BlazorStoc nu mai este instalat" : "BlazorStoc este instalat și răspunde") : $"Cod de ieșire {code}";
        finalPort = removing ? null : finalPort;
        openApp.Visible = !removing && code == 0 && finalPort is not null;
        next.Visible = false;
    }

    // The port the application listens on, from the settings the installation wrote (settings.json of the updater).
    private string? ReadPort()
    {
        try
        {
            var file = Path.Combine(existing ? server.InstallRoot ?? root.Text : root.Text.Trim(), "updater", "settings.json");
            if (!File.Exists(file)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.TryGetProperty("port", out var port) && port.TryGetInt32(out var number) ? number.ToString() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(() => AppendLog(line)); } catch (InvalidOperationException) { } return; }
        log.AppendText(line.TrimEnd() + "\r\n");
    }

    /// <summary>For the checks and the preview: the text of the log and the last line of the result.</summary>
    public string LogText => log.Text;
}

/// <summary>The initial ports the window proposes (the installer takes the next free ones when they are taken).</summary>
internal static class ServerProvisionerDefaults
{
    public const string ApplicationPort = "5087";
    public const string DatabasePort = "3307";
}
