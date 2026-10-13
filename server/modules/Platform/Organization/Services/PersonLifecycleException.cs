namespace ICP.Platform.Modules.Organization.Services;

public static class PersonLifecycleFailureCodes
{
    public const string InvalidRequest = "ORGANIZATION_PERSON_LIFECYCLE_INVALID";
    public const string InvalidReason = "ORGANIZATION_PERSON_LIFECYCLE_REASON_INVALID";
    public const string InvalidActor = "ORGANIZATION_PERSON_LIFECYCLE_ACTOR_INVALID";
    public const string PersonNotFound = "ORGANIZATION_PERSON_NOT_FOUND";
    public const string Conflict = "LIFECYCLE_CONFLICT";
    public const string DependencyUnavailable = "ORGANIZATION_PERSON_LIFECYCLE_DEPENDENCY_UNAVAILABLE";
}

public sealed class PersonLifecycleException(
    string failureCode,
    object? current = null,
    Exception? innerException = null,
    int? statusCode = null) : InvalidOperationException(failureCode, innerException)
{
    public string FailureCode { get; } = failureCode;
    public object? Current { get; } = current;
    public int StatusCode { get; } = statusCode ?? failureCode switch
    {
        PersonLifecycleFailureCodes.PersonNotFound => 404,
        PersonLifecycleFailureCodes.Conflict => 409,
        PersonLifecycleFailureCodes.DependencyUnavailable => 503,
        _ => 400
    };
}
