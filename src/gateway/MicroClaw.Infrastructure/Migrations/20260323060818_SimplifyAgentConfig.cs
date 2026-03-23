using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyAgentConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "system_prompt",
                table: "agents",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "mcp_servers_json",
                table: "agents",
                newName: "enabled_mcp_server_ids_json");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "enabled_mcp_server_ids_json",
                table: "agents",
                newName: "mcp_servers_json");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "agents",
                newName: "system_prompt");
        }
    }
}
