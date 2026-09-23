using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
[Migration("20260914130000_subscription_event_logging")]
public partial class subscription_event_logging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EnableEventLogging",
            table: "SubscriptionConfigurations",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "SubscriptionEventLogs",
            columns: table => new
            {
                SubscriptionEventLogId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AddressSentTo = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                ResponseStatusCode = table.Column<int>(type: "int", nullable: true),
                EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DeliveryAttempt = table.Column<int>(type: "int", nullable: false),
                IsReplay = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_SubscriptionEventLogs", x => x.SubscriptionEventLogId));

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionEventLogs_SubscriptionId_ProcessedAt",
            table: "SubscriptionEventLogs",
            columns: new[] { "SubscriptionId", "ProcessedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SubscriptionEventLogs");
        migrationBuilder.DropColumn(name: "EnableEventLogging", table: "SubscriptionConfigurations");
    }
}
