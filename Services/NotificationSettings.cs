using System.Globalization;

namespace BlazorStoc.Services;

// The clean-up of old resolved notifications (Settings → Notifications → "Setări notificări"). Off by default: resolved
// notifications then pile up. With the switch on, the notifications resolved more than PurgeMonths months ago are deleted,
// once a day together with the evaluation. Version -1 means "no row stored yet" (the defaults).
public sealed record NotificationSettings(bool PurgeEnabled, int PurgeMonths, DateTime? LastPurgeUtc, long Version)
{
    public static NotificationSettings Default { get; } = new(false, NotificationPurgeRules.DefaultMonths, null, -1);
}

// What a clean-up would remove: the resolved notifications older than the limit date, except the ones that must stay.
public sealed record NotificationPurgePlan(DateOnly Cutoff, IReadOnlyList<ExpiryNotification> Removable)
{
    public int Count => Removable.Count;
}

// The stored settings after a save, and what the clean-up that followed removed (Cutoff is null when none ran).
public sealed record NotificationSettingsSaved(NotificationSettings Settings, int Removed, DateOnly? Cutoff);

public static class NotificationPurgeRules
{
    public const int MinMonths = 1;
    public const int MaxMonths = 60;
    public const int DefaultMonths = 12;

    public static bool IsValidMonths(int months) => months is >= MinMonths and <= MaxMonths;

    // The limit date: a notification resolved before it is old enough to be removed.
    public static DateOnly Cutoff(DateOnly today, int months) => today.AddMonths(-months);

    // The day of the resolution in the local time zone (the resolution instant is stored in UTC).
    public static DateOnly ResolvedDay(ExpiryNotification notification, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(notification.ResolvedUtc ?? DateTime.UtcNow, DateTimeKind.Utc), zone));

    // "au fost eliminate din baza de date N notificări rezolvate mai vechi de dd.mm.yyyy", as written in the journal.
    public static string RemovedText(int count, DateOnly cutoff) => Sentence(count == 1 ? "a fost eliminată" : "au fost eliminate", count, cutoff);

    // What the confirmation says before a clean-up is switched on or shortened.
    public static string WillRemoveText(int count, DateOnly cutoff) => Sentence(count == 1 ? "va fi eliminată" : "vor fi eliminate", count, cutoff);

    private static string Sentence(string verb, int count, DateOnly cutoff) =>
        $"{verb} din baza de date {count.ToString(CultureInfo.InvariantCulture)} " +
        $"{(count == 1 ? "notificare rezolvată mai veche" : "notificări rezolvate mai vechi")} de {StockMovementRules.DisplayDate(cutoff)}";

    public static string Months(int months) => months == 1 ? "1 lună" : $"{months.ToString(CultureInfo.InvariantCulture)} luni";

    // Whether the daily run is due: enabled, and no run yet on today's local day.
    public static bool IsDue(NotificationSettings settings, DateOnly today, TimeZoneInfo zone) =>
        settings.PurgeEnabled &&
        (settings.LastPurgeUtc is not { } last ||
         DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(last, DateTimeKind.Utc), zone)) < today);

    public static IEnumerable<AuditChange> Changes(NotificationSettings before, bool enabled, int months) =>
        new AuditChange[]
        {
            new("Ștergerea notificărilor rezolvate", before.PurgeEnabled ? "activă" : "oprită", enabled ? "activă" : "oprită"),
            new("Vechime (luni)", before.PurgeMonths.ToString(CultureInfo.InvariantCulture), months.ToString(CultureInfo.InvariantCulture))
        }.Where(change => change.Before != change.After);
}
