using Microsoft.Data.SqlClient;

namespace ICP.Platform.Acceptance;

internal sealed partial class PublishedComponentAcceptanceFixture
{
    // 仅供专用维护路径登录；凭据已由PrepareAsync登记，普通actor的权限与身份不变。
    internal WorkspaceAcceptanceActor Administrator => _administrator ?? throw new InvalidOperationException("正式管理员尚未准备。");

    /// <summary>在本轮独占库中准备三种真实对象的缺失旧值；升级连续性另由真实历史Host测试证明。</summary>
    internal async Task PrepareOwnershipLegacyRowsAsync(CancellationToken token)
    {
        if (_database is null || _oldFolderId is null || _oldPartId is null || _oldDocumentId is null)
            throw new InvalidOperationException("归属路径需要本轮已创建的三种真实对象。");
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE WorkspaceFolder SET OwnerId=NULL,OwnerDepartmentId=NULL WHERE EID=@folder;
            UPDATE Part SET OwnerId=NULL,OwnerDepartmentId=NULL WHERE EID=@part;
            UPDATE Document SET OwnerId=NULL,OwnerDepartmentId=NULL,CreatedBy=N'ownership-missing-creator' WHERE EID=@document;
            """;
        command.Parameters.AddWithValue("@folder", _oldFolderId);
        command.Parameters.AddWithValue("@part", _oldPartId);
        command.Parameters.AddWithValue("@document", _oldDocumentId);
        if (await command.ExecuteNonQueryAsync(token) != 3) throw new InvalidOperationException("归属旧值准备未精确命中本轮三种对象。");
    }
}
