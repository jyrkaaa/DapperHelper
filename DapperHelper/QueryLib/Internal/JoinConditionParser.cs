using System.Linq.Expressions;

namespace QueryLib.Internal;

internal static class JoinConditionParser
{
    internal static string Parse<T1, T2>(
        Expression<Func<T1, T2, bool>> condition,
        string alias0 = "t0",
        string alias1 = "t1") =>
        ParseNode(condition.Body, condition.Parameters[0], condition.Parameters[1], alias0, alias1);

    private static string ParseNode(
        Expression expr,
        ParameterExpression p0, ParameterExpression p1,
        string alias0, string alias1)
    {
        if (expr is BinaryExpression binary)
        {
            if (binary.NodeType == ExpressionType.AndAlso)
            {
                var left = ParseNode(binary.Left, p0, p1, alias0, alias1);
                var right = ParseNode(binary.Right, p0, p1, alias0, alias1);
                return $"({left} AND {right})";
            }

            var leftRef = ResolveRef(binary.Left, p0, p1, alias0, alias1);
            var rightRef = ResolveRef(binary.Right, p0, p1, alias0, alias1);
            var op = binary.NodeType switch
            {
                ExpressionType.Equal => "=",
                ExpressionType.NotEqual => "<>",
                _ => throw new NotSupportedException(
                    $"Join condition operator '{binary.NodeType}' is not supported.")
            };
            return $"{leftRef} {op} {rightRef}";
        }

        throw new NotSupportedException(
            $"Join condition expression type '{expr.NodeType}' is not supported.");
    }

    private static string ResolveRef(
        Expression expr,
        ParameterExpression p0, ParameterExpression p1,
        string alias0, string alias1)
    {
        expr = ColumnResolver.StripConvert(expr);
        if (expr is not MemberExpression member)
            throw new NotSupportedException(
                $"Join conditions must reference entity properties. Got: {expr}");

        var root = member.Expression
            ?? throw new NotSupportedException("Cannot resolve join condition root.");

        var alias = ColumnResolver.StripConvert(root) switch
        {
            var e when e == p0 => alias0,
            var e when e == p1 => alias1,
            _ => throw new NotSupportedException(
                "Join condition properties must belong to the two joined entities.")
        };

        return $"{alias}.{ColumnResolver.GetColumnName(member.Member)}";
    }
}
