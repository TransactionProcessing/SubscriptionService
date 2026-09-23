using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SubscriptionService.Infrastructure.Persistence;

#nullable disable

namespace CatchupService.Infrastructure.Migrations;

[DbContext(typeof(CatchupServiceDbContext))]
[Migration("20260904000000_managed_endpoints")]
public partial class managed_endpoints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE [Endpoints] (
    [EndpointId] int IDENTITY(1,1) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Url] nvarchar(2048) NOT NULL,
    [AuthenticationScheme] nvarchar(200) NULL,
    [AuthenticationParametersJson] nvarchar(max) NULL,
    CONSTRAINT [PK_Endpoints] PRIMARY KEY ([EndpointId])
);

INSERT INTO [Endpoints] ([Name], [Url], [AuthenticationScheme], [AuthenticationParametersJson])
SELECT [SubscriptionId], [EndpointUrl], [AuthenticationScheme], [AuthenticationParametersJson]
FROM [SubscriptionConfigurations];

ALTER TABLE [SubscriptionConfigurations] ADD [EndpointId] int NULL;
UPDATE sc SET [EndpointId] = e.[EndpointId]
FROM [SubscriptionConfigurations] sc
JOIN [Endpoints] e ON e.[Name] = sc.[SubscriptionId];
ALTER TABLE [SubscriptionConfigurations] ALTER COLUMN [EndpointId] int NOT NULL;
ALTER TABLE [SubscriptionConfigurations] ADD CONSTRAINT [FK_SubscriptionConfigurations_Endpoints_EndpointId] FOREIGN KEY ([EndpointId]) REFERENCES [Endpoints] ([EndpointId]);
CREATE INDEX [IX_SubscriptionConfigurations_EndpointId] ON [SubscriptionConfigurations] ([EndpointId]);
ALTER TABLE [SubscriptionConfigurations] DROP COLUMN [EndpointUrl], [AuthenticationScheme], [AuthenticationParametersJson];");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(@"
ALTER TABLE [SubscriptionConfigurations] ADD [EndpointUrl] nvarchar(2048) NOT NULL DEFAULT '';
ALTER TABLE [SubscriptionConfigurations] ADD [AuthenticationScheme] nvarchar(200) NULL;
ALTER TABLE [SubscriptionConfigurations] ADD [AuthenticationParametersJson] nvarchar(max) NULL;
UPDATE sc SET [EndpointUrl] = e.[Url], [AuthenticationScheme] = e.[AuthenticationScheme], [AuthenticationParametersJson] = e.[AuthenticationParametersJson]
FROM [SubscriptionConfigurations] sc JOIN [Endpoints] e ON e.[EndpointId] = sc.[EndpointId];
ALTER TABLE [SubscriptionConfigurations] DROP CONSTRAINT [FK_SubscriptionConfigurations_Endpoints_EndpointId];
DROP INDEX [IX_SubscriptionConfigurations_EndpointId] ON [SubscriptionConfigurations];
ALTER TABLE [SubscriptionConfigurations] DROP COLUMN [EndpointId];
DROP TABLE [Endpoints];");
}
