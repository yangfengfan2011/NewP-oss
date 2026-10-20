using System.Reflection;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataExecutionExceptionProjectionTests
{
    [Theory]
    [InlineData("workspace", 400)]
    [InlineData("workspace", 403)]
    [InlineData("organization", 400)]
    [InlineData("organization", 403)]
    [InlineData("standard", 400)]
    [InlineData("standard", 403)]
    [InlineData("global", 400)]
    [InlineData("global", 403)]
    public async Task ActualFilters_KeepStatusTitleCodeAndHandling(string caller, int status)
    {
        var error = new ActionDataAccessException(status, "ActionDataAccess.DeclarationInvalid");
        var events = new List<string>();
        var result = await InvokeAsync(caller, error, false, events);
        var response = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(status, problem.Status);
        Assert.Equal(caller == "global" ? error.Message : error.Code, problem.Title);
        Assert.Equal(error.Code, Assert.Single(problem.Extensions).Value);
        Assert.True(problem.Extensions.ContainsKey("code"));
        Assert.Equal(caller is "workspace" or "global", result.Handled);
        Assert.Equal(caller is "organization" or "standard" ? new[] { "validate" } : [], events);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("standard")]
    public async Task ActionFilters_ValidateBeforeNextAndKeepItsExceptionBoundary(string caller)
    {
        var events = new List<string>();
        var success = await InvokeAsync(caller, null, false, events);
        Assert.Null(success.Result);
        Assert.Equal(new[] { "validate", "next" }, events);
        events.Clear();
        var failure = await InvokeAsync(caller, new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid"), true, events);
        Assert.IsType<ObjectResult>(failure.Result);
        Assert.Equal(new[] { "validate", "next" }, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonTargetErrorsAndCancellation_KeepTheirOriginalPropagation(bool cancel)
    {
        Exception error = cancel ? new OperationCanceledException() : new InvalidOperationException("business-failure");
        foreach (var caller in new[] { "workspace", "global" })
        {
            var result = await InvokeAsync(caller, error, false, []);
            Assert.Null(result.Result);
            Assert.False(result.Handled);
        }
        foreach (var caller in new[] { "organization", "standard" })
        foreach (var fromNext in new[] { false, true })
        {
            var events = new List<string>();
            var thrown = await Assert.ThrowsAnyAsync<Exception>(() => InvokeAsync(caller, error, fromNext, events));
            Assert.Same(error, thrown);
            Assert.Equal(fromNext ? new[] { "validate", "next" } : ["validate"], events);
        }
    }

    private static async Task<(IActionResult? Result, bool Handled)> InvokeAsync(
        string caller, Exception? error, bool fromNext, List<string> events)
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        if (caller is "workspace" or "global")
        {
            var failure = new ExceptionContext(context, filters) { Exception = error! };
            if (caller == "workspace") await new WorkspaceClipboardExecutionAttribute(WorkspaceObjectCopyExecutions.Copy, "prepare").OnExceptionAsync(failure);
            else new DataExecutionExceptionFilter().OnException(failure);
            Assert.Same(error, failure.Exception);
            return (failure.Result, failure.ExceptionHandled);
        }
        var execution = DispatchProxy.Create<IDataExecutionAccess, ExecutionProbe>();
        var probe = (ExecutionProbe)execution;
        probe.Events = events;
        probe.Failure = fromNext ? null : error;
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        using var services = new ServiceCollection().AddSingleton(execution)
            .AddSingleton<IStandardCrudResourceRegistry>(new StandardCrudResourceRegistry([descriptor])).BuildServiceProvider();
        context.HttpContext.RequestServices = services;
        var action = new ActionExecutingContext(context, filters, new Dictionary<string, object?>(), new object());
        Task<ActionExecutedContext> Next()
        {
            events.Add("next");
            return fromNext && error is not null ? Task.FromException<ActionExecutedContext>(error)
                : Task.FromResult(new ActionExecutedContext(context, filters, action.Controller));
        }
        if (caller == "organization")
        {
            var filter = new OrganizationLookupExecutionAttribute("organization.person.options", "organization.person");
            await filter.OnActionExecutionAsync(action, Next);
            Assert.Equal(filter.ResourceKey, probe.ResourceKey);
            Assert.Equal(new[] { filter.Binding }, probe.Bindings);
        }
        else
        {
            var filter = new StandardCrudDataExecutionAttribute(descriptor.ResourceKey, descriptor.DataExecutions["read"], "read");
            await filter.OnActionExecutionAsync(action, Next);
            Assert.Equal(filter.ResourceKey, probe.ResourceKey);
            Assert.Equal(filter.Bindings, probe.Bindings);
        }
        return (action.Result, false);
    }

    public class ExecutionProbe : DispatchProxy
    {
        public List<string> Events { get; set; } = [];
        public Exception? Failure { get; set; }
        public string? ResourceKey { get; private set; }
        public IReadOnlyList<DataExecutionBindingReference>? Bindings { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal(nameof(IDataExecutionAccess.Validate), method?.Name);
            Events.Add("validate");
            Bindings = Assert.IsAssignableFrom<IReadOnlyList<DataExecutionBindingReference>>(args![0]);
            ResourceKey = Assert.IsType<string>(args[1]);
            if (Failure is not null) throw Failure;
            return null;
        }
    }
}
