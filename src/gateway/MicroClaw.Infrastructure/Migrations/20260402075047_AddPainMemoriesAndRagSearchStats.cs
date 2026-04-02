using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPainMemoriesAndRagSearchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pain_memories");

            migrationBuilder.DropTable(
                name: "rag_search_stats");
        }
    }
}
