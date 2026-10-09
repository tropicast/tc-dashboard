namespace Tropicast.Dashboard.Api;

/// <summary>One page of results.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page (1-100).</param>
/// <param name="TotalCount">Items on all pages.</param>
internal sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal static class Paging
{
    internal const int DefaultSize = 20;
    internal const int MaxSize = 100;

    internal static Dictionary<string, string[]>? Validate(int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["Pages start at 1."];
        }
        if (pageSize is < 1 or > MaxSize)
        {
            errors["pageSize"] = [$"Use 1 to {MaxSize} items per page."];
        }
        return errors.Count > 0 ? errors : null;
    }
}
