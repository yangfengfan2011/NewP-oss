using ICP.Platform.Modules.Identity.Application;

namespace ICP.Platform.HostApi.IdentityMaintenance;

public interface IIdentityMaintenanceTerminal
{
    bool IsInteractive { get; }
    ValueTask<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken = default);
    void WriteMessage(string message);
    void WriteError(string failureCode);
    void WriteSecret(string temporaryCredential);
}

public sealed class SystemIdentityMaintenanceTerminal : IIdentityMaintenanceTerminal
{
    public bool IsInteractive =>
        Environment.UserInteractive
        && !Console.IsInputRedirected
        && !Console.IsOutputRedirected;

    public ValueTask<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.Write($"{prompt} [y/N]: ");
        var response = Console.ReadLine();
        return ValueTask.FromResult(string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase));
    }

    public void WriteMessage(string message) => Console.WriteLine(message);

    public void WriteError(string failureCode) => Console.Error.WriteLine(failureCode);

    public void WriteSecret(string temporaryCredential) =>
        Console.WriteLine($"Temporary credential: {temporaryCredential}");
}

public sealed class IdentityMaintenanceCommandRunner(
    IIdentityLocalAdministratorMaintenanceService maintenanceService,
    IIdentityMaintenanceTerminal terminal,
    IConfiguration configuration)
{
    public async ValueTask<int> ExecuteAsync(
        IdentityMaintenanceCommandKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!terminal.IsInteractive)
        {
            terminal.WriteError(IdentityMaintenanceCommandFailureCodes.InteractiveTerminalRequired);
            return 2;
        }

        if (!await terminal.ConfirmAsync("Confirm local administrator maintenance", cancellationToken))
        {
            terminal.WriteError(IdentityMaintenanceCommandFailureCodes.Cancelled);
            return 2;
        }

        try
        {
            var temporaryCredential = kind switch
            {
                IdentityMaintenanceCommandKind.BootstrapLocalAdministrator =>
                    (await maintenanceService.BootstrapAsync(BuildBootstrapCommand(), cancellationToken)).TemporaryCredential,
                IdentityMaintenanceCommandKind.ResetLocalAdministrator =>
                    (await maintenanceService.ResetAsync(BuildResetCommand(), cancellationToken)).TemporaryCredential,
                _ => throw new InvalidOperationException(IdentityMaintenanceCommandFailureCodes.InvalidArguments)
            };
            terminal.WriteMessage("Identity maintenance completed successfully.");
            terminal.WriteSecret(temporaryCredential);
            return 0;
        }
        catch (IdentityMaintenanceException exception)
        {
            terminal.WriteError(exception.FailureCode);
            return 1;
        }
        catch (InvalidOperationException exception) when (IsStableFailureCode(exception.Message))
        {
            terminal.WriteError(exception.Message);
            return 1;
        }
    }

    private BootstrapLocalAdministratorCommand BuildBootstrapCommand()
    {
        var ecode = configuration["Identity:LocalAdministrator:ECODE"];
        var ename = configuration["Identity:LocalAdministrator:ENAME"];
        if (string.IsNullOrWhiteSpace(ecode) || string.IsNullOrWhiteSpace(ename))
        {
            throw new IdentityMaintenanceException(IdentityMaintenanceCommandFailureCodes.ConfigurationInvalid);
        }

        return new(
            ecode,
            ename,
            IdentityMaintenanceReasons.Bootstrap,
            CreateActor());
    }

    private static ResetLocalAdministratorCredentialCommand BuildResetCommand() =>
        new(IdentityMaintenanceReasons.CredentialReset, CreateActor());

    private static IdentityActorContext CreateActor() => new(
        IdentityMaintenanceActors.LocalMaintenance,
        ICP.Platform.Modules.Identity.Models.IdentitySecurityActorKind.LocalMaintenance,
        Guid.NewGuid().ToString("N"));

    private static bool IsStableFailureCode(string message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.All(static character => char.IsAsciiLetterUpper(character)
            || char.IsDigit(character)
            || character == '_');
}
