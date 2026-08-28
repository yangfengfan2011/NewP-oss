using Microsoft.EntityFrameworkCore.Migrations;

using ICP.Platform.Persistence.EfCore.Migrations;

#nullable disable

namespace ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Example_000004_RecordSourceBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 旧行只在迁移阶段补齐兼容值；该操作不会为列创建永久数据库 DEFAULT。
            migrationBuilder.BackfillRequiredField(
                "Example",
                "ExampleRecord",
                "RecordSource",
                "Legacy",
                "dbo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
