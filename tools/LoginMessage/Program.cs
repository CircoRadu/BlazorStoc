using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
using Microsoft.Win32;

// LoginMessage: la pornire pune mesajul (implicit "NU OPRI PC") pe imaginea ecranului de blocare/login din Windows 11
// (PersonalizationCSP). Imaginea originala NU se sterge: se salveaza valorile vechi din registry, iar o sarcina programata
// SYSTEM "la pornirea Windows" le readuce la repornire, inainte sa apara ecranul de login.
//   LoginMessage.exe ["mesaj"]     aplica mesajul
//   LoginMessage.exe --restore     readuce imaginea originala (o ruleaza si sarcina programata)
// Necesita drepturi de administrator (cerute prin manifest) si Windows 11 Pro/Enterprise/Education.

const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
const string TaskName = "LoginMessageRestore";
string[] valueNames = ["LockScreenImagePath", "LockScreenImageUrl", "LockScreenImageStatus"];
var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LoginMessage");
var imagePath = Path.Combine(dir, "lockscreen.png");
var backupPath = Path.Combine(dir, "backup.json");

try
{
    Directory.CreateDirectory(dir);
    if (args.Length > 0 && args[0].Equals("--restore", StringComparison.OrdinalIgnoreCase))
    {
        Restore();
        return 0;
    }
    Apply(args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "NU OPRI PC");
    Console.WriteLine("Mesajul este pus pe ecranul de blocare/login; la repornire revine imaginea originala.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Eroare: " + ex.Message);
    return 1;
}

void Apply(string message)
{
    // Valorile originale se salveaza o singura data (o a doua rulare nu suprascrie originalul cu imaginea noastra).
    using (var key = Registry.LocalMachine.CreateSubKey(KeyPath, true))
    {
        if (!File.Exists(backupPath))
        {
            var backup = new Dictionary<string, object?[]>();
            foreach (var name in valueNames)
            {
                var value = key.GetValue(name);
                backup[name] = value is null ? [] : [value, key.GetValueKind(name).ToString()];
            }
            File.WriteAllText(backupPath, JsonSerializer.Serialize(backup));
        }
    }

    RenderImage(message, imagePath);

    // Sarcina de revenire se creeaza INAINTE de a schimba imaginea: daca esueaza, nimic nu ramane schimbat.
    var exe = Environment.ProcessPath!;
    Schtasks($"/Create /TN {TaskName} /SC ONSTART /RU SYSTEM /RL HIGHEST /F /TR \"\\\"{exe}\\\" --restore\"");

    using var k = Registry.LocalMachine.CreateSubKey(KeyPath, true);
    k.SetValue("LockScreenImagePath", imagePath, RegistryValueKind.String);
    k.SetValue("LockScreenImageUrl", imagePath, RegistryValueKind.String);
    k.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
}

void Restore()
{
    using (var key = Registry.LocalMachine.CreateSubKey(KeyPath, true))
    {
        Dictionary<string, JsonElement[]>? backup = null;
        if (File.Exists(backupPath))
            backup = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(File.ReadAllText(backupPath));
        foreach (var name in valueNames)
        {
            if (backup is not null && backup.TryGetValue(name, out var saved) && saved.Length == 2)
            {
                var kind = Enum.Parse<RegistryValueKind>(saved[1].GetString()!);
                object value = kind == RegistryValueKind.DWord ? saved[0].GetInt32() : saved[0].GetString()!;
                key.SetValue(name, value, kind);
            }
            else
            {
                key.DeleteValue(name, false);
            }
        }
    }
    TryDelete(imagePath);
    TryDelete(backupPath);
    Schtasks($"/Delete /TN {TaskName} /F", ignoreFailure: true);
}

void RenderImage(string message, string path)
{
    // Ecranul de blocare se afiseaza la rezolutia monitorului; 3840x2160 se reduce curat pe orice ecran 16:9.
    const int w = 3840, h = 2160;
    using var bmp = new Bitmap(w, h);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.FromArgb(150, 0, 0));
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    var lines = message.Replace("\\n", "\n").Split('\n');
    // Fontul cel mai mare in care toate liniile incap pe latime (cu margini) si pe inaltime.
    float size = 600;
    Font font;
    SizeF Measure(Font f)
    {
        float maxW = 0, totalH = 0;
        foreach (var l in lines) { var s = g.MeasureString(l, f, int.MaxValue, StringFormat.GenericTypographic); maxW = Math.Max(maxW, s.Width); totalH += s.Height; }
        return new SizeF(maxW, totalH);
    }
    while (true)
    {
        font = new Font("Segoe UI Black", size, FontStyle.Bold, GraphicsUnit.Pixel);
        var m = Measure(font);
        if ((m.Width <= w * 0.9f && m.Height <= h * 0.8f) || size <= 40) break;
        font.Dispose();
        size -= 10;
    }
    using (font)
    using (var brush = new SolidBrush(Color.White))
    {
        var lineH = font.GetHeight(g);
        var y = (h - lineH * lines.Length) / 2f;
        var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center };
        foreach (var l in lines)
        {
            g.DrawString(l, font, brush, new RectangleF(0, y, w, lineH), fmt);
            y += lineH;
        }
    }
    bmp.Save(path, ImageFormat.Png);
}

void Schtasks(string arguments, bool ignoreFailure = false)
{
    using var p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
    p.WaitForExit();
    if (p.ExitCode != 0 && !ignoreFailure)
        throw new InvalidOperationException("schtasks a esuat: " + p.StandardError.ReadToEnd().Trim());
}

void TryDelete(string path)
{
    try { File.Delete(path); } catch { /* fisier folosit: ramane, nu mai este referit */ }
}
