using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpServerSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plugin_id",
                table: "mcp_server_configs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plugin_name",
                table: "mcp_server_configs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                table: "mcp_server_configs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "plugin_id",
                table: "mcp_server_configs");

            migrationBuilder.DropColumn(
                name: "plugin_name",
                table: "mcp_server_configs");

            migrationBuilder.DropColumn(
                name: "source",
                table: "mcp_server_configs");
        }
    }
}
