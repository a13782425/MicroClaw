using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowDefaultProviderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_provider_id",
                table: "workflows",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_provider_id",
                table: "workflows");
        }
    }
}
