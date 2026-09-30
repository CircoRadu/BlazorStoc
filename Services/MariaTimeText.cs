using System.Globalization;

namespace BlazorStoc.Services;

// Subtask 2.6 (Task 2): every "_utc" column in the real migrated MariaDB schema (migration\schema-mariadb.sql,
// read in this cycle) is TEXT (LONGTEXT or VARCHAR(40)/VARCHAR(10)), never a native DATETIME - the migration kept
// the exact SQLite text values. The 18 triggers already installed on the delivered database write that text with
// CONCAT(DATE_FORMAT(UTC_TIMESTAMP(3),'%Y-%m-%dT%H:%i:%s.'),LEFT(DATE_FORMAT(UTC_TIMESTAMP(3),'%f'),3),'Z') -
// millisecond precision, ISO-8601, always ending in 'Z'. Every Maria repository must read/write the same fixed
// width format (never a native MySqlConnector DateTime parameter/reader, which would use a different separator/
// precision and break both round-tripping and the lexicographic ordering that utf8mb4_nopad_bin comparisons and
// the app's own ORDER BY/range queries rely on).
internal static class MariaTimeText
{
    // Same expression the database triggers use for "now", so app-written and trigger-written timestamps compare
    // and sort identically under utf8mb4_nopad_bin.
    public const string NowSql = "CONCAT(DATE_FORMAT(UTC_TIMESTAMP(3),'%Y-%m-%dT%H:%i:%s.'),LEFT(DATE_FORMAT(UTC_TIMESTAMP(3),'%f'),3),'Z')";

    public static string Format(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public static DateTime Parse(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) is { Kind: DateTimeKind.Utc } utc
            ? utc : DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeKind.Utc);

    public static DateTime? ParseOrNull(string? text) =>
        string.IsNullOrEmpty(text) ? null : Parse(text);

    // Bug A15 (Task 2, subtask 2.11): "now" truncated to the exact millisecond precision that Format/Parse
    // round-trip through the database. Records such as Project/ProjectObservation carry CreatedAtUtc/UpdatedAtUtc
    // in their equality (they are plain C# records), and ProjectRules.CheckCurrent compares the in-memory
    // "original" object against a row just re-read from the database with `current != original`. DateTime.UtcNow
    // keeps sub-millisecond ticks that Format() then discards when writing the row; a caller that builds its
    // in-memory object straight from an untruncated DateTime.UtcNow therefore never matches what a later read of
    // the same row parses back, and every Update/Delete on a project or observation intermittently (whenever the
    // sub-millisecond ticks are non-zero) fails its optimistic-concurrency check with a false "modified or deleted
    // in the meantime" error - which for Delete happens inside the same archive+delete transaction and looks, from
    // outside, like the whole transaction silently rolled back. Use this instead of DateTime.UtcNow wherever the
    // resulting timestamp ends up on an object that MariaTimeText.Format will also persist and that is later
    // compared by record equality (Project/ProjectObservation create and update in MariaProjectRepository).
    public static DateTime Now() => Parse(Format(DateTime.UtcNow));
}

// Subtask 2.7 (Task 2): where MariaDB-mode file stores keep their files on disk, shared by every store so they
// never disagree with each other about the archive directory. Never falls back to the app's own SQLite-mode
// "data/..." directories (App:ProductImagesPath / App:ProjectFilesPath / App:ArchiveFilesPath, used only by
// SqliteLocalStore in demo mode) - MariaDB mode has its own separate, explicit key for each directory, all rooted
// under Database:MariaAssetsRoot, defaulting to the local asset layout from
// docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md section 7 (%LOCALAPPDATA%\BlazorStoc-MariaDB\assets).
internal static class MariaAssetPaths
{
    private static string AssetsRoot(IConfiguration configuration) =>
        configuration["Database:MariaAssetsRoot"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB", "assets");

    public static string ProductImages(IConfiguration configuration) => Path.GetFullPath(
        configuration["Database:MariaProductImagesPath"] ?? Path.Combine(AssetsRoot(configuration), "product-images"));

    public static string ProjectFiles(IConfiguration configuration) => Path.GetFullPath(
        configuration["Database:MariaProjectFilesPath"] ?? Path.Combine(AssetsRoot(configuration), "project-files"));

    public static string ServicePhotos(IConfiguration configuration) => Path.GetFullPath(
        configuration["Database:MariaServicePhotosPath"] ?? Path.Combine(AssetsRoot(configuration), "service-photos"));

    // Shared by every MariaDB-mode store that archives a file (product images, project files): one directory,
    // never one per store, so ArchiveFileSafety's cleanup-marker retry logic always looks in the same place.
    public static string ArchiveFiles(IConfiguration configuration) => Path.GetFullPath(
        configuration["Database:MariaArchiveFilesPath"] ?? Path.Combine(AssetsRoot(configuration), "archive-files"));

    // Subtask 2.1/2.2 (Task 2): database backup packages (.zip + manifest) and the shared operation lock file live
    // here, outside wwwroot and outside every path served as a static file, like every other asset directory above.
    public static string DatabaseBackups(IConfiguration configuration) => Path.GetFullPath(
        configuration["Database:MariaBackupFilesPath"] ?? Path.Combine(AssetsRoot(configuration), "database-backups"));

    // Distribution installed next to this asset root (docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md section 3); the
    // "mariadb-dump" executable used to export the live database (Subtask 2.2) ships in its bin/ folder.
    public static string MariaDumpExecutable(IConfiguration configuration) =>
        configuration["Database:MariaDumpExecutablePath"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB",
            "mariadb-11.4.13-winx64", "bin", "mariadb-dump.exe");

    // Subtask 3.4 (Task 3): the "mariadb" CLI client (same bin/ folder as mariadb-dump.exe above) used to import a
    // package's dump.sql into the temporary restore schema by piping the file into its standard input.
    public static string MariaClientExecutable(IConfiguration configuration) =>
        configuration["Database:MariaClientExecutablePath"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB",
            "mariadb-11.4.13-winx64", "bin", "mariadb.exe");
}

// Subtask 2.11 (Task 2): every write-capable Maria* repository refuses to write unless the configured database
// name matches the one real production name ("BlazorStoc"), as a defense-in-depth guard against ever writing to
// some other/legacy database by misconfiguration - this must never be loosened for a real deployment. Integration
// testing (Subtask 2.11) legitimately needs a differently-named, isolated database though (never the delivery one),
// so the expected name is itself configurable via Database:ExpectedName, defaulting to "BlazorStoc" when unset:
// a real deployment that never sets it keeps the exact original behavior (Database:Name must literally be
// "BlazorStoc"), while an isolated test configuration can deliberately set BOTH Database:Name and
// Database:ExpectedName to the same non-default value to opt in, explicitly, in its own private config file only -
// never as a silent default and never committed to appsettings.json/Git.
internal static class MariaDatabaseGuard
{
    public static bool IsAllowedDatabase(IConfiguration configuration) =>
        string.Equals(configuration["Database:Name"] ?? "BlazorStoc", configuration["Database:ExpectedName"] ?? "BlazorStoc", StringComparison.Ordinal);
}
