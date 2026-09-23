using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParkedEvents",
                columns: table => new
                {
                    ParkedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    StreamName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Payload = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkedEvents", x => x.ParkedEventId);
                });

            migrationBuilder.CreateTable(
                name: "ReplaySessions",
                columns: table => new
                {
                    ReplaySessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplaySessions", x => x.ReplaySessionId);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionCheckpoints",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Checkpoint = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionCheckpoints", x => x.SubscriptionId);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionConfigurations",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SecondaryIndexName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EndpointUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestTimeoutSeconds = table.Column<long>(type: "bigint", nullable: false),
                    RetryMaxAttempts = table.Column<int>(type: "int", nullable: false),
                    RetryDelaySeconds = table.Column<long>(type: "bigint", nullable: false),
                    CheckpointBatchSize = table.Column<int>(type: "int", nullable: false),
                    AuthenticationScheme = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AuthenticationParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionConfigurations", x => x.SubscriptionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParkedEvents_SubscriptionId_SequenceNumber",
                table: "ParkedEvents",
                columns: new[] { "SubscriptionId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplaySessions_SubscriptionId",
                table: "ReplaySessions",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParkedEvents");

            migrationBuilder.DropTable(
                name: "ReplaySessions");

            migrationBuilder.DropTable(
                name: "SubscriptionCheckpoints");

            migrationBuilder.DropTable(
                name: "SubscriptionConfigurations");
        }
    }
}
