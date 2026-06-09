using System.Linq.Expressions;
using System.Text;
using QueryLib.Internal;

namespace QueryLib;

public sealed class JoinedQueryBuilder<TMain, TJoin>
{
    private readonly string _mainTable;
    private readonly ISqlDialect _dialect;

    private readonly Dictionary<Type, string> _aliasMap = new() { [typeof(TMain)] = "t0" };

    private readonly record struct JoinEntry(string JoinType, string Table, string Alias, string OnClause);
    private readonly List<JoinEntry> _joins = [];

    // Predicates captured as lazy builders so any entity type can be stored without generics
    private readonly List<Func<QueryContext, string>> _whereDelegates = [];
    private readonly List<string> _selectCols = [];
    private readonly List<(string QualifiedCol, bool Desc)> _orderBys = [];

    private bool _distinct;
    private int? _take;
    private int? _skip;

    internal JoinedQueryBuilder(
        QueryBuilder<TMain> source, string joinType, string joinTable, string onClause)
    {
        _mainTable = source.Table;
        _dialect = source.Dialect;
        _distinct = source.IsDistinct;
        _take = source.TakeCount;
        _skip = source.SkipCount;

        foreach (var col in source.SelectCols)
            _selectCols.Add($"t0.{col}");

        foreach (var (col, desc) in source.OrderBys)
            _orderBys.Add(($"t0.{col}", desc));

        foreach (var pred in source.Predicates)
        {
            var p = pred;
            _whereDelegates.Add(ctx => new ExpressionParser(ctx, "t0").Parse(p));
        }

        _joins.Add(new JoinEntry(joinType, joinTable, "t1", onClause));
        _aliasMap[typeof(TJoin)] = "t1";
    }

    public JoinedQueryBuilder<TMain, TJoin> Distinct() { _distinct = true; return this; }
    public JoinedQueryBuilder<TMain, TJoin> Take(int count) { _take = count; return this; }
    public JoinedQueryBuilder<TMain, TJoin> Skip(int count) { _skip = count; return this; }

    // --- Where ---

    // Compiler picks these two overloads for TMain / TJoin without needing a type argument
    public JoinedQueryBuilder<TMain, TJoin> Where(Expression<Func<TMain, bool>> predicate)
        => AddWhere(predicate, "t0");

    public JoinedQueryBuilder<TMain, TJoin> Where(Expression<Func<TJoin, bool>> predicate)
        => AddWhere(predicate, "t1");

    // For any additional joined table
    public JoinedQueryBuilder<TMain, TJoin> Where<T>(Expression<Func<T, bool>> predicate)
        => AddWhere(predicate, GetAlias(typeof(T)));

    private JoinedQueryBuilder<TMain, TJoin> AddWhere<T>(Expression<Func<T, bool>> predicate, string alias)
    {
        var p = predicate;
        var a = alias;
        _whereDelegates.Add(ctx => new ExpressionParser(ctx, a).Parse(p));
        return this;
    }

    // --- Select ---

    public JoinedQueryBuilder<TMain, TJoin> Select(params Expression<Func<TMain, object?>>[] columns)
        => AddSelect(columns, "t0");

    public JoinedQueryBuilder<TMain, TJoin> SelectJoin(params Expression<Func<TJoin, object?>>[] columns)
        => AddSelect(columns, "t1");

    public JoinedQueryBuilder<TMain, TJoin> SelectFrom<T>(params Expression<Func<T, object?>>[] columns)
        => AddSelect(columns, GetAlias(typeof(T)));

    private JoinedQueryBuilder<TMain, TJoin> AddSelect<T>(
        Expression<Func<T, object?>>[] columns, string alias)
    {
        foreach (var col in columns)
            _selectCols.Add($"{alias}.{ColumnResolver.ExtractColumnName(col)}");
        return this;
    }

    // --- OrderBy ---

    public JoinedQueryBuilder<TMain, TJoin> OrderBy<TProp>(
        Expression<Func<TMain, TProp>> selector, bool descending = false)
        => AddOrderBy(selector, "t0", descending);

    public JoinedQueryBuilder<TMain, TJoin> OrderByJoin<TProp>(
        Expression<Func<TJoin, TProp>> selector, bool descending = false)
        => AddOrderBy(selector, "t1", descending);

    public JoinedQueryBuilder<TMain, TJoin> OrderByFrom<T, TProp>(
        Expression<Func<T, TProp>> selector, bool descending = false)
        => AddOrderBy(selector, GetAlias(typeof(T)), descending);

