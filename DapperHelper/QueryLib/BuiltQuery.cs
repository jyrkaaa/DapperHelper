using Dapper;

namespace QueryLib;

public sealed record BuiltQuery(string Sql, DynamicParameters Parameters);
