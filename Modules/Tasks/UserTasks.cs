using BlazorStoc.Services;

namespace BlazorStoc.Modules.Tasks;

// Tasks given by an administrator to ONE user on a beneficiary (docs/PROPUNERE_TASKURI_BENEFICIAR.md). The records, the rules and the contracts of the module; the
// MariaDB implementation is MariaUserTaskRepository / MariaUserTaskFileStore, the circuit breaker is in GuardedTasks.cs.

public enum UserTaskStatus
{
    /// <summary>"În viitor": given, not started.</summary>
    Future = 1,
    /// <summary>"În lucru".</summary>
    InProgress = 2,
    /// <summary>"Finalizat": the only status that is not active.</summary>
    Done = 3
}

public sealed class UserTaskOperationException(string message) : Exception(message);

/// <summary>A task with the names of its beneficiary and user. FileCount is the number of attached files.</summary>
public sealed record UserTask(int Id, int BeneficiaryId, string BeneficiaryName, int AssignedUserId, string AssignedUserName, bool AssignedUserActive,
    string Title, string Description, UserTaskStatus Status, DateOnly? DueDate, DateTime CreatedUtc, string CreatedBy,
    DateTime? AcceptedUtc, string? AcceptedBy, DateTime? FinishedUtc, long Version, int FileCount)
{
    /// <summary>Every task that is not finished is active.</summary>
    public bool IsActive => Status != UserTaskStatus.Done;
    /// <summary>False while the user did not take the task over: a new or moved task (shown on a red background).</summary>
    public bool IsAccepted => AcceptedUtc is not null;
}

/// <summary>A user a task can be given to.</summary>
public sealed record UserTaskUser(int Id, string Username, string DisplayName, bool IsActive)
{
    public string Name => DisplayName.Trim().Length > 0 ? DisplayName.Trim() : Username;
}

public sealed class UserTaskInput
{
    public int BeneficiaryId { get; set; }
    public int AssignedUserId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public UserTaskStatus Status { get; set; } = UserTaskStatus.Future;
    public DateOnly? DueDate { get; set; }

    public static UserTaskInput From(UserTask task) => new()
    {
        BeneficiaryId = task.BeneficiaryId, AssignedUserId = task.AssignedUserId, Title = task.Title, Description = task.Description,
        Status = task.Status, DueDate = task.DueDate
    };

    /// <summary>The input cleaned (text trimmed); throws <see cref="UserTaskOperationException"/> for the first problem.</summary>
    public UserTaskInput Validated()
    {
        if (UserTaskRules.ValidationError(this) is { } error) throw new UserTaskOperationException(error);
        return new()
        {
            BeneficiaryId = BeneficiaryId, AssignedUserId = AssignedUserId, Title = Title.Trim(), Description = Description.Trim(), Status = Status, DueDate = DueDate
        };
    }
}

/// <summary>What needs the attention of a user about a task: not taken over yet, past its deadline, or close to it.</summary>
public sealed record UserTaskAlert(UserTask Task, bool NotAccepted, bool Overdue, bool DueSoon, int? DaysLeft);

public static class UserTaskRules
{
    public const int TitleMaxLength = 150, DescriptionMaxLength = 4000, DefaultWarningDays = 3, MaxWarningDays = 60;
    public const string ConcurrentMessage = "Taskul s-a schimbat între timp. Actualizează lista.";
    public const string SettingWarningDays = "warning_days";

    public static string StatusLabel(UserTaskStatus status) => status switch
    {
        UserTaskStatus.Future => "În viitor",
        UserTaskStatus.InProgress => "În lucru",
        UserTaskStatus.Done => "Finalizat",
        _ => ""
    };

    public static readonly IReadOnlyList<UserTaskStatus> Statuses = [UserTaskStatus.Future, UserTaskStatus.InProgress, UserTaskStatus.Done];

    public static string? ValidationError(UserTaskInput input)
    {
        if (input.BeneficiaryId <= 0) return "Alege beneficiarul.";
        if (input.AssignedUserId <= 0) return "Alege utilizatorul căruia îi este dat taskul.";
        var title = input.Title.Trim();
        if (title.Length == 0) return "Titlul este obligatoriu.";
        if (title.Length > TitleMaxLength) return $"Titlul poate avea cel mult {TitleMaxLength} de caractere.";
        var description = input.Description.Trim();
        if (description.Length == 0) return "Descrierea este obligatorie.";
        if (description.Length > DescriptionMaxLength) return $"Descrierea poate avea cel mult {DescriptionMaxLength} de caractere.";
        if (!Enum.IsDefined(input.Status)) return "Starea taskului nu este validă.";
        return null;
    }

    public static string WarningDaysError(int days) =>
        days < 1 || days > MaxWarningDays ? $"Numărul de zile trebuie să fie între 1 și {MaxWarningDays}." : "";

    public static void CheckCurrent(long currentVersion, UserTask original)
    {
        if (currentVersion != original.Version) throw new UserTaskOperationException(ConcurrentMessage);
    }

