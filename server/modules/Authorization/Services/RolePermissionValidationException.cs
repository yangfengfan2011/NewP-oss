namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>可翻译为请求校验失败的功能配置错误；存储与提交异常继续传播。</summary>
public sealed class RolePermissionValidationException(string message, string code = "AUTHORIZATION_INVALID", IReadOnlyDictionary<string, object?>? args = null) : AuthorizationValidationException(message, code, args);
