using ICP.Platform.Modules.Audit.DTOs;
using ICP.Platform.Modules.Audit.Models;
using ICP.Platform.Modules.Audit.Repositories;
using ICP.Platform.Modules.Audit.Services;
using Xunit;

namespace ICP.Platform.Modules.Audit.Tests;

public class AuditServiceTests
{
    private readonly IAuditService _auditService;
    private readonly IOperationLogRepository _operationLogRepository;
    private readonly IAccessLogRepository _accessLogRepository;

    public AuditServiceTests()
    {
        _operationLogRepository = new OperationLogRepository();
        _accessLogRepository = new AccessLogRepository();
        _auditService = new AuditService(_operationLogRepository, _accessLogRepository);
    }

    [Fact]
    public async Task LogOperationAsync_CreatesOperationLog()
    {
        // Arrange
        var request = new CreateOperationLogRequest
        {
            ModuleId = "TestModule",
            OperationType = "Create",
            OperationTarget = "TestEntity",
            TargetId = "123",
            Description = "创建测试实体",
            Result = "Success"
        };

        // Act
        var logId = await _auditService.LogOperationAsync(request, "user1", "测试用户", "127.0.0.1");

        // Assert
        Assert.True(logId > 0);
    }

    [Fact]
    public async Task QueryOperationLogsAsync_ReturnsFilteredLogs()
    {
        // Arrange
        var request1 = new CreateOperationLogRequest
        {
            ModuleId = "Module1",
            OperationType = "Create",
            Result = "Success"
        };
        var request2 = new CreateOperationLogRequest
        {
            ModuleId = "Module2",
            OperationType = "Update",
            Result = "Success"
        };

        await _auditService.LogOperationAsync(request1, "user1", "用户1", "127.0.0.1");
        await _auditService.LogOperationAsync(request2, "user2", "用户2", "127.0.0.2");

        // Act
        var query = new QueryOperationLogRequest
        {
            ModuleId = "Module1",
            PageIndex = 1,
            PageSize = 10
        };
        var (items, total) = await _auditService.QueryOperationLogsAsync(query);

        // Assert
        Assert.Equal(1, total);
        Assert.Single(items);
        Assert.Equal("Module1", items[0].ModuleId);
    }

    [Fact]
    public async Task LogAccessAsync_CreatesAccessLog()
    {
        // Arrange
        var accessLog = new AccessLog
        {
            AccessTime = DateTime.UtcNow,
            UserId = "user1",
            UserName = "测试用户",
            RequestPath = "/api/test",
            HttpMethod = "GET",
            StatusCode = 200,
            ResponseTime = 150,
            ClientIp = "127.0.0.1"
        };

        // Act
        var logId = await _auditService.LogAccessAsync(accessLog);

        // Assert
        Assert.True(logId > 0);
    }

    [Fact]
    public async Task QueryAccessLogsAsync_ReturnsFilteredLogs()
    {
        // Arrange
        var log1 = new AccessLog
        {
            AccessTime = DateTime.UtcNow,
            UserId = "user1",
            RequestPath = "/api/test1",
            HttpMethod = "GET",
            StatusCode = 200,
            ResponseTime = 100
        };
        var log2 = new AccessLog
        {
            AccessTime = DateTime.UtcNow,
            UserId = "user2",
            RequestPath = "/api/test2",
            HttpMethod = "POST",
            StatusCode = 500,
            ResponseTime = 200
        };

        await _auditService.LogAccessAsync(log1);
        await _auditService.LogAccessAsync(log2);

        // Act
        var query = new QueryAccessLogRequest
        {
            MinStatusCode = 400,
            PageIndex = 1,
            PageSize = 10
        };
        var (items, total) = await _auditService.QueryAccessLogsAsync(query);

        // Assert
        Assert.Equal(1, total);
        Assert.Single(items);
        Assert.Equal(500, items[0].StatusCode);
    }

    [Fact]
    public async Task QueryOperationLogsAsync_SupportsPagination()
    {
        // Arrange
        for (int i = 0; i < 25; i++)
        {
            var request = new CreateOperationLogRequest
            {
                ModuleId = "TestModule",
                OperationType = "Create",
                Result = "Success"
            };
            await _auditService.LogOperationAsync(request, $"user{i}", $"用户{i}", "127.0.0.1");
        }

        // Act - 第一页
        var query1 = new QueryOperationLogRequest { PageIndex = 1, PageSize = 10 };
        var (items1, total1) = await _auditService.QueryOperationLogsAsync(query1);

        // Act - 第二页
        var query2 = new QueryOperationLogRequest { PageIndex = 2, PageSize = 10 };
        var (items2, total2) = await _auditService.QueryOperationLogsAsync(query2);

        // Assert
        Assert.Equal(25, total1);
        Assert.Equal(25, total2);
        Assert.Equal(10, items1.Count);
        Assert.Equal(10, items2.Count);
    }
}
