using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    bound_skill_ids_json = table.Column<string>(type: "TEXT", nullable: true),
                    enabled_mcp_server_ids_json = table.Column<string>(type: "TEXT", nullable: true),
                    tool_group_configs_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                    context_window_messages = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channel_retry_queue",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    channel_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    channel_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    message_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_text = table.Column<string>(type: "TEXT", nullable: false),
                    retry_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    next_retry_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    last_error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_retry_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    channel_type = table.Column<string>(type: "TEXT", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    settings_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cron_job_run_logs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    cron_job_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    triggered_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    duration_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cron_job_run_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cron_jobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    cron_expression = table.Column<string>(type: "TEXT", nullable: true),
                    run_at_ms = table.Column<long>(type: "INTEGER", nullable: true),
                    target_session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    prompt = table.Column<string>(type: "TEXT", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    last_run_at_ms = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cron_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mcp_server_configs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    transport_type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    command = table.Column<string>(type: "TEXT", nullable: true),
                    args_json = table.Column<string>(type: "TEXT", nullable: true),
                    env_json = table.Column<string>(type: "TEXT", nullable: true),
                    url = table.Column<string>(type: "TEXT", nullable: true),
                    headers_json = table.Column<string>(type: "TEXT", nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
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
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    protocol = table.Column<string>(type: "TEXT", nullable: false),
                    base_url = table.Column<string>(type: "TEXT", nullable: true),
                    api_key = table.Column<string>(type: "TEXT", nullable: false),
                    model_name = table.Column<string>(type: "TEXT", nullable: false),
                    max_output_tokens = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 8192),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    capabilities_json = table.Column<string>(type: "TEXT", nullable: true)
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
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
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
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", nullable: false),
                    is_approved = table.Column<bool>(type: "INTEGER", nullable: false),
                    channel_type = table.Column<string>(type: "TEXT", nullable: false),
                    channel_id = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    agent_id = table.Column<string>(type: "TEXT", nullable: true),
                    parent_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    approval_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skills", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usages",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    provider_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    provider_name = table.Column<string>(type: "TEXT", nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    input_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    output_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    cached_input_tokens = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    day_number = table.Column<int>(type: "INTEGER", nullable: false),
                    input_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    output_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    cache_input_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    cache_output_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_retry_queue_message_id",
                table: "channel_retry_queue",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channel_retry_queue_status",
                table: "channel_retry_queue",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_cron_job_run_logs_cron_job_id",
                table: "cron_job_run_logs",
                column: "cron_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_rag_configs_scope",
                table: "rag_configs",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "ix_usages_session_provider_source_day",
                table: "usages",
                columns: new[] { "session_id", "provider_id", "source", "day_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "channel_retry_queue");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "cron_job_run_logs");

            migrationBuilder.DropTable(
                name: "cron_jobs");

            migrationBuilder.DropTable(
                name: "mcp_server_configs");

            migrationBuilder.DropTable(
                name: "providers");

            migrationBuilder.DropTable(
                name: "rag_configs");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "skills");

            migrationBuilder.DropTable(
                name: "usages");
        }
    }
}
