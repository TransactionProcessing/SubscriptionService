using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class subscription_progress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ProcessedCount",
                table: "SubscriptionCheckpoints",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "StreamEventCounts",
                columns: table => new
                {
                    SecondaryIndexName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TotalCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamEventCounts", x => x.SecondaryIndexName);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StreamEventCounts");

            migrationBuilder.DropColumn(
                name: "ProcessedCount",
                table: "SubscriptionCheckpoints");
        }
    }
}
