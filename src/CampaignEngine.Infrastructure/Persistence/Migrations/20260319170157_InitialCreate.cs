using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampaignEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RateLimitPerMinute = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    EncryptedConnectionString = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    IsSubTemplate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    ResolvedHtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DataSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FilterExpression = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FreeFieldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalRecipients = table.Column<int>(type: "int", nullable: false),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false),
                    SuccessCount = table.Column<int>(type: "int", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    StaticCcAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DynamicCcField = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StaticBccAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DataSourceFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsFilterable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsRecipientAddress = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSourceFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataSourceFields_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceholderManifests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsFromDataSource = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceholderManifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceholderManifests_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TemplateHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateHistory_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDynamic = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DynamicFieldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignAttachments_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelayDays = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    StepFilter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TemplateSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignSteps_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignSteps_TemplateSnapshots_TemplateSnapshotId",
                        column: x => x.TemplateSnapshotId,
                        principalTable: "TemplateSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SendLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RecipientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ErrorDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SendLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SendLogs_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_IsActive",
                table: "ApiKeys",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Name",
                table: "ApiKeys",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAttachments_CampaignId",
                table: "CampaignAttachments",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_DataSourceId",
                table: "Campaigns",
                column: "DataSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Name",
                table: "Campaigns",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSteps_CampaignId_StepOrder",
                table: "CampaignSteps",
                columns: new[] { "CampaignId", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSteps_TemplateSnapshotId",
                table: "CampaignSteps",
                column: "TemplateSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSourceFields_DataSourceId_FieldName",
                table: "DataSourceFields",
                columns: new[] { "DataSourceId", "FieldName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_Name",
                table: "DataSources",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaceholderManifests_TemplateId_Key",
                table: "PlaceholderManifests",
                columns: new[] { "TemplateId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_CampaignId",
                table: "SendLogs",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_CorrelationId",
                table: "SendLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_CreatedAt",
                table: "SendLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_Status",
                table: "SendLogs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateHistory_TemplateId_Version",
                table: "TemplateHistory",
                columns: new[] { "TemplateId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_Name_Channel",
                table: "Templates",
                columns: new[] { "Name", "Channel" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateSnapshots_OriginalTemplateId",
                table: "TemplateSnapshots",
                column: "OriginalTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "CampaignAttachments");

            migrationBuilder.DropTable(
                name: "CampaignSteps");

            migrationBuilder.DropTable(
                name: "DataSourceFields");

            migrationBuilder.DropTable(
                name: "PlaceholderManifests");

            migrationBuilder.DropTable(
                name: "SendLogs");

            migrationBuilder.DropTable(
                name: "TemplateHistory");

            migrationBuilder.DropTable(
                name: "TemplateSnapshots");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "Templates");

            migrationBuilder.DropTable(
                name: "DataSources");
        }
    }
}
