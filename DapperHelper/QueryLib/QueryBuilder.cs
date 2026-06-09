using System.Linq.Expressions;
using System.Text;
using QueryLib.Dialects;
using QueryLib.Internal;

namespace QueryLib;

public sealed class QueryBuilder<TEntity>
{
    internal string Table { get; }
    internal ISqlDialect Dialect { get; }
    internal List<Expression<Func<TEntity, bool>>> Predicates { get; } = [];
    internal List<string> SelectCols { get; } = [];
    internal List<(string Column, bool Desc)> OrderBys { get; } = [];
    internal bool IsDistinct { get; private set; }
    internal int? TakeCount { get; private set; }
    internal int? SkipCount { get; private set; }

    private QueryBuilder(string table, ISqlDialect dialect)
    {
        Table = table;
        Dialect = dialect;
    }

    public static QueryBuilder<TEntity> From(string? tableName = null, ISqlDialect? dialect = null) =>
        new(tableName ?? ColumnResolver.GetTableName<TEntity>(), dialect ?? SqliteDialect.Instance);

    public QueryBuilder<TEntity> Distinct() { IsDistinct = true; return this; }

    public QueryBuilder<TEntity> Select(params Expression<Func<TEntity, object?>>[] columns)
    {
        foreach (var col in columns)
            SelectCols.Add(ColumnResolver.ExtractColumnName(col));
        return this;
    }

    public QueryBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        Predicates.Add(predicate);
        return this;
    }

    public QueryBuilder<TEntity> OrderBy<TProp>(
        Expression<Func<TEntity, TProp>> selector, bool descending = false)
    {
        OrderBys.Add((ColumnResolver.ExtractColumnName(selector), descending));
        return this;
    }

    public QueryBuilder<TEntity> Take(int count) { TakeCount = count; return this; }
    public QueryBuilder<TEntity> Skip(int count) { SkipCount = count; return this; }

    // Single-key equality join: .InnerJoin<Role>(u => u.RoleId, r => r.Id)
    public JoinedQueryBuilder<TEntity, TJoin> InnerJoin<TJoin>(
        Expression<Func<TEntity, object?>> leftKey,
        Expression<Func<TJoin, object?>> rightKey) =>
        CreateJoin<TJoin>("INNER JOIN", leftKey, rightKey);

    public JoinedQueryBuilder<TEntity, TJoin> LeftJoin<TJoin>(
        Expression<Func<TEntity, object?>> leftKey,
        Expression<Func<TJoin, object?>> rightKey) =>
        CreateJoin<TJoin>("LEFT JOIN", leftKey, rightKey);

    // Composite/expression join: .InnerJoin<Role>((u, r) => u.RoleId == r.Id && u.TenantId == r.TenantId)
    public JoinedQueryBuilder<TEntity, TJoin> InnerJoin<TJoin>(
        Expression<Func<TEntity, TJoin, bool>> condition) =>
        CreateJoin<TJoin>("INNER JOIN", condition);

    public JoinedQueryBuilder<TEntity, TJoin> LeftJoin<TJoin>(
        Expression<Func<TEntity, TJoin, bool>> condition) =>
        CreateJoin<TJoin>("LEFT JOIN", condition);

    private JoinedQueryBuilder<TEntity, TJoin> CreateJoin<TJoin>(
        string joinType,
        Expression<Func<TEntity, object?>> leftKey,
        Expression<Func<TJoin, object?>> rightKey)
    {
        var on = $"t0.{ColumnResolver.ExtractColumnName(leftKey)} = t1.{ColumnResolver.ExtractColumnName(rightKey)}";
        return new JoinedQueryBuilder<TEntity, TJoin>(this, joinType, ColumnResolver.GetTableName<TJoin>(), on);
    }

    private JoinedQueryBuilder<TEntity, TJoin> CreateJoin<TJoin>(
        string joinType,
        Expression<Func<TEntity, TJoin, bool>> condition)
    {
        var on = JoinConditionParser.Parse(condition);
        return new JoinedQueryBuilder<TEntity, TJoin>(this, joinType, ColumnResolver.GetTableName<TJoin>(), on);
    }

    public BuiltQuery Build()
    {
        var ctx = new QueryContext(Dialect);
        var parser = new ExpressionParser(ctx, null);
        var whereParts = Predicates.Select(p => parser.Parse(p)).ToList();

        var sql = new StringBuilder();
        sql.Append(IsDistinct ? "SELECT DISTINCT " : "SELECT ");
        sql.Append(SelectCols.Count > 0 ? string.Join(", ", SelectCols) : "*");
        sql.Append($" FROM {Table}");

        if (whereParts.Count > 0)
            sql.Append($" WHERE {SqlHelpers.CombineWhere(whereParts)}");

        if (OrderBys.Count > 0)
        {
            var cols = OrderBys.Select(o => o.Desc ? $"{o.Column} DESC" : o.Column);
            sql.Append($" ORDER BY {string.Join(", ", cols)}");
        }

        sql.Append(Dialect.Paging(TakeCount, SkipCount));

        return new BuiltQuery(sql.ToString(), ctx.Parameters);
    }
}
