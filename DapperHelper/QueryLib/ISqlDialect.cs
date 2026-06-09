namespace QueryLib;

public interface ISqlDialect
{
    string EscapeLikeValue(string value);
    string LikeExpression(string column, string paramRef);
    string Paging(int? take, int? skip);
}
