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

    private static async Task<string> BuildInstallerAsync(string work, string package, string version, string name, bool withPrerequisites = false)
    {
        var launcher = Path.Combine(work, "launcher.exe");
        if (!File.Exists(launcher)) await File.WriteAllBytesAsync(launcher, [.. Enumerable.Range(0, 4096).Select(index => (byte)(index * 7))]);
        var updater = Path.Combine(work, "updater-" + name);
        Directory.CreateDirectory(Path.Combine(updater, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(updater, "BlazorStoc.Updater.dll"), "updater v1 " + version);
        await File.WriteAllTextAsync(Path.Combine(updater, "BlazorStoc.Updater.pdb"), "symbols");
        foreach (var script in new[] { "supervisor", "data-root", "updater-task", "provision-database", "uninstall-database", "firewall" }) await File.WriteAllTextAsync(Path.Combine(updater, "scripts", script + ".ps1"), $"{script} {version}");
        foreach (var resource in new[] { "database-accounts.json", "schema-mariadb.sql", "triggers-mariadb.sql" }) await File.WriteAllTextAsync(Path.Combine(updater, "scripts", resource), $"{resource} {version}");
        var exe = Path.Combine(work, name + ".exe");
        string? prerequisites = null;
        if (withPrerequisites)
        {
            prerequisites = Path.Combine(work, "prerequisites-" + name);
            Directory.CreateDirectory(prerequisites);
            foreach (var file in new[] { "dotnet-runtime-9.0.20-win-x64.exe", "aspnetcore-runtime-9.0.20-win-x64.exe", "vc_redist.x64.exe" }) await File.WriteAllTextAsync(Path.Combine(prerequisites, file), "installer of " + file);
        }
        await InstallerPayload.BuildAsync(launcher, exe, package, updater, DateTime.UtcNow, version, default, prerequisites);
        return exe;
    }

    // ---- the command line ----

    private static void InstallerArgumentsAsync(Action<bool, string> check)
    {
        var plain = InstallerArguments.Parse([]);
        var repair = InstallerArguments.Parse(["--repair", "--yes", "--reason", "x y", "--install-root", @"D:\BlazorStoc"]);
        var rollback = InstallerArguments.Parse(["--rollback", "--restore-db", "b.pkg"]);
        var first = InstallerArguments.Parse(["--init", "--install-root", @"D:\B", "--port", "5088", "--db-port", "3310", "--env", "A=B;C=D", "--no-task", "--no-database", "--no-install"]);
        var trusting = InstallerArguments.Parse(["--trust", "ab12cd34", "--allow-downgrade", "--health-timeout", "30"]);
        check(plain.Mode == InstallerMode.Install && !plain.Yes && !plain.Init && plain.InstallRoot is null && InstallerArguments.Parse(["-h"]).Mode == InstallerMode.Help && InstallerArguments.Parse(["--check", "--trusted", "ab"]).Trusted == "ab",
            "Installer: no arguments means update to the version in the file; -h and --check are understood");
        check(repair.Mode == InstallerMode.Repair && repair.Yes && repair.Reason == "x y" && repair.InstallRoot == @"D:\BlazorStoc" && repair.Original.Count == 6 && rollback.Mode == InstallerMode.Rollback && rollback.RestoreDb == "b.pkg",
            "Installer: --repair and --rollback with their options, and the words typed are kept for the elevated copy");
        check(first.Init && first.Mode == InstallerMode.Init && first.Port == "5088" && first.DbPort == "3310" && first.NoDatabase && first.NoInstall && first.Env == "A=B;C=D" && first.NoTask && trusting.Trust == "ab12cd34" && trusting.AllowDowngrade && trusting.HealthTimeout == "30" && !trusting.Init,
            "Installer: the options of the first step on a new server, and of an installation, are read");
        string[][] wrong = [["--db-port", "3310"], ["--init", "--port", "80"], ["--init", "--db-port", "abc"], ["--no-database"], ["--repair", "--rollback"], ["--restore-db", "x"], ["--init", "--rollback"], ["--nothing"], ["stray"], ["--install-root"], ["--reason", "--yes"], ["--health-timeout", "abc"], ["--health-timeout", "0"], ["--status", "--check"], ["--port", "5000"], ["--no-task"], ["--env", "A=B"]];
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
        File.WriteAllText(Path.Combine(empty, "other-file.txt"), "not ours");
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
        // prepare only: the ports asked for are taken on this server, so the next free ones are chosen and written to the settings; the database server and the supervisor get them
        var fresh = new World(Path.Combine(work, "first-world"));
        var exe = await BuildInstallerAsync(work, await fresh.PackAsync(keys, "1.0.0"), "1.0.0", "inst-first");
        var host = new FakeInstallerHost(exe, work, fresh) { BusyPorts = [5999, 3999, 6000] };
        var code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", fresh.Layout.Root, "--port", "5999", "--db-port", "3999", "--no-task"]);
        var settings = UpdaterSettings.Load(fresh.Layout);
        check(code == 0 && fresh.Layout.Current is null && settings.Port == 6001 && settings.DatabasePort == 4000 && File.Exists(fresh.Layout.SettingsFile) && Directory.Exists(fresh.Layout.StagedFolder)
            && host.Commands.Count == 1 && host.Commands[0].StartsWith("init ") && File.ReadAllText(Path.Combine(fresh.Layout.UpdaterFolder, "BlazorStoc.Updater.dll")) == "updater v1 1.0.0"
            && File.ReadAllText(Path.Combine(fresh.Layout.ScriptsFolder, "supervisor.ps1")) == "supervisor 1.0.0" && File.ReadAllText(Path.Combine(fresh.Layout.ScriptsFolder, "schema-mariadb.sql")) == "schema-mariadb.sql 1.0.0"
            && (!Acl.IsAdministrator() || host.Location.Value == fresh.Layout.Root),
            $"Installer: --init --no-install on a new server makes the installation and writes the FREE ports (5999 and 3999 were taken: {settings.Port} and {settings.DatabasePort}); exit code {code}");
        check(host.Scripts.Calls.Count == 3 && host.Scripts.Calls[0].StartsWith("provision-database.ps1 ") && host.Scripts.Calls[0].Contains("-Port 4000 ") && host.Scripts.Calls[0].Contains(Path.Combine(fresh.Layout.Root, "mariadb"))
            && host.Scripts.Calls[0].Contains("-MigratorFile " + fresh.Layout.MigrationAccountFile) && host.Scripts.Calls[1] == $"supervisor.ps1 -Action install -Port 6001 -InstallRoot {fresh.Layout.Root}",
            "Installer: the database server is made on the chosen database port and the supervisor registered on the chosen application port, before any version");
        check(host.Output.Contains("5999 is taken on this server, 6001 is free") && host.Output.Contains("3999 is taken on this server, 4000 is free"), "Installer: the ports that had to be moved are said in words");
        check(host.Messages.Any(message => message.Contains("--trust")), "Installer: --init --no-install says to run the installer again to install the version");

        // run again: what exists is kept, the ports stay (the application on its port is no reason to move it)
        host = new FakeInstallerHost(exe, work, fresh) { BusyPorts = [6001, 4000] };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", fresh.Layout.Root, "--no-task"]);
        settings = UpdaterSettings.Load(fresh.Layout);
        check(code == 0 && settings.Port == 6001 && settings.DatabasePort == 4000 && host.Scripts.Calls[0].Contains("-Port 4000 "), "Installer: --init again keeps the ports already written, even though the application and the database now listen on them");

        // everything in one command, on the initial ports (free)
        var all = new World(Path.Combine(work, "all-world"));
        var exeAll = await BuildInstallerAsync(work, await all.PackAsync(keys, "1.0.0"), "1.0.0", "inst-all");
        host = new FakeInstallerHost(exeAll, work, all);
        code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", all.Layout.Root, "--trust", signer, "--no-task", "--no-admin"]);
        settings = UpdaterSettings.Load(all.Layout);
        check(code == 0 && all.Layout.Current == "1.0.0" && settings.Port == 5087 && settings.DatabasePort == 3307 && settings.MariaServiceName == "BlazorStocMariaDB" && host.Commands.Count == 2 && host.Commands[0].StartsWith("init ")
            && host.Commands[1].StartsWith("install ") && host.Commands[1].Contains("--reason first installation") && host.Scripts.Calls.Count == 3 && host.Messages.Any(message => message.Contains("The installation is complete"))
            && new TrustedKeys(all.Layout.TrustedKeysFile).Load().Any(key => string.Equals(key.Fingerprint, signer, StringComparison.OrdinalIgnoreCase)),
            $"Installer: --init installs everything in one go (folders, database server, supervisor, the version) on the initial ports when they are free; exit code {code}");

        // the database server cannot be made: nothing is installed and the cause is said
        var broken = new World(Path.Combine(work, "broken-world"));
        var exeBroken = await BuildInstallerAsync(work, await broken.PackAsync(keys, "1.0.0"), "1.0.0", "inst-broken");
        host = new FakeInstallerHost(exeBroken, work, broken);
        host.Scripts.DatabaseExit = 7;
        code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", broken.Layout.Root, "--trust", signer, "--no-task", "--no-admin"]);
        check(code == 7 && broken.Layout.Current is null && host.Commands.Count == 1 && host.Scripts.Calls.Count == 1 && host.Output.Contains("The database server could not be made"), "Installer: a database server that cannot be made stops the installation before the version");

        // the database is somewhere else
        var external = new World(Path.Combine(work, "external-world"));
        var exeExternal = await BuildInstallerAsync(work, await external.PackAsync(keys, "1.0.0"), "1.0.0", "inst-external");
        host = new FakeInstallerHost(exeExternal, work, external) { BusyPorts = [5087] };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", external.Layout.Root, "--no-database", "--no-supervisor", "--no-firewall", "--no-task"]);
        settings = UpdaterSettings.Load(external.Layout);
        check(code == 0 && settings.Port == 5088 && settings.DatabasePort is null && host.Scripts.Calls.Count == 0, "Installer: --no-database --no-supervisor choose only the application port and run no script");

        // a plain run on a server without an installation: the first installation is made by itself, in the default folder (here one of the checks)
        var plain = new World(Path.Combine(work, "plain-world"));
        var exePlain = await BuildInstallerAsync(work, await plain.PackAsync(keys, "1.0.0"), "1.0.0", "inst-plain");
        host = new FakeInstallerHost(exePlain, work, plain) { DefaultInstallRoot = plain.Layout.Root, Interactive = true, Administrator = new AdministratorAccount("boss", "Boss", "correct horse") };
        code = await new InstallerRunner(host).RunAsync(["--yes"]);
        check(code == 0 && plain.Layout.Current == "1.0.0" && host.Commands[1].Contains("--trust-package-signer") && new TrustedKeys(plain.Layout.TrustedKeysFile).Load().Any(key => string.Equals(key.Fingerprint, signer, StringComparison.OrdinalIgnoreCase))
            && host.Output.Contains("trusted from now on"),
            "Installer: the first installation trusts the signer of the package inside the file, nobody types a fingerprint");
        check(plain.Layout.Current == "1.0.0" && host.Commands.Count == 3 && host.Commands[0].StartsWith("init ") && host.Commands[1].StartsWith("install ") && host.Commands[2].StartsWith("create-admin ") && host.Scripts.Calls.Count == 3,
            $"Installer: without --init, on a server with no installation, the installer makes everything by itself in the default folder; exit code {code}");
        host = new FakeInstallerHost(exePlain, work, plain) { DefaultInstallRoot = Path.Combine(work, "never-made"), Interactive = true };
        host.Answers.Enqueue("n");
        code = await new InstallerRunner(host).RunAsync(["--trust", signer]);
        check(code == InstallerExit.Cancelled && host.Commands.Count == 0 && !Directory.Exists(Path.Combine(work, "never-made")), "Installer: a person at the console is told what will be installed and n cancels before anything is made");
        var empty2 = Path.Combine(work, "empty-folder");
        Directory.CreateDirectory(empty2);
        host = new FakeInstallerHost(exePlain, work, plain) { Interactive = true, Administrator = new AdministratorAccount("boss", "Boss", "correct horse") };
        code = await new InstallerRunner(host).RunAsync(["--yes", "--trust", signer, "--install-root", empty2]);
        check(code == 0 && host.Commands.Count == 3 && host.Commands[0].StartsWith("init "), "Installer: an empty folder named by --install-root is a new installation too");
        // the administrator account of the application
        var adm = new World(Path.Combine(work, "admin-world"));
        var exeAdm = await BuildInstallerAsync(work, await adm.PackAsync(keys, "1.0.0"), "1.0.0", "inst-admin");
        Environment.SetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD", "correct horse");
        try
        {
            host = new FakeInstallerHost(exeAdm, work, adm);
            code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", adm.Layout.Root, "--admin-user", "boss", "--admin-name", "Boss B", "--no-task"]);
            check(code == 0 && host.Commands.Count == 3 && host.Commands[2] == "create-admin --install-root " + adm.Layout.Root && host.AdminEnvironment is { } env && env["BLAZORSTOC_ADMIN_USER"] == "boss" && env["BLAZORSTOC_ADMIN_NAME"] == "Boss B"
                && env["BLAZORSTOC_ADMIN_PASSWORD"] == "correct horse" && !host.Commands.Any(command => command.Contains("correct horse")) && host.Messages.Any(message => message.Contains("Administrator account \"boss\" made")),
                $"Installer: the administrator account is made after the version, with the details in the environment of the updater and never on a command line; exit code {code}");
            host = new FakeInstallerHost(exeAdm, work, adm) { AdminCode = 4 };
            code = await new InstallerRunner(host).RunAsync(["--repair", "--yes", "--install-root", adm.Layout.Root]);
            check(code == 0 && !host.Commands.Any(command => command.StartsWith("create-admin")), "Installer: a repair does not ask for or make an account");
            var again = new World(Path.Combine(work, "admin-world-2"));
            var exeAgain = await BuildInstallerAsync(work, await again.PackAsync(keys, "1.0.0"), "1.0.0", "inst-admin-2");
            host = new FakeInstallerHost(exeAgain, work, again) { AdminCode = 4 };
            code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", again.Layout.Root, "--admin-user", "boss", "--no-task"]);
            check(code == 0 && host.Messages.Any(message => message.Contains("exists already") && message.Contains("NOT the one typed")), "Installer: a user with that name already in the application is kept and the person is told the password typed was not applied");
            var failed = new World(Path.Combine(work, "admin-world-3"));
            var exeFailed = await BuildInstallerAsync(work, await failed.PackAsync(keys, "1.0.0"), "1.0.0", "inst-admin-3");
            host = new FakeInstallerHost(exeFailed, work, failed) { AdminCode = 1 };
            code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", failed.Layout.Root, "--admin-user", "boss", "--no-task"]);
            check(code == 0 && failed.Layout.Current == "1.0.0" && host.Messages.Any(message => message.Contains("could not be made") && message.Contains("\"admin\"")), "Installer: when the account cannot be made the version stays installed and the way in with the configured admin is said");
        }
        finally { Environment.SetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD", null); }
        var nobody = new World(Path.Combine(work, "admin-world-4"));
        var exeNobody = await BuildInstallerAsync(work, await nobody.PackAsync(keys, "1.0.0"), "1.0.0", "inst-admin-4");
        host = new FakeInstallerHost(exeNobody, work, nobody);
        code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", nobody.Layout.Root, "--no-task"]);
        check(code == InstallerExit.NoAdministrator && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("--admin-user") && message.Contains("--no-admin")), "Installer: without a console and without --admin-user it stops before anything is made, and says what to give");
        host = new FakeInstallerHost(exeNobody, work, nobody);
        code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", nobody.Layout.Root, "--no-task", "--admin-user", "boss"]);
        check(code == InstallerExit.NoAdministrator && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("BLAZORSTOC_ADMIN_PASSWORD")), "Installer: --admin-user without the password in the environment is refused (no password on the command line)");
        Environment.SetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD", "short");
        try
        {
            host = new FakeInstallerHost(exeNobody, work, nobody);
            code = await new InstallerRunner(host).RunAsync(["--init", "--yes", "--install-root", nobody.Layout.Root, "--no-task", "--admin-user", "boss"]);
            check(code == InstallerExit.NoAdministrator && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("at least 8")), "Installer: a password the application would refuse (under 8 characters) is refused before anything is made");
        }
        finally { Environment.SetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD", null); }
        check(AdministratorRules.UsernameProblem("admin") is { } reserved && reserved.Contains("reserved") && AdministratorRules.UsernameProblem("Admin") is not null && AdministratorRules.UsernameProblem(" ADMIN ") is not null && AdministratorRules.UsernameProblem("admin2") is null && AdministratorRules.UsernameProblem("alex.admin") is null,
            "Installer: the user name \"admin\" (any case) is refused, it is the administrator of the configuration; names that only contain it are fine");
        check(AdministratorPromptChecks(), "Installer prompt: user name, password and the password again; the second one is checked after every key, a wrong pair is asked again, nothing typed as a password is shown");
        // the prerequisites inside the installer: what the server lacks is installed from the file, signed by Microsoft, silently
        var preWorld = new World(Path.Combine(work, "prereq-world"));
        var exePre = await BuildInstallerAsync(work, await preWorld.PackAsync(keys, "1.0.0"), "1.0.0", "inst-pre", withPrerequisites: true);
        using (var opened = InstallerPayload.TryOpen(exePre)!)
        {
            var extracted = Path.Combine(work, "pre-extracted");
            await opened.ExtractPrerequisitesAsync(extracted);
            check(opened.PrerequisiteFiles.SequenceEqual(["aspnetcore-runtime-9.0.20-win-x64.exe", "dotnet-runtime-9.0.20-win-x64.exe", "vc_redist.x64.exe"]) && File.ReadAllText(Path.Combine(extracted, "vc_redist.x64.exe")) == "installer of vc_redist.x64.exe",
                "Installer payload: the installers of the prerequisites travel inside the file and come out unchanged");
        }
        using (var opened = InstallerPayload.TryOpen(exe)!) check(opened.PrerequisiteFiles.Count == 0, "Installer payload: an installer made without prerequisites lists none");
        var preRoot = preWorld.Layout.Root;
        host = new FakeInstallerHost(exePre, work, preWorld) { NeedsRuntime = true, VisualCpp = false };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == 0 && host.Prerequisites.Select(call => call.Split(' ')[0]).SequenceEqual(["dotnet-runtime-9.0.20-win-x64.exe", "aspnetcore-runtime-9.0.20-win-x64.exe", "vc_redist.x64.exe"]) && host.Prerequisites.All(call => call.EndsWith("/install /quiet /norestart"))
            && host.Commands.Count == 1 && host.Messages.Any(message => message.Contains("The prerequisites are installed")),
            $"Installer: a server without .NET and without the Visual C++ runtime gets them from the file (silently, no restart) and then the installation goes on; exit code {code}");
        host = new FakeInstallerHost(exePre, work, preWorld);
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == 0 && host.Prerequisites.Count == 0, "Installer: a server that has the runtimes and the Visual C++ runtime gets nothing installed");
        host = new FakeInstallerHost(exePre, work, preWorld) { VisualCpp = false };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == 0 && host.Prerequisites.Count == 1 && host.Prerequisites[0].StartsWith("vc_redist.x64.exe "), "Installer: only what is missing is installed (here the Visual C++ runtime)");
        host = new FakeInstallerHost(exePre, work, preWorld) { NeedsRuntime = true, Signed = false };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == InstallerExit.PrerequisiteFailed && host.Prerequisites.Count == 0 && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("not signed by Microsoft")), "Installer: a prerequisite that is not signed by Microsoft is not run");
        host = new FakeInstallerHost(exePre, work, preWorld) { NeedsRuntime = true, PrerequisiteCode = 5 };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == InstallerExit.PrerequisiteFailed && host.Commands.Count == 0 && host.Errors.Any(message => message.Contains("ended with code 5")), "Installer: a prerequisite that fails stops the installation before anything is touched, and says the code");
        host = new FakeInstallerHost(exePre, work, preWorld) { VisualCpp = false, PrerequisiteCode = 3010 };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == 0 && host.Messages.Any(message => message.Contains("restart")), "Installer: a prerequisite that asks for a restart later does not stop the installation");
        host = new FakeInstallerHost(exePre, work, preWorld) { NeedsRuntime = true };
        code = await new InstallerRunner(host).RunAsync(["--check", "--trusted", signer]);
        check(code == InstallerExit.NoDotnet && host.Prerequisites.Count == 0, "Installer: --check changes nothing on the machine (it does not install prerequisites)");
        host = new FakeInstallerHost(exe, work, preWorld) { NeedsRuntime = true };
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", preRoot, "--no-task", "--no-database", "--no-supervisor"]);
        check(code == InstallerExit.NoDotnet && host.Prerequisites.Count == 0, "Installer: an installer made without prerequisites says what is missing instead of guessing");
        // the real check of the signature (Windows' own Authenticode), on files that exist on every machine that runs these checks
        var realHost = new ConsoleInstallerHost();
        var dotnetExe = realHost.FindDotnet(null);
        if (dotnetExe is not null && OperatingSystem.IsWindows())
        {
            var tampered = Path.Combine(work, "tampered-dotnet.exe");
            File.Copy(dotnetExe, tampered, overwrite: true);
            await using (var stream = new FileStream(tampered, FileMode.Open, FileAccess.ReadWrite)) { stream.Seek(1024, SeekOrigin.Begin); stream.WriteByte(0xAB); }
            check(realHost.IsSignedByMicrosoft(dotnetExe) && !realHost.IsSignedByMicrosoft(tampered) && !realHost.IsSignedByMicrosoft(Path.Combine(AppContext.BaseDirectory, "BlazorStoc.Updater.dll")) && !realHost.IsSignedByMicrosoft(Path.Combine(work, "does-not-exist.exe")),
                "Installer: the signature check of the prerequisites accepts a file signed by Microsoft (dotnet.exe) and refuses a copy that was changed, an unsigned file and a missing one");
        }
        // the uninstaller: the application goes, the data stay (or go too, after a restore package)
        var gone = new World(Path.Combine(work, "uninstall-world"));
        var exeGone = await BuildInstallerAsync(work, await gone.PackAsync(keys, "1.0.0"), "1.0.0", "inst-gone");
        host = new FakeInstallerHost(exeGone, work, gone);
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", gone.Layout.Root, "--no-task", "--no-admin"]);
        Directory.CreateDirectory(Path.Combine(gone.Layout.Root, "mariadb", "data"));
        File.WriteAllText(Path.Combine(gone.Layout.Root, "mariadb", "data", "ibdata1"), "the data");
        var rootGone = gone.Layout.Root;
        host = new FakeInstallerHost(exeGone, work, gone) { Registered = rootGone };
        code = await new InstallerRunner(host).RunAsync(["--uninstall", "--yes"]);
        var left = Directory.Exists(rootGone) ? Directory.EnumerateFileSystemEntries(rootGone).Select(Path.GetFileName).ToArray() : [];
        check(code == 0 && left.SequenceEqual(["mariadb"]) && File.ReadAllText(Path.Combine(rootGone, "mariadb", "data", "ibdata1")) == "the data" && host.Commands.Count == 1 && host.Commands[0] == "uninstall --install-root " + rootGone
            && host.Scripts.Calls.Any(call => call.StartsWith("supervisor.ps1 -Action stop")) && host.Scripts.Calls.Any(call => call.StartsWith("supervisor.ps1 -Action uninstall")) && host.Scripts.Calls.Any(call => call.StartsWith("firewall.ps1 -Action remove -Port "))
            && host.Scripts.Calls.Any(call => call.StartsWith("uninstall-database.ps1 ") && !call.Contains("-RemoveData")) && host.Location.Value is null && host.Output.Contains("The data were kept"),
            $"Installer: --uninstall removes the application, the supervisor, the firewall rule and the database service, keeps the database folder, forgets the installation; exit code {code}");
        check(host.Prerequisites.Count == 0, "Installer: the uninstaller installs no prerequisites");

        // total deletion: the data of the application too (the data root named by the settings of the installation)
        var wipe = new World(Path.Combine(work, "wipe-world"));
        var exeWipe = await BuildInstallerAsync(work, await wipe.PackAsync(keys, "1.0.0"), "1.0.0", "inst-wipe");
        var wipeData = Path.Combine(work, "wipe-data");
        Directory.CreateDirectory(Path.Combine(wipeData, "assets", "database-backups"));
        File.WriteAllText(Path.Combine(wipeData, "application-connection.private.json"), "{}");
        File.WriteAllText(Path.Combine(wipeData, "somebody-elses-file.txt"), "not ours");
        host = new FakeInstallerHost(exeWipe, work, wipe);
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", wipe.Layout.Root, "--no-task", "--no-admin", "--env", "App__DataRoot=" + wipeData]);
        Directory.CreateDirectory(Path.Combine(wipe.Layout.Root, "mariadb", "data"));
        var previousDataRoot = Environment.GetEnvironmentVariable("App__DataRoot");
        try
        {
            host = new FakeInstallerHost(exeWipe, work, wipe) { Registered = wipe.Layout.Root };
            code = await new InstallerRunner(host).RunAsync(["--uninstall", "--remove-data", "--yes"]);
            check(code == 0 && !Directory.Exists(wipe.Layout.Root) && !Directory.Exists(Path.Combine(wipeData, "assets")) && !File.Exists(Path.Combine(wipeData, "application-connection.private.json"))
                && File.Exists(Path.Combine(wipeData, "somebody-elses-file.txt")) && host.Commands[0] == "uninstall --install-root " + wipe.Layout.Root + " --remove-data" && host.Scripts.Calls.Any(call => call.StartsWith("uninstall-database.ps1 ") && call.Contains("-RemoveData")),
                $"Installer: --uninstall --remove-data deletes the installation and the data the installation made, never the other files of the data folder; exit code {code}");
        }
        finally { Environment.SetEnvironmentVariable("App__DataRoot", previousDataRoot); }

        // the restore package that cannot be made stops everything before anything is removed
        var safe = new World(Path.Combine(work, "safe-world"));
        var exeSafe = await BuildInstallerAsync(work, await safe.PackAsync(keys, "1.0.0"), "1.0.0", "inst-safe");
        host = new FakeInstallerHost(exeSafe, work, safe);
        code = await new InstallerRunner(host).RunAsync(["--init", "--no-install", "--yes", "--install-root", safe.Layout.Root, "--no-task", "--no-admin"]);
        host = new FakeInstallerHost(exeSafe, work, safe) { Registered = safe.Layout.Root };
        code = await new InstallerRunner(host).RunAsync(["--uninstall", "--remove-data", "--backup-to", Path.Combine(work, "restore-package"), "--yes"]);
        check(code != 0 && Directory.Exists(safe.Layout.Root) && File.Exists(safe.Layout.SettingsFile) && host.Scripts.Calls.Count == 0 && host.Output.Contains("nothing was removed"), "Installer: when the restore package cannot be made nothing is removed (no version installed to make it)");
        // at a console the folder of the package is asked; --remove-data needs the word STERGE
        host = new FakeInstallerHost(exeSafe, work, safe) { Registered = safe.Layout.Root, Interactive = true };
        host.Answers.Enqueue(Path.Combine(work, "restore-package"));
        host.Answers.Enqueue("nu");
        code = await new InstallerRunner(host).RunAsync(["--uninstall", "--remove-data"]);
        check(code == InstallerExit.Cancelled && host.Questions == 2 && host.Messages.Any(message => message.Contains("restore package")) && host.Commands.Count == 0 && Directory.Exists(safe.Layout.Root), "Installer: at a console --uninstall --remove-data asks for the folder of a restore package and needs STERGE typed; anything else cancels");
        host = new FakeInstallerHost(exeSafe, work, safe) { Registered = safe.Layout.Root };
        code = await new InstallerRunner(host).RunAsync(["--uninstall", "--remove-data"]);
        check(code == InstallerExit.Cancelled && host.Commands.Count == 0, "Installer: without a console and without --yes the uninstaller does not guess");
        check(Throws<ArgumentException>(() => InstallerArguments.Parse(["--remove-data"])) && Throws<ArgumentException>(() => InstallerArguments.Parse(["--uninstall", "--backup-to", "D:\\x"])) && InstallerArguments.Parse(["--uninstall", "--remove-data", "--backup-to", "D:\\x"]).BackupTo == "D:\\x",
            "Installer: --remove-data and --backup-to go with --uninstall (--backup-to with --remove-data)");
        check(PortSelectorChecks(), "PortSelector: the initial port when free, the next free one when not, never the port of the other, and a way out at the end of the range");

        host = new FakeInstallerHost(exe, work, fresh);
        code = await new InstallerRunner(host).RunAsync(["--init", "--yes"]);
        check(code == InstallerExit.BadArguments && host.Commands.Count == 0, "Installer: --init without a folder is refused");
    }

    private static bool AdministratorPromptChecks()
    {
        static IEnumerable<ConsoleKeyInfo> Keys(string text) => text.Select(character => character == '\n' ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false) : character == '<' ? new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false)
            : new ConsoleKeyInfo(character, ConsoleKey.A, false, false, false));
        // a user name too short, then boss, display name (Enter = same), a password too short, a good one whose repetition is wrong, again, the repetition corrected with Backspace
        var queue = new Queue<ConsoleKeyInfo>(Keys("a\nboss\n\nabc\nabcd1234\nabcd1235\nabcd1234\nabcd1235<4\n"));
        var output = new StringWriter();
        var account = new AdministratorPrompt(queue.Dequeue, output).Ask();
        var text = output.ToString();
        return account == new AdministratorAccount("boss", "boss", "abcd1234") && queue.Count == 0 && text.Contains(AdministratorRules.UsernameProblem("a")!) && text.Contains(AdministratorRules.PasswordProblem("abc")!)
            && text.Contains(AdministratorPrompt.Differ) && text.Contains(AdministratorPrompt.Match) && text.Contains("The passwords are not the same") && !text.Contains("abcd1234") && !text.Contains("abcd1235")
            && AdministratorRules.UsernameProblem("ana.pop-1_x") is null && AdministratorRules.UsernameProblem("a b") is not null && AdministratorRules.PasswordProblem("12345678") is null;
    }

    private static bool PortSelectorChecks()
    {
        var none = new FakePorts([]);
        var busy = new FakePorts([5087, 5088, 5089]);
        var top = new FakePorts([65535, 65534]);
        return PortSelector.Choose(5087, [], none) is { Chosen: 5087, Moved: false }
            && PortSelector.Choose(5087, [], busy) is { Chosen: 5090, Moved: true }
            && PortSelector.Choose(5087, [5090], busy).Chosen == 5091
            && PortSelector.Choose(65535, [], top).Chosen == 65533
            && Throws<ArgumentException>(() => PortSelector.Choose(80, [], none)) && Throws<ArgumentException>(() => PortSelector.Choose(70000, [], none))
            && Throws<InvalidOperationException>(() => PortSelector.Choose(5000, [], new FakePorts(Enumerable.Range(1024, 65535 - 1024 + 1).ToHashSet())));
    }

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
        public string? DefaultInstallRoot { get; init; }
        public AdministratorAccount? Administrator { get; init; }
        public bool NeedsRuntime { get; init; }
        public bool VisualCpp { get; init; } = true;
        public bool Signed { get; init; } = true;
        public int PrerequisiteCode { get; init; }
        public List<string> Prerequisites { get; } = [];
        private readonly HashSet<string> installedRuntimes = [];
        public bool VisualCppRuntimeInstalled => VisualCpp;
        public bool IsSignedByMicrosoft(string file) => Signed && File.Exists(file);
        public Task<int> RunPrerequisiteAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var name = Path.GetFileName(file);
            Prerequisites.Add(name + " " + string.Join(' ', arguments));
            if (PrerequisiteCode == 0 || PrerequisiteCode == 3010) installedRuntimes.Add(name.Split('-')[0]);
            return Task.FromResult(PrerequisiteCode);
        }
        public int AdminCode { get; init; }
        public IReadOnlyDictionary<string, string>? AdminEnvironment { get; private set; }
        public AdministratorAccount? AskAdministrator() => Administrator;
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
        public HashSet<int> BusyPorts { get; init; } = [];
        public FakeScripts Scripts { get; } = new();
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

        public string? FindDotnet(string? requested) => NeedsRuntime && !installedRuntimes.Contains("dotnet") ? null : Dotnet;
        public Task<string?> RuntimeProblemAsync(string dotnet, CancellationToken cancellationToken) =>
            Task.FromResult(NeedsRuntime && !(installedRuntimes.Contains("dotnet") && installedRuntimes.Contains("aspnetcore")) ? "The .NET 9 runtime (Microsoft.AspNetCore.App 9.x) is not installed." : RuntimeProblem);

        public string CreateWorkFolder()
        {
            var folder = Path.Combine(work, "installer-work-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            Folders.Add(folder);
            return folder;
        }

        public void DeleteWorkFolder(string folder) => Directory.Delete(folder, recursive: true);

        public async Task<int> RunUpdaterAsync(string dotnet, string updaterDll, IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
        {
            Commands.Add(string.Join(' ', arguments));
            // The account is made by the installed application (a real process): here only what the installer handed over is kept.
            if (arguments[0] == "create-admin") { AdminEnvironment = environment; return AdminCode; }
            var line = arguments.ToList();
            // "init" and "refresh" copy the updater that runs them: here it runs inside the test, so the folder of the payload is named.
            if (line[0] is "init" or "refresh") line.AddRange(["--from", Path.GetDirectoryName(updaterDll)!]);
            var previousOut = Console.Out;
            var previousError = Console.Error;
            Console.SetOut(output);
            Console.SetError(output);
            try { return await Cli.RunAsync([.. line], engines, Location, new FakePorts(BusyPorts), Scripts); }
            finally { Console.SetOut(previousOut); Console.SetError(previousError); }
        }
    }

    private sealed class FakePorts(HashSet<int> busy) : IPortProbe
    {
        public bool IsFree(int port) => !busy.Contains(port);
    }

    private sealed class FakeScripts : IScriptRunner
    {
        public List<string> Calls { get; } = [];
        public int DatabaseExit { get; set; }

        public Task<int> RunAsync(string script, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(Path.GetFileName(script) + " " + string.Join(' ', arguments));
            return Task.FromResult(Path.GetFileName(script) == "provision-database.ps1" ? DatabaseExit : 0);
        }
    }

    private sealed class FakeLocation : IInstallLocationStore
    {
        public string? Value { get; private set; }
        public void Clear() => Value = null;
        public string? Read() => Value;
        public void Write(string installRoot) => Value = Path.GetFullPath(installRoot);
    }
}
