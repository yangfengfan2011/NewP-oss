namespace ICP.Platform.Modules.Example;

/// <summary>
/// Example 模块拥有的示例记录权限代码与 Standard CRUD stem。
/// </summary>
public static class ExamplePermissions
{
    public const string RecordStem = "Example.Record";
    public const string RecordView = RecordStem + ".View";
    public const string RecordCreate = RecordStem + ".Create";
    public const string RecordUpdate = RecordStem + ".Update";
    public const string RecordDelete = RecordStem + ".Delete";
}
