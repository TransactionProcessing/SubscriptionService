using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
[Migration("20260703090000_add_stream_event_count_checkpoint")]
public partial class add_stream_event_count_checkpoint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "LastScannedCommitPosition",
            table: "StreamEventCounts",
            type: "bigint",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastScannedCommitPosition",
            table: "StreamEventCounts");
    }
}
