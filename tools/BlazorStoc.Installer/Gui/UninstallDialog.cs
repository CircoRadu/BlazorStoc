using System.Drawing;

namespace BlazorStoc.Installer.Gui;

/// <summary>The choice made in the uninstall dialog: delete the data too, and where to save a restore package first (null: no package).</summary>
public sealed record UninstallChoice(bool RemoveData, string? BackupFolder);

/// <summary>
/// "Dezinstalează…": two ways. Remove only the application (the database and the files of the application stay, a new installation reuses them) or delete everything with the loss of all the data,
/// optionally after a restore package (the backup of the database with the files of the application) has been saved in a folder of the person's choice, outside the installation.
/// </summary>
public sealed class UninstallDialog : Form
{
    private readonly RadioButton keep = new() { Name = "rbKeep", Text = "Șterge doar aplicația și păstrează baza de date și fișierele (recomandat)", Checked = true, AutoSize = true, Location = new Point(20, 70) };
    private readonly RadioButton all = new() { Name = "rbAll", Text = "Ștergere totală, cu pierderea tuturor datelor", AutoSize = true, Location = new Point(20, 128), ForeColor = Color.FromArgb(170, 40, 40) };
    private readonly CheckBox package = new() { Name = "chkPackage", Text = "Creează un pachet de restaurare înainte de ștergere", Checked = true, AutoSize = true, Location = new Point(48, 196), Enabled = false };
    private readonly TextBox folder = new() { Name = "txtBackupFolder", Location = new Point(48, 226), Size = new Size(400, 27), Enabled = false };
    private readonly Button browse = new() { Name = "btnBackupBrowse", Text = "Alege…", Location = new Point(456, 224), Size = new Size(90, 30), Enabled = false };
    private readonly Button ok = new() { Name = "btnUninstallOk", Text = "Dezinstalează", Location = new Point(300, 330), Size = new Size(130, 34) };
    private readonly Button cancel = new() { Name = "btnUninstallCancel", Text = "Renunță", DialogResult = DialogResult.Cancel, Location = new Point(440, 330), Size = new Size(110, 34) };
    private readonly Label problem = new() { Name = "lblUninstallProblem", AutoSize = false, Size = new Size(520, 36), Location = new Point(28, 280), ForeColor = Color.FromArgb(170, 40, 40) };

    public UninstallDialog(string installRoot)
    {
        Text = "Dezinstalare BlazorStoc";
        ClientSize = new Size(580, 384);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        Name = "UninstallDialog";
        folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BlazorStoc-restaurare");
        Controls.AddRange([
            new Label { Text = $"Instalarea din {installRoot}. Se opresc și se șterg aplicația, supervizorul, sarcinile programate, regula de firewall și serviciul bazei de date.", AutoSize = false, Size = new Size(540, 48), Location = new Point(20, 14) },
            keep,
            new Label { Text = "Baza de date (folderul instanței MariaDB) și fișierele aplicației (poze, arhive, chei) rămân pe disc; o instalare nouă le refolosește.", AutoSize = false, Size = new Size(520, 40), Location = new Point(40, 94), ForeColor = Color.FromArgb(90, 90, 90) },
            all,
            new Label { Text = "Se șterg baza de date și toate fișierele aplicației. Nu se mai pot recupera decât dintr-un pachet de restaurare.", AutoSize = false, Size = new Size(520, 40), Location = new Point(40, 152), ForeColor = Color.FromArgb(90, 90, 90) },
            package, folder, browse, problem, ok, cancel]);
        keep.CheckedChanged += (_, _) => Refresh_();
        all.CheckedChanged += (_, _) => Refresh_();
        package.CheckedChanged += (_, _) => Refresh_();
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Folderul în care se salvează pachetul de restaurare (în afara instalării)", UseDescriptionForTitle = true, SelectedPath = folder.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
        };
        ok.Click += (_, _) =>
        {
            if (all.Checked && package.Checked)
            {
                if (!Path.IsPathRooted(folder.Text)) { problem.Text = "Alege un folder complet pentru pachetul de restaurare."; return; }
                if (Path.GetFullPath(folder.Text).StartsWith(Path.GetFullPath(installRoot).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) { problem.Text = "Pachetul trebuie salvat în afara folderului instalării, care se șterge."; return; }
            }
            var question = all.Checked
                ? "Se șterg aplicația ȘI TOATE DATELE" + (package.Checked ? $", după ce se salvează pachetul de restaurare în {folder.Text}." : ", FĂRĂ pachet de restaurare.") + " Continui?"
                : "Se șterge aplicația; datele se păstrează. Continui?";
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, all.Checked ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (all.Checked && !package.Checked && MessageBox.Show(this, "Ești sigur? Fără pachet, datele șterse nu se mai pot recupera.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok;
        CancelButton = cancel;
        Refresh_();
    }

    private void Refresh_()
    {
        package.Enabled = all.Checked;
        folder.Enabled = browse.Enabled = all.Checked && package.Checked;
        problem.Text = "";
    }

    public UninstallChoice Choice => new(all.Checked, all.Checked && package.Checked ? folder.Text.Trim() : null);
}
