using System.Globalization;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Announcements;

// Anunturi: the administrator (Setari -> Anunturi) writes announcements with formatted text and pictures, chooses the audience (everybody or selected users) and publishes them;
// after the sign-in every user sees the active announcements he did not see yet in a window (AnnouncementPopup). A mandatory announcement comes at every sign-in until "Am citit".

public static class AnnouncementRules
{
    public const string StateDraft = "draft", StateActive = "activ", StateArchived = "arhivat";
    public const string AudienceAll = "all", AudienceSelected = "selected";
    public const int MaxTitle = 200, MaxBodyChars = 200_000, MaxUsers = 500;
    public const int MaxZipBytes = 40 * 1024 * 1024, MaxZipEntries = 60, MaxZipUnpackedBytes = 80 * 1024 * 1024;
    public const string ImageUrlPrefix = "/media/anunturi/";

    public static string StateLabel(string state, bool expired) => state switch
    {
        StateDraft => "Draft",
        StateActive when expired => "Arhivat (expirat)",
        StateActive => "Activ",
        _ => "Arhivat"
    };

    /// <summary>The user name with the first letter capital, as the window of the users shows who published.</summary>
    public static string Capitalize(string? name) => string.IsNullOrEmpty(name) ? "" : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];

    public static string ImageUrl(int id) => ImageUrlPrefix + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>The date shown and typed by people is dd.MM.yyyy; the stored form is yyyy-MM-dd (empty = no end).</summary>
    public static bool TryParseDisplayDate(string? text, out string stored)
    {
        stored = "";
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
        stored = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    public static string DisplayDate(string stored) =>
        DateTime.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "";

    public static bool IsExpired(string state, string validUntil, DateTime todayLocal) =>
        state == StateActive && validUntil.Length > 0 && string.CompareOrdinal(validUntil, todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0;
}

public sealed class AnnouncementException(string message) : Exception(message), IExpectedAnswer;

/// <summary>What the editor sends: Id 0 = a new announcement. ValidUntil is dd.MM.yyyy or empty.</summary>
public sealed record AnnouncementInput(int Id, string Title, string BodyHtml, bool Mandatory, string Audience, IReadOnlyList<string> Users, string ValidUntil);

public sealed record AnnouncementRow(int Id, string Title, bool Mandatory, string Audience, int SelectedUsers, string State, string ValidUntil, bool Expired, int PublicationNo,
    string CreatedBy, DateTime CreatedUtc, DateTime? PublishedUtc, DateTime? UpdatedUtc, int Seen, int Acknowledged)
{
    public string StateText => AnnouncementRules.StateLabel(State, Expired);
    public string AudienceText => Audience == AnnouncementRules.AudienceAll ? "Toți utilizatorii" : $"{SelectedUsers} selectați";
}

public sealed record AnnouncementDetail(int Id, string Title, string BodyHtml, bool Mandatory, string Audience, IReadOnlyList<string> Users, string ValidUntil, string State, int PublicationNo, string PublishedBy = "", DateTime? PublishedUtc = null);

/// <summary>An announcement shown to a user: the body is already cleaned.</summary>
public sealed record AnnouncementView(int Id, string Title, string BodyHtml, bool Mandatory, string PublishedBy = "", DateTime? PublishedUtc = null);

public sealed record AnnouncementUserChoice(string UserName, string DisplayName);

/// <summary>Who saw an announcement: the user, when the window closed over it (general) or "Am citit" was pressed (mandatory), and whether he confirmed the reading.</summary>
public sealed record AnnouncementSeen(string UserName, string DisplayName, DateTime? SeenUtc, bool Acknowledged);

public sealed record AnnouncementImageContent(string ContentType, byte[] Content);

public sealed record AnnouncementUploaded(int Id, string Url);

public sealed record AnnouncementImportResult(int Id, IReadOnlyList<string> Warnings);

public sealed record AnnouncementZip(string FileName, byte[] Content);

public interface IAnnouncementService
{
    // --- every signed-in user ---
    /// <summary>The active announcements of the signed-in user that wait for him (mandatory first); the preference "do not show" leaves only the mandatory ones.</summary>
    Task<IReadOnlyList<AnnouncementView>> GetPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>A normal announcement is marked seen when the window is closed.</summary>
    Task MarkSeenAsync(int announcementId, CancellationToken cancellationToken = default);
    /// <summary>"Am citit" on a mandatory announcement: it does not come again for this user.</summary>
    Task AcknowledgeAsync(int announcementId, CancellationToken cancellationToken = default);
    Task<bool> GetHideNormalAsync(CancellationToken cancellationToken = default);
    Task SetHideNormalAsync(bool hide, CancellationToken cancellationToken = default);
    Task<AnnouncementImageContent?> GetImageAsync(int imageId, CancellationToken cancellationToken = default);

    // --- setari-anunturi.view / .edit ---
    Task<IReadOnlyList<AnnouncementRow>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AnnouncementSeen>> GetSeenByAsync(int id, CancellationToken cancellationToken = default);
    Task<AnnouncementDetail?> GetAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Saves a new announcement as a draft, or the changes of an existing one (its state does not change). Returns the identifier.</summary>
    Task<int> SaveAsync(AnnouncementInput input, CancellationToken cancellationToken = default);
    /// <summary>Saves and publishes; for an announcement that was published before, everybody targeted receives it again (the seen/read state is erased).</summary>
    Task<int> PublishAsync(AnnouncementInput input, CancellationToken cancellationToken = default);
    /// <summary>Activates an announcement as it is saved (a draft or an archived one): it is published, and who had seen it receives it again. Refused when the date "valabil pana" has passed or the audience is empty.</summary>
    Task ActivateAsync(int id, CancellationToken cancellationToken = default);
    Task ArchiveAsync(int id, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<AnnouncementUploaded> UploadImageAsync(ReportImage image, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AnnouncementUserChoice>> GetUserChoicesAsync(CancellationToken cancellationToken = default);
    /// <summary>A ZIP (anunt.json, continut.html, imagini/) becomes a new draft.</summary>
    Task<AnnouncementImportResult> ImportZipAsync(Stream zip, CancellationToken cancellationToken = default);
    Task<AnnouncementZip> ExportZipAsync(int id, CancellationToken cancellationToken = default);
}
