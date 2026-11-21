using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The download icon of the backup packages table (PROP-0020): every package has one, a package on the server goes straight to the download address, a package only on the NAS is first brought to
// the server (the same checked "fetch" as for a restore) and then downloaded.
public static class BackupDownloadTableChecks
{
    private sealed class FakeBackups(List<BackupPackage> packages) : IDatabaseBackupService
    {
        public Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default, IOperationLockHandle? existingLock = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BackupPackage>>([.. packages]);
        public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRestore : IDatabaseRestoreService
    {
        public Task<RestoreResult> RestoreAsync(string packageFileName, IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeNasSettings(bool enabled) : INasBackupSettingsRepository
    {
        public Task<NasBackupSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NasBackupSettings(@"\nas\backup", "u", true, enabled, null, null, ""));
        public Task SaveAsync(NasBackupInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCopier(List<BackupPackage> packages, string[] onNas) : INasBackupCopier
    {
        public readonly List<string> Fetched = [];
        public Task<NasCopyResult> TestAsync(NasBackupInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<NasCopyResult> CopyMissingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<NasCopyResult> CopyAfterBackupAsync(string? onlyPackage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<NasShareListing> ListShareAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NasShareListing(true, "", [.. onNas.Select(name => new NasShareFile(name, 100, DateTime.UtcNow))]));
        public Task<NasCopyResult> FetchAsync(string fileName, CancellationToken cancellationToken = default)
        {
            Fetched.Add(fileName);
            packages.Add(new BackupPackage(fileName, BackupKind.Manual, 100, DateTime.UtcNow, "x", "Administrator", 1));
            return Task.FromResult(new NasCopyResult(true, 1, 0, 0, ""));
        }
        public Task RecordBackupFailureAsync(string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public static void Run(Action<bool, string> check)
    {
        var packages = new List<BackupPackage> { new("local-1.zip", BackupKind.Manual, 100, DateTime.UtcNow, "x", "Administrator", 1) };
        var nasOnly = BackupNaming.BuildFileName(BackupKind.Manual, new DateTime(2026, 11, 10, 9, 0, 0), "Administrator", "radu");
        var copier = new FakeCopier(packages, [nasOnly, "local-1.zip"]);
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDatabaseBackupService>(new FakeBackups(packages));
        context.Services.AddSingleton<IDatabaseRestoreService>(new FakeRestore());
        context.Services.AddSingleton<INasBackupSettingsRepository>(new FakeNasSettings(true));
        context.Services.AddSingleton<INasBackupCopier>(copier);
        context.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin"));
        var cut = context.Render<BackupPackagesTable>();
        cut.WaitForAssertion(() => { if (cut.FindAll("button.row-icon.download").Count < 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        check(cut.FindAll("button.row-icon.download").Count == 2 && cut.Find("button[aria-label='Descarcă local-1.zip']") is not null, "Backup table: every package has a download icon");
        check(cut.Find($"button[aria-label='Descarcă {nasOnly}']").GetAttribute("title")!.Contains("adus mai întâi de pe NAS", StringComparison.Ordinal), "Backup table: the icon of a package only on the NAS says it is brought to the server first");
        cut.Find("button[aria-label='Descarcă local-1.zip']").Click();
        cut.WaitForAssertion(() => { if (!navigation.Uri.EndsWith("/api/backup/local-1.zip", StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(copier.Fetched.Count == 0, "Backup table: a package on the server is downloaded without touching the NAS");
        cut.Find($"button[aria-label='Descarcă {nasOnly}']").Click();
        cut.WaitForAssertion(() => { if (!navigation.Uri.EndsWith(BackupDownloadRules.Url(nasOnly), StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(copier.Fetched.SequenceEqual([nasOnly]), "Backup table: a package only on the NAS is brought to the server first and then downloaded");
    }
}
