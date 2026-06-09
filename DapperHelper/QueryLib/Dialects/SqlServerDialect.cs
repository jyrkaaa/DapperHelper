using System.Text;

namespace QueryLib.Dialects;

public sealed class SqlServerDialect : ISqlDialect
{
    public static readonly SqlServerDialect Instance = new();

    public string EscapeLikeValue(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    public string LikeExpression(string column, string paramRef) =>
        $"{column} LIKE {paramRef}";

    public string Paging(int? take, int? skip)
    {
        if (!take.HasValue && !skip.HasValue) return string.Empty;
        var sb = new StringBuilder();
        sb.Append($" OFFSET {skip ?? 0} ROWS");
        if (take.HasValue) sb.Append($" FETCH NEXT {take.Value} ROWS ONLY");
        return sb.ToString();
    }
}
