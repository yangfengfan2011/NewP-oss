using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;

namespace ICP.Platform.Modules.Identity.Application;

public sealed record IdentityActorContext(
    string ActorId,
    IdentitySecurityActorKind ActorKind,
    string TraceId);

public sealed record CreateIdentityAccountCommand(
    string ECODE,
    string ENAME,
    IdentityActorContext Actor);

public sealed record UpdateIdentityAccountNameCommand(
    string AccountEid,
    string ENAME,
    long ExpectedRevision,
    IdentityActorContext Actor);

public sealed record SetIdentityAccountStatusCommand(
    string AccountEid,
    IdentityAccountStatus TargetStatus,
    long ExpectedRevision,
    string Reason,
    IdentityActorContext Actor);

public sealed record ResetIdentityCredentialCommand(
    string AccountEid,
    long ExpectedRevision,
    string Reason,
    IdentityActorContext Actor);

public sealed record ChangeIdentityPasswordCommand(
    string AccountEid,
    string CurrentPassword,
    string NewPassword,
    long ExpectedRevision,
    IdentityActorContext Actor,
    string TimeZoneId);

public sealed record DeleteIdentityAccountCommand(
    string AccountEid,
    long ExpectedRevision,
    string Reason,
    IdentityActorContext Actor);

public sealed record IdentityAccountCreationResult(
    IdentityAccountSnapshot Account,
    string TemporaryCredential);

public sealed record IdentityCredentialResetResult(
    IdentityAccountSnapshot Account,
    string TemporaryCredential);

public sealed record IdentityPasswordChangeResult(
    IdentityAccountSnapshot Account,
    AuthenticationSessionIssuance Session);

public static class IdentityFailureCodes
{
    public const string InvalidAccountCode = "IDENTITY_ECODE_INVALID";
    public const string InvalidDisplayName = "IDENTITY_ENAME_INVALID";
    public const string InvalidReason = "IDENTITY_REASON_INVALID";
    public const string InvalidActor = "IDENTITY_ACTOR_INVALID";
    public const string InvalidPersonReference = "IDENTITY_PERSON_REFERENCE_INVALID";
    public const string AccountCodeConflict = "IDENTITY_ECODE_CONFLICT";
    public const string AccountNotFound = "IDENTITY_ACCOUNT_NOT_FOUND";
    public const string RevisionConflict = "IDENTITY_REVISION_CONFLICT";
    public const string InvalidTargetStatus = "IDENTITY_STATUS_TARGET_INVALID";
    public const string DeletedAccount = "IDENTITY_ACCOUNT_DELETED";
    public const string LocalAdministratorProtected = "IDENTITY_LOCAL_ADMIN_PROTECTED";
    public const string PersonnelBound = "IDENTITY_PERSONNEL_BOUND";
    public const string PersonnelDisableHold = "IDENTITY_PERSONNEL_DISABLE_HOLD";
    public const string InvalidPassword = "CREDENTIAL_PASSWORD_INVALID";
    public const string InvalidCurrentCredential = "CREDENTIAL_CURRENT_INVALID";
    public const string CredentialUnchanged = "CREDENTIAL_UNCHANGED";
}

public sealed class IdentityApplicationException : InvalidOperationException
{
    public IdentityApplicationException(
        string failureCode,
        IdentityAccountSnapshot? latestAccount = null,
        Exception? innerException = null)
        : base(failureCode, innerException)
    {
        FailureCode = failureCode;
        LatestAccount = latestAccount;
    }

    public string FailureCode { get; }

    public IdentityAccountSnapshot? LatestAccount { get; }
}

public interface IIdentityAccountApplicationService
{
    ValueTask<IdentityAccountCreationResult> CreateAsync(
        CreateIdentityAccountCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityAccountSnapshot> UpdateNameAsync(
        UpdateIdentityAccountNameCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityAccountSnapshot> SetStatusAsync(
        SetIdentityAccountStatusCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityCredentialResetResult> ResetCredentialAsync(
        ResetIdentityCredentialCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityPasswordChangeResult> ChangePasswordAsync(
        ChangeIdentityPasswordCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityAccountSnapshot> DeleteAsync(
        DeleteIdentityAccountCommand command,
        CancellationToken cancellationToken = default);
}

public static class IdentitySecurityEventTypes
{
    public const string LocalAdministratorBootstrapped = "LocalAdministratorBootstrapped";
    public const string LocalAdministratorCredentialReset = "LocalAdministratorCredentialReset";
    public const string AccountCreated = "AccountCreated";
    public const string DisplayNameChanged = "DisplayNameChanged";
    public const string AccountEnabled = "AccountEnabled";
    public const string AccountDisabled = "AccountDisabled";
    public const string CredentialReset = "CredentialReset";
    public const string PasswordChanged = "PasswordChanged";
    public const string AccountDeleted = "AccountDeleted";
    public const string PersonnelBound = "PersonnelBound";
    public const string PersonnelDisabled = "PersonnelDisabled";
    public const string PersonnelDisableHoldReleased = "PersonnelDisableHoldReleased";
}
