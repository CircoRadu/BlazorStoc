using System.IO.Compression;
using System.Text;
using BlazorStoc.Updater;

namespace BlazorStoc.Checks;

// Stage 5 of the application update (docs/PROPUNERE_ACTUALIZARE.md): BlazorStoc-Actualizare-<version>.exe. The payload (the package and the updater appended to the launcher), the command line,
// and the REAL InstallerRunner and updater command line (Cli) on a temporary installation: the fake host of the engine checks stands for the application, and the fake installer host runs the
// updater of the payload in this process instead of starting dotnet. What needs the real thing (the single-file launcher, UAC, dotnet, the supervisor, the scheduled task) is exercised against
// a real installation, not here.
public static partial class UpdateEngineChecks
{
    private static async Task InstallerAsync(Action<bool, string> check, string folder, SigningKeyStore keys, SigningKeyStore keysOther, string signer)
    {
        var work = Path.Combine(folder, "installer");
        Directory.CreateDirectory(work);
        await InstallerPayloadAsync(check, work, keys);
        InstallerArgumentsAsync(check);
        await InstallerFlowAsync(check, work, keys, keysOther, signer);
    }

    // ---- the file: launcher + payload ----

    private static async Task InstallerPayloadAsync(Action<bool, string> check, string work, SigningKeyStore keys)
    {
        var world = new World(Path.Combine(work, "payload-world"));
        var package = await world.PackAsync(keys, "2.0.0");
        var exe = await BuildInstallerAsync(work, package, "2.0.0", "payload");
        var launcher = await File.ReadAllBytesAsync(Path.Combine(work, "launcher.exe"));
        var exeBytes = await File.ReadAllBytesAsync(exe);
        check(exeBytes.Length > launcher.Length && exeBytes.AsSpan(0, launcher.Length).SequenceEqual(launcher), "Installer: the launcher is the beginning of the file, unchanged (the executable still starts)");

        using (var payload = InstallerPayload.TryOpen(exe))
        {
            var packageHash = await InstallerPayload.Sha256Async(package);
            check(payload is { Info.Version: "2.0.0" } && payload.Info.PackageSha256 == packageHash && payload.Info.BuiltUtc > DateTime.UtcNow.AddMinutes(-5), "Installer: the payload says which version and which package it carries");
            var taken = Path.Combine(work, "taken", "package.bsu");
            await payload!.ExtractPackageAsync(taken);
            check(File.ReadAllBytes(taken).AsSpan().SequenceEqual(File.ReadAllBytes(package)), "Installer: the package comes out of the payload byte for byte");
            var updater = Path.Combine(work, "taken", "updater");
            await payload.ExtractUpdaterAsync(updater);
            check(File.ReadAllText(Path.Combine(updater, "BlazorStoc.Updater.dll")) == "updater v1 2.0.0" && File.Exists(Path.Combine(updater, "scripts", "supervisor.ps1")) && !File.Exists(Path.Combine(updater, "BlazorStoc.Updater.pdb")),
                "Installer: the updater and its scripts come out of the payload, without symbols");
        }
        check(InstallerPayload.TryOpen(Path.Combine(work, "launcher.exe")) is null, "Installer: the launcher alone has no payload");

        var cut = Path.Combine(work, "cut.exe");
        await File.WriteAllBytesAsync(cut, exeBytes[..^5]);
        check(InstallerPayload.TryOpen(cut) is null, "Installer: a file cut short (the marker is gone) is not taken for an installer");

        var damagedLength = (byte[])exeBytes.Clone();
        BitConverter.GetBytes(long.MaxValue / 4).CopyTo(damagedLength, damagedLength.Length - 24);
        var lengthFile = Path.Combine(work, "length.exe");
        await File.WriteAllBytesAsync(lengthFile, damagedLength);
        check(Throws<InvalidDataException>(() => InstallerPayload.TryOpen(lengthFile)), "Installer: a payload that claims to be longer than the file is refused");

        var damagedPackage = (byte[])exeBytes.Clone();
        var inside = exeBytes.AsSpan().IndexOf(File.ReadAllBytes(package).AsSpan(0, 64));
        damagedPackage[inside + 100] ^= 0xFF;
        var damagedFile = Path.Combine(work, "damaged.exe");
        await File.WriteAllBytesAsync(damagedFile, damagedPackage);
        using (var payload = InstallerPayload.TryOpen(damagedFile))
        {
            var refused = false;
            try { await payload!.ExtractPackageAsync(Path.Combine(work, "taken", "damaged.bsu")); } catch (InvalidDataException) { refused = true; }
            check(inside > 0 && refused && !File.Exists(Path.Combine(work, "taken", "damaged.bsu")), "Installer: a package damaged on the way is found by its checksum and not left on disk");
        }

        var unsafeFile = Path.Combine(work, "unsafe.exe");
        await File.WriteAllBytesAsync(unsafeFile, RawInstaller(launcher, ["installer.json", "package.bsu", "updater/BlazorStoc.Updater.dll", "updater/../evil.txt"]));
        using (var payload = InstallerPayload.TryOpen(unsafeFile))
        {
            var refused = false;
            try { await payload!.ExtractUpdaterAsync(Path.Combine(work, "unsafe-out", "updater")); } catch (InvalidDataException) { refused = true; }
            check(refused && !File.Exists(Path.Combine(work, "unsafe-out", "evil.txt")), "Installer: a file name that leaves the folder is refused when the updater is taken out");
        }
        var missing = Path.Combine(work, "missing.exe");
        await File.WriteAllBytesAsync(missing, RawInstaller(launcher, ["installer.json", "package.bsu"]));
        check(Throws<InvalidDataException>(() => InstallerPayload.TryOpen(missing)), "Installer: a payload without the updater is refused");
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    // A file in the format of the installer whose zip holds the names given (the content is not used by the checks that read it).
    private static byte[] RawInstaller(byte[] launcher, string[] names)
    {
        using var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in names)
            {
                using var stream = zip.CreateEntry(name).Open();
                var body = name == "installer.json" ? """{"version":"9.9.9","packageSha256":"00","builtUtc":"2026-01-01T00:00:00Z"}""" : "x";
                stream.Write(Encoding.UTF8.GetBytes(body));
            }
        using var file = new MemoryStream();
        file.Write(launcher);
        file.Write(zipped.ToArray());
        file.Write(BitConverter.GetBytes(zipped.Length));
        file.Write(Encoding.ASCII.GetBytes(InstallerPayload.Marker));
        return file.ToArray();
    }