    private JoinedQueryBuilder<TMain, TJoin> AddOrderBy(
        LambdaExpression selector, string alias, bool descending)
    {
        _orderBys.Add(($"{alias}.{ColumnResolver.ExtractColumnName(selector)}", descending));
        return this;
    }

    // --- Additional joins (chained from any already-joined table) ---

    // .LeftJoin<UserTenants, Tenant>(ut => ut.TenantId, t => t.Id)
    public JoinedQueryBuilder<TMain, TJoin> InnerJoin<TLeft, T3>(
        Expression<Func<TLeft, object?>> leftKey,
        Expression<Func<T3, object?>> rightKey)
        => AddChainedJoin<TLeft, T3>("INNER JOIN", leftKey, rightKey);

    public JoinedQueryBuilder<TMain, TJoin> LeftJoin<TLeft, T3>(
        Expression<Func<TLeft, object?>> leftKey,
        Expression<Func<T3, object?>> rightKey)
        => AddChainedJoin<TLeft, T3>("LEFT JOIN", leftKey, rightKey);

    // .LeftJoin<UserTenants, Tenant>((ut, t) => ut.TenantId == t.Id)
    public JoinedQueryBuilder<TMain, TJoin> InnerJoin<TLeft, T3>(
        Expression<Func<TLeft, T3, bool>> condition)
        => AddChainedJoin<TLeft, T3>("INNER JOIN", condition);

    public JoinedQueryBuilder<TMain, TJoin> LeftJoin<TLeft, T3>(
        Expression<Func<TLeft, T3, bool>> condition)
        => AddChainedJoin<TLeft, T3>("LEFT JOIN", condition);

    private JoinedQueryBuilder<TMain, TJoin> AddChainedJoin<TLeft, T3>(
        string joinType,
        Expression<Func<TLeft, object?>> leftKey,
        Expression<Func<T3, object?>> rightKey)
    {
        var leftAlias = GetAlias(typeof(TLeft));
        var t3Alias = NextAlias();
        var on = $"{leftAlias}.{ColumnResolver.ExtractColumnName(leftKey)}" +
                 $" = {t3Alias}.{ColumnResolver.ExtractColumnName(rightKey)}";
        _joins.Add(new JoinEntry(joinType, ColumnResolver.GetTableName<T3>(), t3Alias, on));
        _aliasMap[typeof(T3)] = t3Alias;
        return this;
    }

    private JoinedQueryBuilder<TMain, TJoin> AddChainedJoin<TLeft, T3>(
        string joinType, Expression<Func<TLeft, T3, bool>> condition)
    {
        var leftAlias = GetAlias(typeof(TLeft));
        var t3Alias = NextAlias();
        var on = JoinConditionParser.Parse(condition, leftAlias, t3Alias);
        _joins.Add(new JoinEntry(joinType, ColumnResolver.GetTableName<T3>(), t3Alias, on));
        _aliasMap[typeof(T3)] = t3Alias;
        return this;
    }

    // --- Build ---

    public BuiltQuery Build()
    {
        var ctx = new QueryContext(_dialect);
        var whereParts = _whereDelegates.Select(d => d(ctx)).ToList();

        var sql = new StringBuilder();
        sql.Append(_distinct ? "SELECT DISTINCT " : "SELECT ");

        if (_selectCols.Count == 0)
        {
            var aliases = new[] { "t0" }.Concat(_joins.Select(j => j.Alias));
            sql.Append(string.Join(", ", aliases.Select(a => $"{a}.*")));
        }
        else
        {
            sql.Append(string.Join(", ", _selectCols));
        }

        sql.Append($" FROM {_mainTable} t0");

        foreach (var join in _joins)
            sql.Append($" {join.JoinType} {join.Table} {join.Alias} ON {join.OnClause}");

        if (whereParts.Count > 0)
            sql.Append($" WHERE {SqlHelpers.CombineWhere(whereParts)}");

        if (_orderBys.Count > 0)
        {
            var cols = _orderBys.Select(o => o.Desc ? $"{o.QualifiedCol} DESC" : o.QualifiedCol);
            sql.Append($" ORDER BY {string.Join(", ", cols)}");
        }

        sql.Append(_dialect.Paging(_take, _skip));

        return new BuiltQuery(sql.ToString(), ctx.Parameters);
    }

    private string NextAlias() => $"t{_joins.Count + 1}";

    private string GetAlias(Type type)
    {
        if (_aliasMap.TryGetValue(type, out var alias)) return alias;
        throw new InvalidOperationException(
            $"Type '{type.Name}' is not part of this query. Add it with a join first.");
    }
}
