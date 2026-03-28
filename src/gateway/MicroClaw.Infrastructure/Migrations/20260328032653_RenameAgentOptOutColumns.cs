using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAgentOptOutColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "bound_skill_ids_json",
                table: "agents",
                newName: "disabled_skill_ids_json");

            migrationBuilder.RenameColumn(
                name: "enabled_mcp_server_ids_json",
                table: "agents",
                newName: "disabled_mcp_server_ids_json");

            // 语义从"启用/绑定列表"变为"禁用列表"，旧数据不兼容，清空为 NULL（= 全部启用）
            migrationBuilder.Sql("UPDATE agents SET disabled_skill_ids_json = NULL, disabled_mcp_server_ids_json = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "disabled_skill_ids_json",
                table: "agents",
                newName: "bound_skill_ids_json");

            migrationBuilder.RenameColumn(
                name: "disabled_mcp_server_ids_json",
                table: "agents",
                newName: "enabled_mcp_server_ids_json");
        }
    }
}
