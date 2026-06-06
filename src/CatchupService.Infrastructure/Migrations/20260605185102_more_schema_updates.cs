using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class more_schema_updates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Checkpoint",
                table: "SubscriptionCheckpoints");

            migrationBuilder.AddColumn<string>(
                name: "CheckpointReason",
                table: "SubscriptionCheckpoints",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckpointReason",
                table: "SubscriptionCheckpoints");

            migrationBuilder.AddColumn<long>(
                name: "Checkpoint",
                table: "SubscriptionCheckpoints",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
