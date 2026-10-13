namespace ICP.Platform.Modules.Organization.Services;

public static class PersonUserBindingFailureCodes
{
    public const string InvalidRequest = "ORGANIZATION_PERSON_BINDING_INVALID";
    public const string InvalidReason = "ORGANIZATION_PERSON_BINDING_REASON_INVALID";
    public const string InvalidActor = "ORGANIZATION_PERSON_BINDING_ACTOR_INVALID";
    public const string PersonNotFound = "ORGANIZATION_PERSON_NOT_FOUND";
    public const string PersonDisabled = "ORGANIZATION_PERSON_BINDING_PERSON_DISABLED";
    public const string PersonAlreadyBound = "ORGANIZATION_PERSON_ALREADY_BOUND";
    public const string UserNotAvailable = "ORGANIZATION_PERSON_BINDING_USER_NOT_AVAILABLE";
    public const string UserAlreadyBound = "ORGANIZATION_USER_ALREADY_BOUND";
    public const string LocalAdministratorProtected = "ORGANIZATION_LOCAL_ADMIN_BINDING_FORBIDDEN";
    public const string RevisionConflict = "IDENTITY_REVISION_CONFLICT";
    public const string IdentityUnavailable = "ORGANIZATION_IDENTITY_UNAVAILABLE";
}

public sealed class PersonUserBindingException(
    string failureCode,
    object? current = null,
    Exception? innerException = null,
    int? statusCode = null) : InvalidOperationException(failureCode, innerException)
{
    public string FailureCode { get; } = failureCode;
    public object? Current { get; } = current;
    public int StatusCode { get; } = statusCode ?? failureCode switch
    {
        PersonUserBindingFailureCodes.PersonNotFound => 404,
        PersonUserBindingFailureCodes.IdentityUnavailable => 503,
        PersonUserBindingFailureCodes.PersonDisabled
            or PersonUserBindingFailureCodes.PersonAlreadyBound
            or PersonUserBindingFailureCodes.UserAlreadyBound
            or PersonUserBindingFailureCodes.LocalAdministratorProtected
            or PersonUserBindingFailureCodes.RevisionConflict => 409,
        _ => 400
    };
}
