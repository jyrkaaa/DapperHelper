using System.Collections;
using System.Linq.Expressions;

namespace QueryLib.Internal;

internal sealed class ExpressionParser(QueryContext ctx, string? alias)
{
    public string Parse<T>(Expression<Func<T, bool>> predicate) =>
        ParseNode(predicate.Body, predicate.Parameters[0]);

    private string ParseNode(Expression expr, ParameterExpression param) => expr switch
    {
        ConstantExpression { Value: true } => "1=1",
        ConstantExpression { Value: false } => "1=0",
        BinaryExpression binary => ParseBinary(binary, param),
        // !u.IsActive  →  col = 0
        UnaryExpression { NodeType: ExpressionType.Not, Operand: MemberExpression m }
            when m.Type == typeof(bool) && IsDirectMember(m, param) =>
            $"{QualifiedColumn(m)} = 0",
        // NOT (complex)
        UnaryExpression { NodeType: ExpressionType.Not } unary =>
            $"NOT ({ParseNode(unary.Operand, param)})",
        // u.IsActive  →  col = 1
        MemberExpression member when member.Type == typeof(bool) && IsDirectMember(member, param) =>
            $"{QualifiedColumn(member)} = 1",
        MethodCallExpression call => ParseMethodCall(call, param),
        _ => throw new NotSupportedException(
            $"Expression '{expr}' ({expr.NodeType}) is not supported in WHERE clauses.")
    };

    private string ParseBinary(BinaryExpression expr, ParameterExpression param)
    {
        if (expr.NodeType == ExpressionType.AndAlso)
            return $"({ParseNode(expr.Left, param)} AND {ParseNode(expr.Right, param)})";
        if (expr.NodeType == ExpressionType.OrElse)
            return $"({ParseNode(expr.Left, param)} OR {ParseNode(expr.Right, param)})";

        var (colExpr, valExpr, flip) = ResolveSides(expr.Left, expr.Right, param);
        var column = QualifiedColumn((MemberExpression)ColumnResolver.StripConvert(colExpr));
        var opType = flip ? FlipOp(expr.NodeType) : expr.NodeType;
        var value = Evaluate(valExpr);

        if (value is null)
            return opType == ExpressionType.Equal ? $"{column} IS NULL" : $"{column} IS NOT NULL";

        var op = opType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw new NotSupportedException($"Operator '{expr.NodeType}' is not supported.")
        };

        return $"{column} {op} {ctx.AddParam(value)}";
    }

    private (Expression col, Expression val, bool flipped) ResolveSides(
        Expression left, Expression right, ParameterExpression param)
    {
        if (IsDirectMember(left, param)) return (left, right, false);
        if (IsDirectMember(right, param)) return (right, left, true);
        throw new NotSupportedException(
            "One side of a comparison must be a direct entity property (e.g. u.Email).");
    }

    private string ParseMethodCall(MethodCallExpression call, ParameterExpression param)
    {
        // u.Email.Contains("x"), u.Name.StartsWith("x"), u.Name.EndsWith("x")
        if (call.Object is MemberExpression instanceMember &&
            IsDirectMember(instanceMember, param) &&
            call.Arguments.Count >= 1)
        {
            var col = QualifiedColumn(instanceMember);
            var arg = Evaluate(call.Arguments[0])?.ToString() ?? string.Empty;

            return call.Method.Name switch
            {
                "Contains" => ctx.Dialect.LikeExpression(col,
                    ctx.AddParam($"%{ctx.Dialect.EscapeLikeValue(arg)}%")),
                "StartsWith" => ctx.Dialect.LikeExpression(col,
                    ctx.AddParam($"{ctx.Dialect.EscapeLikeValue(arg)}%")),
                "EndsWith" => ctx.Dialect.LikeExpression(col,
                    ctx.AddParam($"%{ctx.Dialect.EscapeLikeValue(arg)}")),
                _ => throw new NotSupportedException(
                    $"String method '{call.Method.Name}' is not supported.")
            };
        }

        // ids.Contains(u.Id)  or  Enumerable.Contains(ids, u.Id)
        if (call.Method.Name == "Contains")
        {
            Expression? collectionExpr = null;
            Expression? elementExpr = null;

            if (call.Object is null && call.Arguments.Count == 2)
            {
                collectionExpr = call.Arguments[0];
                elementExpr = call.Arguments[1];
            }
            else if (call.Object is not null && call.Arguments.Count == 1)
            {
                collectionExpr = call.Object;
                elementExpr = call.Arguments[0];
            }

            if (collectionExpr is not null && elementExpr is not null &&
                IsDirectMember(elementExpr, param))
            {
                var col = QualifiedColumn(
                    (MemberExpression)ColumnResolver.StripConvert(elementExpr));
                var items = (Evaluate(collectionExpr) as IEnumerable)
                    ?.Cast<object?>().ToList()
                    ?? throw new InvalidOperationException("Collection evaluated to null.");
                if (items.Count == 0) return "1=0";
                return $"{col} IN ({string.Join(", ", items.Select(ctx.AddParam))})";
            }
        }

        throw new NotSupportedException(
            $"Method '{call.Method.DeclaringType?.Name}.{call.Method.Name}' is not supported.");
    }

    private string QualifiedColumn(MemberExpression member)
    {
        var col = ColumnResolver.GetColumnName(member.Member);
        return alias is null ? col : $"{alias}.{col}";
    }

    // Only matches direct property accesses: u.Email, not u.Email.Length
    private static bool IsDirectMember(Expression expr, ParameterExpression param)
    {
        expr = ColumnResolver.StripConvert(expr);
        return expr is MemberExpression { Expression: { } parent }
            && ColumnResolver.StripConvert(parent) == param;
    }

    private static object? Evaluate(Expression expr) => expr switch
    {
        ConstantExpression c => c.Value,
        _ => Expression.Lambda(expr).Compile().DynamicInvoke()
    };

    private static ExpressionType FlipOp(ExpressionType op) => op switch
    {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => op
    };
}
