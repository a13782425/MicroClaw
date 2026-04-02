using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropYamlMigratedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "mcp_server_configs");

            migrationBuilder.DropTable(
                name: "providers");

            migrationBuilder.DropTable(
                name: "rag_configs");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "workflows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AllowedSubAgentIdsJson = table.Column<string>(type: "TEXT", nullable: true),
                    context_window_messages = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    disabled_mcp_server_ids_json = table.Column<string>(type: "TEXT", nullable: true),
                    disabled_skill_ids_json = table.Column<string>(type: "TEXT", nullable: true),
                    expose_as_a2a = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    monthly_budget_usd = table.Column<decimal>(type: "TEXT", nullable: true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    routing_strategy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SourcePlugin = table.Column<string>(type: "TEXT", nullable: true),
                    tool_group_configs_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    channel_type = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    settings_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_server_configs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    args_json = table.Column<string>(type: "TEXT", nullable: true),
                    command = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    env_json = table.Column<string>(type: "TEXT", nullable: true),
                    headers_json = table.Column<string>(type: "TEXT", nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    plugin_id = table.Column<string>(type: "TEXT", nullable: true),
                    plugin_name = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    transport_type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    url = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_server_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "providers",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    api_key = table.Column<string>(type: "TEXT", nullable: false),
                    base_url = table.Column<string>(type: "TEXT", nullable: true),
                    capabilities_json = table.Column<string>(type: "TEXT", nullable: true),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    max_output_tokens = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 8192),
                    model_name = table.Column<string>(type: "TEXT", nullable: false),
                    model_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "chat"),
                    protocol = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rag_configs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    agent_id = table.Column<string>(type: "TEXT", nullable: true),
                    approval_reason = table.Column<string>(type: "TEXT", nullable: true),
                    channel_id = table.Column<string>(type: "TEXT", nullable: false),
                    channel_type = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    is_approved = table.Column<bool>(type: "INTEGER", nullable: false),
                    parent_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    provider_id = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "workflows",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    default_provider_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    edges_json = table.Column<string>(type: "TEXT", nullable: true),
                    entry_node_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    nodes_json = table.Column<string>(type: "TEXT", nullable: true),
                    updated_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rag_configs_scope",
                table: "rag_configs",
                column: "scope");
        }
    }
}
