using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class park_soft_delete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SoftDeleteParked",
                table: "SubscriptionConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ParkedEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SoftDeleteParked",
                table: "SubscriptionConfigurations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ParkedEvents");
        }
    }
}
