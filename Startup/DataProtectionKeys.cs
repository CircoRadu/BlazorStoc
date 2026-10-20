namespace BlazorStoc.Startup;

// Where the keys of the Data Protection system are kept (they protect the NAS password stored in the database). The application and the backup worker read the same
// folder, so a password saved by the application is read by the worker.
internal static class DataProtectionKeys
{
    /// <summary>An explicit App:DataProtectionPath always wins; otherwise the dedicated local asset directory from the handoff document.</summary>
    public static string PathFor(IConfiguration configuration) =>
        configuration["App:DataProtectionPath"]
            ?? AppDataRoot.Combine(configuration, "assets", "data-protection-keys");
}
