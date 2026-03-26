using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflows",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    nodes_json = table.Column<string>(type: "TEXT", nullable: true),
                    edges_json = table.Column<string>(type: "TEXT", nullable: true),
                    entry_node_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflows");
        }
    }
}
