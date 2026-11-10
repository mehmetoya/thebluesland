using System.Data.Common;

namespace TheBluesland.WeeklyNewsletter.Reporting;

/// <summary>
/// EF Core's Npgsql execution strategy wraps a failed connection in an
/// <see cref="InvalidOperationException"/> ("likely due to a transient failure"), so catching
/// <see cref="DbException"/> alone misses an unreachable database on EF queries.
/// </summary>
public static class DatabaseErrors
{
    public static DbException? Find(Exception exception) =>
        exception as DbException ?? (exception as InvalidOperationException)?.InnerException as DbException;

    /// <summary>Type and message only - no stack trace in the public Actions log.</summary>
    public static string Describe(DbException exception) => $"{exception.GetType().Name}: {exception.Message}";
}
