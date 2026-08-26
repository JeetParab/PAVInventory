using Microsoft.Data.Sqlite;

namespace PAV.Core.Services;

/// <summary>
/// Serialises writes in this process. SQLite file locks handle other PCs.
/// Busy/locked errors are retried — normal on a network share.
/// </summary>
public sealed class SqliteWriteLock : IWriteLock
{
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public async Task<T> WriteAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await WithRetry(action, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task WriteAsync(Func<Task> action, CancellationToken ct = default)
    {
        await WriteAsync(async () =>
        {
            await action();
            return 0;
        }, ct);
    }

    private static async Task<T> WithRetry<T>(Func<Task<T>> action, CancellationToken ct)
    {
        const int attempts = 8;
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (i < attempts && IsBusy(ex))
            {
                await Task.Delay(150 * i, ct);
            }
        }

        return await action();
    }

    private static bool IsBusy(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqliteException s && (s.SqliteErrorCode is 5 or 6))
                return true;
        }
        return false;
    }
}