    /// <summary>
    /// The tasks of a user that need attention on the given day: the unfinished ones not taken over, past their deadline, or due within <paramref name="warningDays"/> days.
    /// Overdue first (the oldest deadline first), then the ones not taken over (the newest first), then the ones due soon (the nearest first).
    /// </summary>
    public static IReadOnlyList<UserTaskAlert> Alerts(IEnumerable<UserTask> tasks, DateOnly today, int warningDays) =>
        [.. tasks.Where(task => task.IsActive)
            .Select(task =>
            {
                int? days = task.DueDate is { } due ? due.DayNumber - today.DayNumber : null;
                return new UserTaskAlert(task, !task.IsAccepted, days < 0, days >= 0 && days <= warningDays, days);
            })
            .Where(alert => alert.NotAccepted || alert.Overdue || alert.DueSoon)
            .OrderBy(alert => alert.Overdue ? 0 : alert.NotAccepted ? 1 : 2)
            .ThenBy(alert => alert.Overdue || alert.DueSoon ? alert.DaysLeft : 0)
            .ThenByDescending(alert => alert.Task.CreatedUtc)];

    public static string DueText(DateOnly? due) => due is { } value ? StockMovementRules.DisplayDate(value) : "—";

    public static string Target(UserTask task) => $"#{task.Id} · {task.Title} · {task.BeneficiaryName}";

    /// <summary>The fields of a task for the journal (creation and deletion).</summary>
    public static string Identification(UserTask task) => AuditDetails.Identification(
        ("Beneficiar", task.BeneficiaryName), ("Utilizator", task.AssignedUserName), ("Titlu", task.Title), ("Stare", StatusLabel(task.Status)),
        ("Termen", DueText(task.DueDate)), ("Descriere", task.Description));
}

/// <summary>The files of a task may be of any type (unlike the other attached files). A type the application knows (image, PDF, Word, Excel, text) is kept as sniffed from the bytes; any other is stored as
/// "application/octet-stream" and is only ever downloaded, never shown inline.</summary>
public static class UserTaskFileRules
{
    public const string Unknown = "application/octet-stream";

    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf", "text/plain", "text/csv"
    };

    public static string ContentType(byte[] content, string declaredContentType, string fileName)
    {
        if (content.Length == 0) throw new UserTaskOperationException("Fișierul este gol.");
        if (content.LongLength > EntityFileRules.MaximumBytes) throw new UserTaskOperationException("Fișierul poate avea cel mult 20 MB.");
        try { return EntityFileRules.DetectContentType(content, declaredContentType, fileName); }
        catch (WorkPointOperationException) { return Unknown; }
    }

    /// <summary>True for the types the browser may display inside the application (the rest is offered for download only).</summary>
    public static bool CanShowInline(string contentType) => InlineTypes.Contains(contentType);
}

public interface IUserTaskRepository
{
    /// <summary>Every task (taskuri.view), the newest first.</summary>
    Task<IReadOnlyList<UserTask>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserTask>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    /// <summary>The tasks addressed to the signed-in user (no permission key needed).</summary>
    Task<IReadOnlyList<UserTask>> GetMineAsync(CancellationToken cancellationToken = default);
    /// <summary>One task; null when it does not exist or is not addressed to the signed-in user and the user lacks taskuri.view.</summary>
    Task<UserTask?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserTaskUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default);
    Task<UserTask> CreateAsync(UserTaskInput input, CancellationToken cancellationToken = default);
    /// <summary>Changes title, description, deadline, status and user (the beneficiary stays); moving the task to another user makes it "not taken over" again.</summary>
    Task<UserTask> UpdateAsync(UserTask original, UserTaskInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(UserTask original, string reason, CancellationToken cancellationToken = default);
    /// <summary>The user of the task, or whoever holds taskuri.edit. The user changing the status takes the task over too.</summary>
    Task<UserTask> SetStatusAsync(UserTask original, UserTaskStatus status, CancellationToken cancellationToken = default);
    /// <summary>The user of the task takes it over (the red background goes away).</summary>
    Task<UserTask> AcceptAsync(UserTask original, CancellationToken cancellationToken = default);
    /// <summary>Days before a deadline from which the user is warned (global setting, default 3); any signed-in user may read it.</summary>
    Task<int> GetWarningDaysAsync(CancellationToken cancellationToken = default);
    Task SetWarningDaysAsync(int days, CancellationToken cancellationToken = default);
}

public interface IUserTaskFiles
{
    Task<IReadOnlyList<EntityFile>> GetAsync(int taskId, CancellationToken cancellationToken = default);
    Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default);
    Task<EntityFile> AddAsync(int taskId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default);
    /// <summary>The user of the task deletes only the files they uploaded; whoever holds taskuri.edit deletes any.</summary>
    Task DeleteAsync(int fileId, CancellationToken cancellationToken = default);
}
