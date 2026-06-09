using Dapper;

namespace QueryLib.Internal;

internal sealed class QueryContext(ISqlDialect dialect)
{
    private int _paramCount;

    internal ISqlDialect Dialect => dialect;
    internal DynamicParameters Parameters { get; } = new();

    internal string AddParam(object? value)
    {
        var name = $"p{_paramCount++}";
        Parameters.Add(name, value);
        return $"@{name}";
    }
}
