using ICP.Platform.HostApi.IdentityMaintenance;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class IdentityMaintenanceCommandTests
{
    [Fact]
    public async Task HostProcess_WithRedirectedOutput_RejectsMaintenanceBeforeHostStartup()
    {
        var hostAssembly = typeof(ICP.Platform.HostApi.Controllers.HealthController).Assembly.Location;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{hostAssembly}\" identity bootstrap-local-admin",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        Assert.True(process.Start());
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(2, process.ExitCode);
        Assert.Empty(standardOutput);
        Assert.Contains(IdentityMaintenanceCommandFailureCodes.InteractiveTerminalRequired, standardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Now listening", standardError, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("bootstrap-local-admin", IdentityMaintenanceCommandKind.BootstrapLocalAdministrator)]
    [InlineData("reset-local-admin", IdentityMaintenanceCommandKind.ResetLocalAdministrator)]
    public void Parse_AcceptsOnlySupportedTwoSegmentCommands(string verb, IdentityMaintenanceCommandKind expected)
    {
        var result = IdentityMaintenanceCommandLine.Parse(["identity", verb]);

        Assert.True(result.IsMaintenanceCommand);
        Assert.Equal(expected, result.Kind);
        Assert.Null(result.FailureCode);
    }

    [Fact]
    public void Parse_RejectsAdditionalArgumentThatCouldCarryAPlaintextPassword()
    {
        var result = IdentityMaintenanceCommandLine.Parse(["identity", "bootstrap-local-admin", "secret"]);

        Assert.True(result.IsMaintenanceCommand);
        Assert.Equal(IdentityMaintenanceCommandFailureCodes.InvalidArguments, result.FailureCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTerminalIsNotInteractive_FailsBeforeCallingIdentity()
    {
        var maintenance = new RecordingMaintenanceService();
        var terminal = new RecordingTerminal(isInteractive: false, confirms: true);
        var runner = CreateRunner(maintenance, terminal);

        var exitCode = await runner.ExecuteAsync(IdentityMaintenanceCommandKind.BootstrapLocalAdministrator);

        Assert.Equal(2, exitCode);
        Assert.Empty(maintenance.Calls);
        Assert.Contains(IdentityMaintenanceCommandFailureCodes.InteractiveTerminalRequired, terminal.Errors);
    }

    [Fact]
    public async Task ExecuteAsync_BootstrapPrintsTemporaryCredentialExactlyOnce()
    {
        var maintenance = new RecordingMaintenanceService();
        var terminal = new RecordingTerminal(isInteractive: true, confirms: true);
        var runner = CreateRunner(maintenance, terminal);

        var exitCode = await runner.ExecuteAsync(IdentityMaintenanceCommandKind.BootstrapLocalAdministrator);

        Assert.Equal(0, exitCode);
        Assert.Equal(["bootstrap"], maintenance.Calls);
        Assert.Single(terminal.Secrets);
        Assert.Equal("one-time-secret", terminal.Secrets[0]);
        Assert.DoesNotContain("one-time-secret", terminal.Messages);
        Assert.DoesNotContain("one-time-secret", terminal.Errors);
    }

    private static IdentityMaintenanceCommandRunner CreateRunner(
        IIdentityLocalAdministratorMaintenanceService maintenance,
        IIdentityMaintenanceTerminal terminal)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:LocalAdministrator:ECODE"] = "admin",
                ["Identity:LocalAdministrator:ENAME"] = "Local Administrator"
            })
            .Build();
        return new IdentityMaintenanceCommandRunner(maintenance, terminal, configuration);
    }

    private sealed class RecordingMaintenanceService : IIdentityLocalAdministratorMaintenanceService
    {
        public List<string> Calls { get; } = [];

        public ValueTask<IdentityAccountCreationResult> BootstrapAsync(BootstrapLocalAdministratorCommand command, CancellationToken cancellationToken = default)
        {
            Calls.Add("bootstrap");
            return ValueTask.FromResult(new IdentityAccountCreationResult(Account(), "one-time-secret"));
        }

        public ValueTask<IdentityCredentialResetResult> ResetAsync(ResetLocalAdministratorCredentialCommand command, CancellationToken cancellationToken = default)
        {
            Calls.Add("reset");
            return ValueTask.FromResult(new IdentityCredentialResetResult(Account(), "reset-secret"));
        }

        private static IdentityAccountSnapshot Account() => new()
        {
            UserEid = "User_1",
            ECODE = "admin",
            ENAME = "Local Administrator",
            Status = IdentityAccountStatus.Enabled,
            Revision = 1,
            IsLocalAdministrator = true
        };
    }

    private sealed class RecordingTerminal(bool isInteractive, bool confirms) : IIdentityMaintenanceTerminal
    {
        public bool IsInteractive { get; } = isInteractive;
        public List<string> Messages { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Secrets { get; } = [];
        public ValueTask<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken = default) => ValueTask.FromResult(confirms);
        public void WriteMessage(string message) => Messages.Add(message);
        public void WriteError(string failureCode) => Errors.Add(failureCode);
        public void WriteSecret(string temporaryCredential) => Secrets.Add(temporaryCredential);
    }
}
