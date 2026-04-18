using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRagSearchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rag_search_stats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rag_search_stats",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    elapsed_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    recall_count = table.Column<int>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_search_stats", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rag_search_stats_recorded_at_ms",
                table: "rag_search_stats",
                column: "recorded_at_ms");

            migrationBuilder.CreateIndex(
                name: "ix_rag_search_stats_scope",
                table: "rag_search_stats",
                column: "scope");
        }
    }
}
