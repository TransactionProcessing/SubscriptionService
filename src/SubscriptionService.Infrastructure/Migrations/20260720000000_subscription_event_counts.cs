using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
[Migration("20260720000000_subscription_event_counts")]
public partial class subscription_event_counts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_StreamEventCounts",
            table: "StreamEventCounts");

        migrationBuilder.RenameTable(
            name: "StreamEventCounts",
            newName: "StreamEventCounts_Old");

        migrationBuilder.CreateTable(
            name: "StreamEventCounts",
            columns: table => new
            {
                SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                SecondaryIndexName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TotalCount = table.Column<long>(type: "bigint", nullable: false),
                LastScannedCommitPosition = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StreamEventCounts", x => x.SubscriptionId);
            });

        migrationBuilder.Sql(@"
INSERT INTO [StreamEventCounts] (
    [SubscriptionId],
    [SecondaryIndexName],
    [TotalCount],
    [LastScannedCommitPosition],
    [UpdatedAt])
SELECT
    [sc].[SubscriptionId],
    [sc].[SecondaryIndexName],
    COALESCE([old].[TotalCount], 0),
    [old].[LastScannedCommitPosition],
    COALESCE([old].[UpdatedAt], SYSUTCDATETIME())
FROM [SubscriptionConfigurations] AS [sc]
LEFT JOIN [StreamEventCounts_Old] AS [old]
    ON [old].[SecondaryIndexName] = [sc].[SecondaryIndexName];
");

        migrationBuilder.DropTable(
            name: "StreamEventCounts_Old");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_StreamEventCounts",
            table: "StreamEventCounts");

        migrationBuilder.RenameTable(
            name: "StreamEventCounts",
            newName: "StreamEventCounts_New");

        migrationBuilder.CreateTable(
            name: "StreamEventCounts",
            columns: table => new
            {
                SecondaryIndexName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TotalCount = table.Column<long>(type: "bigint", nullable: false),
                LastScannedCommitPosition = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StreamEventCounts", x => x.SecondaryIndexName);
            });

        migrationBuilder.Sql(@"
INSERT INTO [StreamEventCounts] (
    [SecondaryIndexName],
    [TotalCount],
    [LastScannedCommitPosition],
    [UpdatedAt])
SELECT
    [SecondaryIndexName],
    MAX([TotalCount]),
    MAX([LastScannedCommitPosition]),
    MAX([UpdatedAt])
FROM [StreamEventCounts_New]
GROUP BY [SecondaryIndexName];
");

        migrationBuilder.DropTable(
            name: "StreamEventCounts_New");
    }
}
