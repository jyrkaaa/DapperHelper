using System.Text;

namespace QueryLib.Dialects;

public sealed class PostgresDialect : ISqlDialect
{
    public static readonly PostgresDialect Instance = new();

    public string EscapeLikeValue(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public string LikeExpression(string column, string paramRef) =>
        $"{column} LIKE {paramRef} ESCAPE '\\'";

    public string Paging(int? take, int? skip)
    {
        var sb = new StringBuilder();
        if (take.HasValue) sb.Append($" LIMIT {take.Value}");
        if (skip.HasValue) sb.Append($" OFFSET {skip.Value}");
        return sb.ToString();
    }
}
