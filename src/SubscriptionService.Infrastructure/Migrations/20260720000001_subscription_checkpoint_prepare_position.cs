using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations
{
    [DbContext(typeof(CatchupServiceDbContext))]
    [Migration("20260720000001_subscription_checkpoint_prepare_position")]
    /// <inheritdoc />
    public partial class subscription_checkpoint_prepare_position : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[SubscriptionCheckpoints]', N'PreparePosition') IS NULL
BEGIN
    ALTER TABLE [SubscriptionCheckpoints]
    ADD [PreparePosition] bigint NULL;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'[SubscriptionCheckpoints]', N'PreparePosition') IS NULL
    RETURN;

ALTER TABLE [SubscriptionCheckpoints]
DROP COLUMN [PreparePosition];");
        }
    }
}
