using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class checkpoint_store_tweak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CommitPosition",
                table: "SubscriptionCheckpoints",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommitPosition",
                table: "SubscriptionCheckpoints");
        }
    }
}
