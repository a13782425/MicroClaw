using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorAgentToolGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider_id",
                table: "agents");

            migrationBuilder.AddColumn<string>(
                name: "tool_group_configs_json",
                table: "agents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tool_group_configs_json",
                table: "agents");

            migrationBuilder.AddColumn<string>(
                name: "provider_id",
                table: "agents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
