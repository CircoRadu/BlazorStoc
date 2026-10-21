using Microsoft.Win32;

namespace BlazorStoc.Updater;

/// <summary>
/// Where the installation of this server is, so the installer (and every updater command) finds it without being told: HKLM\SOFTWARE\BlazorStoc, value InstallRoot, written by "init".
/// It is in HKLM on purpose: only an administrator can change it, so the ordinary account of the application cannot point the installer at a folder of its own.
/// </summary>
public interface IInstallLocationStore
{
    string? Read();
    void Write(string installRoot);
    /// <summary>Forgets the installation (the uninstaller).</summary>
    void Clear();
}

public sealed class RegistryInstallLocation : IInstallLocationStore
{
    private const string KeyPath = @"SOFTWARE\BlazorStoc";
    private const string ValueName = "InstallRoot";

    public string? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) as string is { Length: > 0 } value ? value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    public void Write(string installRoot)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, Path.GetFullPath(installRoot), RegistryValueKind.String);
    }

    /// <summary>Removes the value (the uninstaller does it).</summary>
    public void Clear()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
