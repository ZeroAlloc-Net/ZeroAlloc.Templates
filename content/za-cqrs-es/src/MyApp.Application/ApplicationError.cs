namespace MyApp.Application;

/// <summary>Application-layer error envelope returned by handlers via <c>Result&lt;T, ApplicationError&gt;</c>.</summary>
public sealed record ApplicationError(string Code, string Message);