    private static async Task<string> BuildInstallerAsync(string work, string package, string version, string name)
    {
        var launcher = Path.Combine(work, "launcher.exe");
        if (!File.Exists(launcher)) await File.WriteAllBytesAsync(launcher, [.. Enumerable.Range(0, 4096).Select(index => (byte)(index * 7))]);
        var updater = Path.Combine(work, "updater-" + name);
        Directory.CreateDirectory(Path.Combine(updater, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(updater, "BlazorStoc.Updater.dll"), "updater v1 " + version);
        await File.WriteAllTextAsync(Path.Combine(updater, "BlazorStoc.Updater.pdb"), "symbols");
        foreach (var script in new[] { "supervisor", "data-root", "updater-task" }) await File.WriteAllTextAsync(Path.Combine(updater, "scripts", script + ".ps1"), $"{script} {version}");
        var exe = Path.Combine(work, name + ".exe");
        await InstallerPayload.BuildAsync(launcher, exe, package, updater, DateTime.UtcNow, version);
        return exe;
    }

    // ---- the command line ----

    private static void InstallerArgumentsAsync(Action<bool, string> check)
    {
        var plain = InstallerArguments.Parse([]);
        var repair = InstallerArguments.Parse(["--repair", "--yes", "--reason", "x y", "--install-root", @"D:\BlazorStoc"]);
        var rollback = InstallerArguments.Parse(["--rollback", "--restore-db", "b.pkg"]);
        var first = InstallerArguments.Parse(["--init", "--install-root", @"D:\B", "--port", "5088", "--env", "A=B;C=D", "--no-task"]);
        var trusting = InstallerArguments.Parse(["--trust", "ab12cd34", "--allow-downgrade", "--health-timeout", "30"]);
        check(plain.Mode == InstallerMode.Install && !plain.Yes && !plain.Init && plain.InstallRoot is null && InstallerArguments.Parse(["-h"]).Mode == InstallerMode.Help && InstallerArguments.Parse(["--check", "--trusted", "ab"]).Trusted == "ab",
            "Installer: no arguments means update to the version in the file; -h and --check are understood");
        check(repair.Mode == InstallerMode.Repair && repair.Yes && repair.Reason == "x y" && repair.InstallRoot == @"D:\BlazorStoc" && repair.Original.Count == 6 && rollback.Mode == InstallerMode.Rollback && rollback.RestoreDb == "b.pkg",
            "Installer: --repair and --rollback with their options, and the words typed are kept for the elevated copy");
        check(first.Init && first.Mode == InstallerMode.Init && first.Port == "5088" && first.Env == "A=B;C=D" && first.NoTask && trusting.Trust == "ab12cd34" && trusting.AllowDowngrade && trusting.HealthTimeout == "30" && !trusting.Init,
            "Installer: the options of the first step on a new server, and of an installation, are read");
        string[][] wrong = [["--repair", "--rollback"], ["--restore-db", "x"], ["--init", "--rollback"], ["--nothing"], ["stray"], ["--install-root"], ["--reason", "--yes"], ["--health-timeout", "abc"], ["--health-timeout", "0"], ["--status", "--check"], ["--port", "5000"], ["--no-task"], ["--env", "A=B"]];
        check(wrong.All(arguments => Throws<ArgumentException>(() => InstallerArguments.Parse(arguments))), "Installer: contradictory, unknown and incomplete options are refused");
    }

    // ---- the whole thing on a fake application ----

    private static async Task InstallerFlowAsync(Action<bool, string> check, string work, SigningKeyStore keys, SigningKeyStore keysOther, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(work, "flow-world"), keys, signer, "1.0.0");
        var root = world.Layout.Root;
        new UpdaterSettings(Port: 5999).Save(world.Layout);
        await File.WriteAllTextAsync(Path.Combine(world.Layout.UpdaterFolder, "BlazorStoc.Updater.dll"), "updater v1 1.0.0");
        var package11 = await world.PackAsync(keys, "1.1.0");
        var exe11 = await BuildInstallerAsync(work, package11, "1.1.0", "inst-1.1.0");

        // an update with the installer: the updater of the payload installs the package, then replaces the updater of the installation
        var host = new FakeInstallerHost(exe11, work, world);
        var code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root, "--reason", "prueba"]);
        var history = world.Layout.HistoryFile;
        var last = File.ReadLines(history).Last();
        check(code == 0 && world.Layout.Current == "1.1.0" && world.Layout.Previous == "1.0.0" && world.Host.Calls.Contains("stop") && world.Host.Calls.Contains("start:1.1.0"),
            $"Installer: the update to the version in the file is made by the engine (stop, switch, start); exit code {code}");
        check(host.Commands.Count == 2 && host.Commands[0].StartsWith("install ") && host.Commands[0].Contains("package.bsu") && host.Commands[0].Contains("--reason prueba") && host.Commands[1].StartsWith("refresh "),
            "Installer: the updater is asked to install the package, then to replace the updater of the installation");
        check(last.Contains("\"via\":\"installer\"") && last.Contains("\"reason\":\"prueba\"") && last.Contains("\"to\":\"1.1.0\""), "Installer: the history of the updates says it was the installer, with the reason typed");
        check(File.ReadAllText(Path.Combine(world.Layout.UpdaterFolder, "BlazorStoc.Updater.dll")) == "updater v1 1.1.0" && File.ReadAllText(Path.Combine(world.Layout.ScriptsFolder, "supervisor.ps1")) == "supervisor 1.1.0"
            && File.ReadAllText(Path.Combine(world.Layout.UpdaterFolder, "updater-task.ps1")) == "updater-task 1.1.0" && UpdaterSettings.Load(world.Layout).Port == 5999 && File.Exists(world.Layout.TrustedKeysFile),
            "Installer: after the update the updater and the scripts of the installation are the ones of the installer; settings and trusted signers are kept");
        check(host.Folders.Count == 1 && host.Folders.All(folder => !Directory.Exists(folder)) && host.Errors.Count == 0 && host.Messages.Any(message => message.StartsWith("Done: BlazorStoc 1.1.0 is installed")),
            "Installer: the temporary folder with the package and the updater is removed and the person is told it is done");

        // the same version again is refused by the engine (nothing is replaced); --repair reinstalls it
        host = new FakeInstallerHost(exe11, work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == 4 && host.Commands.Count == 1 && world.Layout.Current == "1.1.0" && host.Messages.Any(message => message.StartsWith("Refused")), $"Installer: the same version is refused and the updater of the installation is left alone; exit code {code}");
        host = new FakeInstallerHost(exe11, work, world);
        code = await new InstallerRunner(host).RunAsync(["--repair", "--yes", "--install-root", root]);
        check(code == 0 && host.Commands[0].Contains("--reinstall") && host.Commands[0].Contains("repair with the installer") && File.ReadLines(history).Last().Contains("\"to\":\"1.1.0\"") && world.Layout.Current == "1.1.0",
            $"Installer: --repair installs the version in the file again over the one that runs; exit code {code}");

        // the version that runs is damaged (a file is gone, the application is down): --repair puts it back although nothing "runs" any more
        var damaged = Path.Combine(world.Layout.AppFolder("1.1.0"), "BlazorStoc.dll");
        File.Delete(damaged);
        world.Host.Calls.Clear();
        host = new FakeInstallerHost(exe11, work, world);
        code = await new InstallerRunner(host).RunAsync(["--repair", "--yes", "--install-root", root]);
        check(code == 0 && File.Exists(damaged) && world.Layout.Current == "1.1.0" && world.Host.Calls.Contains("start:1.1.0") && host.Commands[1].StartsWith("refresh "),
            $"Installer: --repair also works when the installed version is damaged (nothing answers, a file is missing); exit code {code}");

        // a version older than the one that runs needs --allow-downgrade
        var exeOld = await BuildInstallerAsync(work, await world.PackAsync(keys, "0.9.0"), "0.9.0", "inst-0.9.0");
        host = new FakeInstallerHost(exeOld, work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == 4 && world.Layout.Current == "1.1.0", $"Installer: an older version is refused; exit code {code}");
        host = new FakeInstallerHost(exeOld, work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root, "--allow-downgrade"]);
        check(code == 0 && world.Layout.Current == "0.9.0" && host.Commands[0].Contains("--allow-downgrade"), $"Installer: --allow-downgrade accepts it; exit code {code}");

        // --rollback goes back to the previous version; the person is asked first
        host = new FakeInstallerHost(exe11, work, world) { Interactive = true };
        code = await new InstallerRunner(host).RunAsync(["--install-root", root]);
        check(code == 0 && world.Layout.Current == "1.1.0" && host.Questions == 1, $"Installer: with a console it asks, and Enter goes on; exit code {code}");
        host = new FakeInstallerHost(exe11, work, world);
        code = await new InstallerRunner(host).RunAsync(["--rollback", "--yes", "--install-root", root, "--restore-db", "b.pkg"]);
        check(code == 0 && world.Layout.Current == "0.9.0" && host.Commands[0].StartsWith("rollback ") && host.Commands[0].Contains("--restore-db b.pkg") && host.Commands[1].StartsWith("refresh ")
            && host.Messages.Any(message => message.StartsWith("Done: the previous version")), $"Installer: --rollback returns to the previous version (and restores the backup named); exit code {code}");

        await InstallerRefusalsAsync(check, work, keys, keysOther, signer, world, exe11);
        await InstallerFirstInstallAsync(check, work, keys, signer);
    }

    private static async Task InstallerRefusalsAsync(Action<bool, string> check, string work, SigningKeyStore keys, SigningKeyStore keysOther, string signer, World world, string exe)
    {
        var root = world.Layout.Root;
        var before = world.Layout.Current;

        var host = new FakeInstallerHost(exe, work, world) { Interactive = true };
        host.Answers.Enqueue("n");
        var code = await new InstallerRunner(host).RunAsync(["--install-root", root]);
        check(code == InstallerExit.Cancelled && host.Commands.Count == 0 && world.Layout.Current == before, "Installer: answering n cancels and nothing is touched");
        host = new FakeInstallerHost(exe, work, world);
        code = await new InstallerRunner(host).RunAsync(["--install-root", root]);
        check(code == InstallerExit.Cancelled && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("--yes")), "Installer: without a console and without --yes it does not guess");

        host = new FakeInstallerHost(exe, work, world) { Admin = false, ElevateCode = 0 };
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == 0 && host.Elevations.Count == 1 && host.Elevations[0] == "--yes --install-root " + root && host.Commands.Count == 0, "Installer: without administrator rights it starts itself again elevated, with the same words, and does nothing else");
        host = new FakeInstallerHost(exe, work, world) { Admin = false, ElevateCode = null };
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == InstallerExit.NotAdministrator && host.Commands.Count == 0, "Installer: when the elevation is refused it stops with its own exit code");

        host = new FakeInstallerHost(exe, work, world) { Dotnet = null };
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == InstallerExit.NoDotnet && host.Commands.Count == 0, "Installer: no dotnet on the machine is said, not attempted");
        host = new FakeInstallerHost(exe, work, world) { RuntimeProblem = "The .NET 9 runtime is not installed." };
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == InstallerExit.NoDotnet && host.Errors.Contains("The .NET 9 runtime is not installed.") && host.Commands.Count == 0, "Installer: a missing .NET 9 runtime is said in words");

        host = new FakeInstallerHost(exe, work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes"]);
        check(code == InstallerExit.NoInstallation && host.Commands.Count == 0, "Installer: without a registered installation and without --install-root it asks for the folder");
        host = new FakeInstallerHost(exe, work, world) { Registered = root };
        code = await new InstallerRunner(host).RunAsync(["--status"]);
        check(code == 0 && host.Commands.Count == 1 && host.Commands[0].StartsWith("status ") && host.Output.Contains("Running version (current.txt): " + world.Layout.Current), "Installer: the registered installation is found by itself and --status asks nothing and changes nothing");
        var empty = Path.Combine(work, "not-an-installation");
        Directory.CreateDirectory(empty);
        host = new FakeInstallerHost(exe, work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", empty]);
        check(code == InstallerExit.NoInstallation && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("--init")), "Installer: a folder that is not an installation is not turned into one by accident (--init says it is wanted)");

        // an unusable file
        host = new FakeInstallerHost(Path.Combine(work, "launcher.exe"), work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == InstallerExit.NoPayload && host.Commands.Count == 0, "Installer: the launcher without a payload says so");
        host = new FakeInstallerHost(Path.Combine(work, "length.exe"), work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == InstallerExit.DamagedPayload && host.Commands.Count == 0, "Installer: a damaged payload stops before anything is touched");
        host = new FakeInstallerHost(Path.Combine(work, "damaged.exe"), work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == InstallerExit.DamagedPayload && host.Commands.Count == 0 && host.Folders.All(folder => !Directory.Exists(folder)), "Installer: a package damaged on the way stops before the updater is asked anything, and the temporary folder is removed");
        host = new FakeInstallerHost(exe, work, world);
        code = await new InstallerRunner(host).RunAsync(["--wat"]);
        check(code == InstallerExit.BadArguments && host.Commands.Count == 0, "Installer: a wrong option is reported with the list of options");

        // a restoration that fails before the version is switched: the database and current.txt are untouched and the version that ran is started again, not left down
        world.Tools.FailRestore = true;
        world.Host.Calls.Clear();
        host = new FakeInstallerHost(exe, work, world);
        code = await new InstallerRunner(host).RunAsync(["--rollback", "--yes", "--install-root", root, "--restore-db", "b.pkg"]);
        world.Tools.FailRestore = false;
        check(code == 6 && world.Layout.Current == before && world.Host.Calls[^1] == "start:" + before && world.Host.Up && File.ReadLines(world.Layout.HistoryFile).Last().Contains("started again") && host.Commands.Count == 1,
            $"Installer: a rollback whose restoration fails leaves the version that ran in place and starts it again; exit code {code}");

        // --check needs no administrator and installs nothing: the signer it expects is the one that signed
        host = new FakeInstallerHost(exe, work, world) { Admin = false };
        code = await new InstallerRunner(host).RunAsync(["--check", "--trusted", signer]);
        check(code == 0 && host.Elevations.Count == 0 && host.Commands.Count == 1 && host.Commands[0].StartsWith("verify ") && world.Layout.Current == before, $"Installer: --check verifies the package of the file without administrator rights and without touching the installation; exit code {code}");
        host = new FakeInstallerHost(exe, work, world) { Admin = false };
        code = await new InstallerRunner(host).RunAsync(["--check", "--trusted", SigningKeyStore.Fingerprint(keysOther.LoadPublic())]);
        check(code == 3, $"Installer: --check with another signer expected says the signer is not the one; exit code {code}");

        // a version that does not start on the server: the engine puts the old one back, the installer says so and leaves the updater alone
        var exeBad = await BuildInstallerAsync(work, await world.PackAsync(keys, "1.2.0"), "1.2.0", "inst-1.2.0");
        world.Host.Broken.Add("1.2.0");
        var updaterBefore = File.ReadAllText(Path.Combine(world.Layout.UpdaterFolder, "BlazorStoc.Updater.dll"));
        host = new FakeInstallerHost(exeBad, work, world);
        code = await new InstallerRunner(host).RunAsync(["--yes", "--install-root", root]);
        check(code == 5 && world.Layout.Current == before && host.Commands.Count == 1 && File.ReadAllText(Path.Combine(world.Layout.UpdaterFolder, "BlazorStoc.Updater.dll")) == updaterBefore
            && host.Messages.Any(message => message.Contains("previous version was put back")), $"Installer: a version that does not answer is rolled back by the engine, the updater of the installation is not replaced; exit code {code}");
        world.Host.Broken.Clear();
    }

    private static async Task InstallerFirstInstallAsync(Action<bool, string> check, string work, SigningKeyStore keys, string signer)
    {
        var fresh = new World(Path.Combine(work, "first-world"));
        var exe = await BuildInstallerAsync(work, await fresh.PackAsync(keys, "1.0.0"), "1.0.0", "inst-first");
        var host = new FakeInstallerHost(exe, work, fresh);
        var code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", fresh.Layout.Root, "--port", "5999", "--no-task"]);
        check(code == 0 && fresh.Layout.Current is null && UpdaterSettings.Load(fresh.Layout).Port == 5999 && File.Exists(fresh.Layout.SettingsFile) && Directory.Exists(fresh.Layout.StagedFolder) && host.Commands.Count == 1 && host.Commands[0].StartsWith("init ")
            && File.ReadAllText(Path.Combine(fresh.Layout.UpdaterFolder, "BlazorStoc.Updater.dll")) == "updater v1 1.0.0" && File.ReadAllText(Path.Combine(fresh.Layout.ScriptsFolder, "supervisor.ps1")) == "supervisor 1.0.0"
            && (!Acl.IsAdministrator() || host.Location.Value == fresh.Layout.Root),
            $"Installer: --init on a new server makes the installation (updater, scripts, settings) and installs no version; exit code {code}");
        check(host.Messages.Any(message => message.Contains("supervisor.ps1") && message.Contains("-Action install") && message.Contains("-Port 5999") && message.Contains("--trust")), "Installer: --init says what to do next (register the supervisor, run the installer again)");

        // the second step: the folder is now known (the registry has it) and the signer is confirmed with --trust
        host = new FakeInstallerHost(exe, work, fresh) { Registered = fresh.Layout.Root };
        code = await new InstallerRunner(host).RunAsync(["--yes", "--trust", signer]);
        check(code == 0 && fresh.Layout.Current == "1.0.0" && host.Commands[0].StartsWith("install ") && host.Commands[1].StartsWith("refresh ")
            && new TrustedKeys(fresh.Layout.TrustedKeysFile).Load().Any(key => string.Equals(key.Fingerprint, signer, StringComparison.OrdinalIgnoreCase)),
            $"Installer: the first version is installed in the registered folder and the signer confirmed with --trust is trusted from then on; exit code {code}");
        host = new FakeInstallerHost(exe, work, fresh);
        code = await new InstallerRunner(host).RunAsync(["--init", "--yes"]);
        check(code == InstallerExit.BadArguments && host.Commands.Count == 0, "Installer: --init without a folder is refused");
    }

    /// <summary>The machine of the installer for the checks: it answers like a console, a Windows account and a dotnet, and "runs" the updater of the payload as the command line of this process (the engine is the one of the fake application).</summary>
    private sealed class FakeInstallerHost(string exe, string work, World world) : IInstallerHost
    {
        private readonly EngineFactory engines = (layout, _, log, _) => new UpdateEngine(layout, world.Host, world.Tools, log, world.Options());
        private readonly StringWriter output = new();

        public string ExePath => exe;
        public bool Admin { get; init; } = true;
        public bool IsAdministrator => Admin;
        public bool Interactive { get; init; }
        public string? Registered { get; init; }
        public string? RegisteredInstallRoot => Registered;
        public string? Dotnet { get; init; } = "dotnet";
        public string? RuntimeProblem { get; init; }
        public int? ElevateCode { get; init; }
        public Queue<string> Answers { get; } = new();
        public int Questions { get; private set; }
        public List<string> Messages { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Commands { get; } = [];
        public List<string> Elevations { get; } = [];
        public List<string> Folders { get; } = [];
        public FakeLocation Location { get; } = new();
        public string Output => output.ToString();

        public void Info(string message) => Messages.Add(message);
        public void Error(string message) => Errors.Add(message);

        public string? ReadLine()
        {
            Questions++;
            return Answers.Count > 0 ? Answers.Dequeue() : "";
        }

        public int? Elevate(IReadOnlyList<string> arguments)
        {
            Elevations.Add(string.Join(' ', arguments));
            return ElevateCode;
        }

        public string? FindDotnet(string? requested) => Dotnet;
        public Task<string?> RuntimeProblemAsync(string dotnet, CancellationToken cancellationToken) => Task.FromResult(RuntimeProblem);

        public string CreateWorkFolder()
        {
            var folder = Path.Combine(work, "installer-work-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            Folders.Add(folder);
            return folder;
        }

        public void DeleteWorkFolder(string folder) => Directory.Delete(folder, recursive: true);

        public async Task<int> RunUpdaterAsync(string dotnet, string updaterDll, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(string.Join(' ', arguments));
            var line = arguments.ToList();
            // "init" and "refresh" copy the updater that runs them: here it runs inside the test, so the folder of the payload is named.
            if (line[0] is "init" or "refresh") line.AddRange(["--from", Path.GetDirectoryName(updaterDll)!]);
            var previousOut = Console.Out;
            var previousError = Console.Error;
            Console.SetOut(output);
            Console.SetError(output);
            try { return await Cli.RunAsync([.. line], engines, Location); }
            finally { Console.SetOut(previousOut); Console.SetError(previousError); }
        }
    }

    private sealed class FakeLocation : IInstallLocationStore
    {
        public string? Value { get; private set; }
        public string? Read() => Value;
        public void Write(string installRoot) => Value = Path.GetFullPath(installRoot);
    }
}
