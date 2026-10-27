namespace TwelfthVoice.Api.Storage;

/// <summary>
/// Reads that a screen waits on get a short time budget. A serverless database that has paused takes
/// up to a minute to wake, and nobody should stare at a spinner meanwhile: past the budget the caller
/// gets an empty answer, and the attempt that timed out still wakes the database for next time.
/// </summary>
public static class StorageReads
{
    public static readonly TimeSpan ScreenBudget = TimeSpan.FromSeconds(2);

    public static async Task<IReadOnlyList<StoredMatch>> RecentMatchesOrNoneAsync(
        IMatchStore store, int take, TimeSpan budget, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        try
        {
            // WaitAsync returns on time even if the driver is slow to honour the cancellation.
            return await store.RecentMatchesAsync(take, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<StoredMatch>();
        }
    }
}
