using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class daily_checkpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyCommitPositions",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SecondaryIndexName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommitPosition = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCommitPositions", x => new { x.SubscriptionId, x.SecondaryIndexName, x.Date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCommitPositions");
        }
    }
}
