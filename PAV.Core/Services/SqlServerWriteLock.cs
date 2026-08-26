using Microsoft.Data.SqlClient;

namespace PAV.Core.Services;

/// <summary>
/// SQL Server handles multi-user locking. Retry only deadlocks / lock timeouts.
/// Do not serialise all writes — that would undo the reason for SQL Server.
/// Permanent errors (login, missing db, unique) are not retried.
/// </summary>
public sealed class SqlServerWriteLock : IWriteLock
{
    public const int MaxAttempts = 5;

    public async Task<T> WriteAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        for (var i = 1; i <= MaxAttempts; i++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (i < MaxAttempts && IsRetryable(ex))
            {
                await Task.Delay(80 * i, ct);
            }
        }
        return await action();
    }

    public async Task WriteAsync(Func<Task> action, CancellationToken ct = default)
    {
        await WriteAsync(async () =>
        {
            await action();
            return 0;
        }, ct);
    }

    public static bool IsRetryable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && sql.Number is 1205 or 1222 or -2)
                return true;
        }
        return false;
    }
}
