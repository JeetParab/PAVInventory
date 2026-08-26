namespace PAV.Core.Services;

/// <summary>
/// Serialises or retries writes. SQLite needs a process lock + busy retry.
/// SQL Server uses its own locks; this only retries deadlocks.
/// </summary>
public interface IWriteLock
{
    Task<T> WriteAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
    Task WriteAsync(Func<Task> action, CancellationToken ct = default);
}
