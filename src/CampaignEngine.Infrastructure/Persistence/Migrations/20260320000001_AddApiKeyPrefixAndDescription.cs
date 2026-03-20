using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampaignEngine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyPrefixAndDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyPrefix",
                table: "ApiKeys",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ApiKeys",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyPrefix",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ApiKeys");
        }
    }
}
