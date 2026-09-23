using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
[Migration("20260716160000_subscription_operational_state")]
public partial class subscription_operational_state : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OperationalReason",
            table: "SubscriptionConfigurations",
            type: "nvarchar(4000)",
            maxLength: 4000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OperationalState",
            table: "SubscriptionConfigurations",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Healthy");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "OperationalReason",
            table: "SubscriptionConfigurations");

        migrationBuilder.DropColumn(
            name: "OperationalState",
            table: "SubscriptionConfigurations");
    }
}
