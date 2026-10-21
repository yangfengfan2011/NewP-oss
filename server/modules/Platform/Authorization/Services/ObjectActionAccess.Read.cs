using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class ObjectActionAccess
{
    private readonly AsyncLocal<ReadScope?> _readScope = new();

    /// <inheritdoc/>
    public async Task<TResult> WithReadAsync<TRead, TResult>(Func<CancellationToken, Task<ObjectActionRead<TRead>>> read,
        Func<TRead, IObjectActionReadScope, CancellationToken, Task<TResult>> consume, CancellationToken cancellationToken = default)
    {
        var request = _http.HttpContext ?? throw new ActionDataAccessException(401, "ButtonExecution.SubjectRequired");
        var subject = ReadSubject(request);
        var previous = _readScope.Value;
        ReadScope? scope = null;
        _readScope.Value = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.RequestAborted.ThrowIfCancellationRequested();
            // delegate必须在这里执行原owner完整读取；只交出本次真实规范身份和业务事实，不能传预先允许结果。
            var actual = await read(cancellationToken) ?? throw new ActionDataAccessException(400, "ButtonExecution.ReadInvalid");
            var resource = ObjectResource(actual.ObjectName);
            ValidateObjectActions(resource, actual.ObjectName, actual.ObjectId, ["read"]);
            scope = new(this, request, subject, actual.ObjectName, actual.ObjectId, resource, cancellationToken);
            _readScope.Value = scope;
            scope.AssertCurrent();
            var result = await consume(actual.Value, scope, cancellationToken);
            scope.AssertCurrent();
            return result;
        }
        finally
        {
            scope?.Expire();
            _readScope.Value = previous;
        }
    }

    // 沿平台已有可信主体构造保留唯一NameIdentifier；缺失认证不变为匿名动作检查。
    private static string ReadSubject(HttpContext request)
    {
        try { return AuthenticatedSubject.FromPrincipal(request.User).SubjectId; }
        catch (PermissionSubjectException) { throw new ActionDataAccessException(401, "ButtonExecution.SubjectRequired"); }
    }

    // 私有作用域只保留当前调用身份及生命周期，不持有任何动作允许结果。
    private sealed class ReadScope(ObjectActionAccess owner, HttpContext request, string subject, string objectName,
        string objectId, DataScopeResource resource, CancellationToken originalToken) : IObjectActionReadScope
    {
        private volatile bool _active = true;

        // 同一HTTP请求、可信主体与活动回调缺一不可；嵌套读取期间外层作用域暂停。
        internal void AssertCurrent()
        {
            if (!_active || !ReferenceEquals(owner._readScope.Value, this) || !ReferenceEquals(owner._http.HttpContext, request)
                || request.User.Identity?.IsAuthenticated != true || ReadSubject(request) != subject)
                throw new ActionDataAccessException(400, "ButtonExecution.ReadScopeInvalid");
            originalToken.ThrowIfCancellationRequested();
            request.RequestAborted.ThrowIfCancellationRequested();
        }

        // finally只撤销本次调用能力；不保存允许供后续请求、目标或分支复用。
        internal void Expire() => _active = false;

        /// <inheritdoc/>
        public async Task<DataExecutionDecision> CheckAsync(string requestedObject, string requestedId, IReadOnlyList<string> actions,
            CancellationToken cancellationToken = default)
        {
            AssertCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            if (requestedObject != objectName || requestedId != objectId)
                throw new ActionDataAccessException(400, "ButtonExecution.ReadScopeTargetInvalid");
            var frozen = actions.ToArray();
            owner.ValidateObjectActions(resource, objectName, objectId, frozen);
            // 调用者可以缩短取消期限，但不能以None抹掉原读取或请求的取消。
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(originalToken, request.RequestAborted, cancellationToken);
            foreach (var action in frozen)
            {
                var allowed = await owner.CheckCurrentActionAsync(resource, objectId, action, cancellation.Token);
                AssertCurrent();
                cancellationToken.ThrowIfCancellationRequested();
                if (!allowed) return DataExecutionDecision.Denied;
            }
            return DataExecutionDecision.Allowed;
        }
    }
}
