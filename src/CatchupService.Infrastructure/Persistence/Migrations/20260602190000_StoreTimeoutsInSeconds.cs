using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Persistence.Migrations;

public partial class StoreTimeoutsInSeconds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "RequestTimeoutTicks",
            table: "SubscriptionConfigurations",
            newName: "RequestTimeoutSeconds");

        migrationBuilder.RenameColumn(
            name: "RetryDelayTicks",
            table: "SubscriptionConfigurations",
            newName: "RetryDelaySeconds");

        migrationBuilder.DropColumn(
            name: "PollIntervalTicks",
            table: "SubscriptionConfigurations");

        migrationBuilder.Sql("""
            UPDATE dbo.SubscriptionConfigurations
            SET
                RequestTimeoutSeconds = CASE WHEN RequestTimeoutSeconds = 0 THEN 0 ELSE (RequestTimeoutSeconds + 9999999) / 10000000 END,
                RetryDelaySeconds = CASE WHEN RetryDelaySeconds = 0 THEN 0 ELSE (RetryDelaySeconds + 9999999) / 10000000 END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE dbo.SubscriptionConfigurations
            SET
                RequestTimeoutSeconds = RequestTimeoutSeconds * 10000000,
                RetryDelaySeconds = RetryDelaySeconds * 10000000;
            """);

        migrationBuilder.RenameColumn(
            name: "RequestTimeoutSeconds",
            table: "SubscriptionConfigurations",
            newName: "RequestTimeoutTicks");

        migrationBuilder.RenameColumn(
            name: "RetryDelaySeconds",
            table: "SubscriptionConfigurations",
            newName: "RetryDelayTicks");

        migrationBuilder.AddColumn<long>(
            name: "PollIntervalTicks",
            table: "SubscriptionConfigurations",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
    }
}
