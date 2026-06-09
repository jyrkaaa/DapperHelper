namespace QueryLib.Internal;

internal static class SqlHelpers
{
    internal static string CombineWhere(IReadOnlyList<string> parts) =>
        parts.Count == 1
            ? parts[0]
            : string.Join(" AND ", parts.Select(p => $"({p})"));
}
