using System.Data.Common;
using System.Text;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using Microsoft.AspNetCore.Identity;

namespace ICP.Platform.Modules.Identity.Application;

public sealed class LocalCredentialAuthenticator : ILocalCredentialAuthenticator
{
    private readonly IIdentityRepository _repository;
    private readonly IPasswordHasher<User> _passwordHasher;

    public LocalCredentialAuthenticator(IIdentityRepository repository, IPasswordHasher<User> passwordHasher)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
    }

    public async ValueTask<LocalCredentialAuthenticationResult> AuthenticateAsync(
        string accountCode,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedCode = (accountCode ?? string.Empty)
                .Trim()
                .Normalize(NormalizationForm.FormKC)
                .ToUpperInvariant();
            var user = await _repository.FindUserByNormalizedCodeAsync(normalizedCode, cancellationToken);
            if (user is not { Status: IdentityAccountStatus.Enabled })
            {
                return Invalid();
            }

            var credential = await _repository.FindCredentialAsync(user.EID, cancellationToken);
            if (credential is null)
            {
                return Invalid();
            }

            var verification = _passwordHasher.VerifyHashedPassword(user, credential.PasswordHash, password ?? string.Empty);
            if (verification == PasswordVerificationResult.Failed)
            {
                return Invalid();
            }

            return new LocalCredentialAuthenticationResult
            {
                Status = LocalCredentialAuthenticationStatus.Succeeded,
                Account = IdentityAccountApplicationService.ToSnapshot(user),
                IsTemporary = credential.IsTemporary
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            return new LocalCredentialAuthenticationResult
            {
                Status = LocalCredentialAuthenticationStatus.StoreUnavailable,
                FailureCode = LocalCredentialAuthenticationFailureCodes.StoreUnavailable
            };
        }
    }

    private static LocalCredentialAuthenticationResult Invalid() => new()
    {
        Status = LocalCredentialAuthenticationStatus.Invalid
    };
}
