using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

/// <summary>The counts of the two validation queues for the current user (zero and "not allowed" for a queue the user may not work).</summary>
public sealed class MariaValidationQueues(IConfiguration configuration, IAccessControl access) : IValidationQueues
{
    public async Task<ValidationQueueCounts> GetAsync(CancellationToken cancellationToken = default)
    {
        var canTickets = await access.HasAsync("mentenanta.validare", cancellationToken).ConfigureAwait(false);
        var canBeneficiaries = await access.HasAsync("beneficiari.validare", cancellationToken).ConfigureAwait(false);
        var tickets = canTickets ? (await MariaActivityReviewService.ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false)).Count : 0;
        var beneficiaries = canBeneficiaries ? (await MariaBeneficiaryCheckService.ReadPendingAsync(configuration, cancellationToken).ConfigureAwait(false)).Count : 0;
        // The open problems of the import (the tab "Probleme import" shows their number, also when it is 0).
        var issues = 0;
        if (canTickets || canBeneficiaries)
        {
            try
            {
                await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
                await using var count = new MySqlConnector.MySqlCommand("SELECT COUNT(*) FROM activity_import_issues WHERE resolved = 0", connection);
                issues = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { issues = 0; }
        }
        return new(tickets, beneficiaries, canTickets, canBeneficiaries, issues);
    }
}

internal sealed class UnavailableValidationQueues : IValidationQueues
{
    public Task<ValidationQueueCounts> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(ValidationQueueCounts.Empty);
}
