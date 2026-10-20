using System.Security.AccessControl;
using System.Security.Principal;

namespace BlazorStoc.Updater;

/// <summary>
/// The folders that hold what only an administrator may read or change (the signers the server trusts, the password of the migrator account): inheritance is cut and only
/// Administrators and SYSTEM keep access. Done only by an administrator (anyone else would lock himself out of the folder he just made).
/// </summary>
public static class Acl
{
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>True when the folder was restricted; false when it was left as it is (not Windows, or not run by an administrator).</summary>
    public static bool RestrictToAdministrators(string folder) => Apply(folder, users: null);

    /// <summary>
    /// The whole installation (docs/PROPUNERE_ACTUALIZARE.md, section 7): the folder of an installation made under C:\ inherits "Authenticated Users: modify", which would let any local
    /// user repoint current.txt or change a release, that is, run his code as the account of the application. So the root keeps Administrators and SYSTEM in control and gives Users only
    /// read and execute (the application runs from releases/); updater/ and private/ stay for administrators alone; updates/staged (the uploads of the application), logs/ and
    /// state/supervisor (written by the supervisor) are the only places Users may write. Returns false when nothing was changed (not Windows, or not run by an administrator).
    /// </summary>
    public static bool ProtectInstallation(InstallLayout layout)
    {
        if (!OperatingSystem.IsWindows() || !IsAdministrator()) return false;
        var supervisorState = Path.Combine(layout.StateFolder, "supervisor");
        layout.EnsureFolders();
        Directory.CreateDirectory(supervisorState);
        Apply(layout.Root, FileSystemRights.ReadAndExecute);
        Apply(layout.UpdaterFolder, users: null);
        Apply(layout.PrivateFolder, users: null);
        foreach (var writable in new[] { layout.StagedFolder, layout.LogsFolder, supervisorState }) Apply(writable, FileSystemRights.Modify);
        return true;
    }

    // Administrators and SYSTEM in full control; Users with the rights given (null = nothing); inheritance from the parent cut.
    private static bool Apply(string folder, FileSystemRights? users)
    {
        if (!OperatingSystem.IsWindows() || !IsAdministrator()) return false;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        void Grant(WellKnownSidType sid, FileSystemRights rights) => security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Grant(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        Grant(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        if (users is { } rights) Grant(WellKnownSidType.BuiltinUsersSid, rights);
        new DirectoryInfo(folder).SetAccessControl(security);
        return true;
    }
}
