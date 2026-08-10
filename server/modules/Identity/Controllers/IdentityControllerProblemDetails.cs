using ICP.Platform.Modules.Identity.Application;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Identity.Controllers;

internal static class IdentityControllerProblemDetails
{
    internal static ObjectResult Create(ControllerBase controller, int status, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = code };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = controller.HttpContext.TraceIdentifier;
        var result = controller.StatusCode(status, problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    internal static ObjectResult FromApplicationException(
        ControllerBase controller,
        int status,
        IdentityApplicationException exception)
    {
        var result = Create(controller, status, exception.FailureCode);
        var problem = (ProblemDetails)result.Value!;
        if (FieldErrors(exception.FailureCode) is { } errors)
        {
            problem.Extensions["errors"] = errors;
        }

        if (exception.LatestAccount is not null)
        {
            problem.Extensions["current"] = IdentityAccountResponse.From(exception.LatestAccount);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string[]>? FieldErrors(string code) => code switch
    {
        IdentityFailureCodes.InvalidAccountCode => Error("ECODE", code),
        IdentityFailureCodes.InvalidDisplayName => Error("ENAME", code),
        IdentityFailureCodes.InvalidReason => Error("Reason", code),
        IdentityFailureCodes.InvalidTargetStatus => Error("TargetStatus", code),
        IdentityFailureCodes.InvalidPassword => Error("NewPassword", code),
        IdentityFailureCodes.InvalidCurrentCredential => Error("CurrentPassword", code),
        IdentityFailureCodes.CredentialUnchanged => Error("NewPassword", code),
        _ => null
    };

    private static IReadOnlyDictionary<string, string[]> Error(string field, string code) =>
        new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [code] };
}
