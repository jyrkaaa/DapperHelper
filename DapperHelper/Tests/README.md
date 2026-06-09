### Testing
dotnet test Tests/Tests.csproj --logger "console;verbosity=normal"
to run tests

Only performance tests:
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~PerformanceLargeDatasetTests|FullyQualifiedName~PerformanceComparisonTests" --logger
"console;verbosity=normal"
