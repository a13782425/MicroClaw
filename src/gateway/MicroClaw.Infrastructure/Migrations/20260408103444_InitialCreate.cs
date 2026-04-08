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
                name: "pain_memories",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    agent_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    trigger_description = table.Column<string>(type: "TEXT", nullable: false),
                    consequence_description = table.Column<string>(type: "TEXT", nullable: false),
                    avoidance_strategy = table.Column<string>(type: "TEXT", nullable: false),
                    severity = table.Column<int>(type: "INTEGER", nullable: false),
                    occurrence_count = table.Column<int>(type: "INTEGER", nullable: false),
                    last_occurred_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pain_memories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rag_search_stats",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    elapsed_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    recall_count = table.Column<int>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_search_stats", x => x.id);
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
                    agent_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
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
                name: "ix_pain_memories_agent_id",
                table: "pain_memories",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_pain_memories_agent_severity",
                table: "pain_memories",
                columns: new[] { "agent_id", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_rag_search_stats_recorded_at_ms",
                table: "rag_search_stats",
                column: "recorded_at_ms");

            migrationBuilder.CreateIndex(
                name: "ix_rag_search_stats_scope",
                table: "rag_search_stats",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "ix_usages_agent_session_provider_source_day",
                table: "usages",
                columns: new[] { "agent_id", "session_id", "provider_id", "source", "day_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_retry_queue");

            migrationBuilder.DropTable(
                name: "cron_job_run_logs");

            migrationBuilder.DropTable(
                name: "cron_jobs");

            migrationBuilder.DropTable(
                name: "pain_memories");

            migrationBuilder.DropTable(
                name: "rag_search_stats");

            migrationBuilder.DropTable(
                name: "usages");
        }
    }
}
