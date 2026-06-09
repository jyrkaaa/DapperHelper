using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace QueryLib.Internal;

internal static class ColumnResolver
{
    internal static string GetTableName<T>()
    {
        var attr = typeof(T).GetCustomAttribute<TableAttribute>();
        return attr?.Name ?? typeof(T).Name;
    }

    internal static string GetColumnName(MemberInfo member)
    {
        var attr = member.GetCustomAttribute<ColumnAttribute>();
        return attr?.Name ?? member.Name;
    }

    internal static string ExtractColumnName(LambdaExpression selector)
    {
        var body = StripConvert(selector.Body);
        if (body is MemberExpression member)
            return GetColumnName(member.Member);
        throw new ArgumentException(
            $"Selector must be a direct property access (e.g. x => x.Name). Got: {selector.Body}");
    }

    internal static Expression StripConvert(Expression expr) =>
        expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u
            ? u.Operand : expr;
}
