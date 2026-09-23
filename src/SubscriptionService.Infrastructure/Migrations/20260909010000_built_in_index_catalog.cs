using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
[Migration("20260909010000_built_in_index_catalog")]
public partial class built_in_index_catalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BuiltInIndexCatalog",
            columns: table => new
            {
                Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                LastSeenCommitPosition = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_BuiltInIndexCatalog", x => x.Name));
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "BuiltInIndexCatalog");
}
