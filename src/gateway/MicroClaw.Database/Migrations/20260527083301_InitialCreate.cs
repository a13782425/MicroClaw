using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "call_daily",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    day_number = table.Column<int>(type: "INTEGER", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    provider_name = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    call_count = table.Column<long>(type: "INTEGER", nullable: false),
                    success_count = table.Column<long>(type: "INTEGER", nullable: false),
                    fail_count = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_call_daily", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "token_daily",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    day_number = table.Column<int>(type: "INTEGER", nullable: false),
                    provider_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    provider_name = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    input_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    output_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    cached_input_tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    input_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    output_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    cache_input_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    cache_output_cost_usd = table.Column<decimal>(type: "TEXT", nullable: false),
                    updated_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_daily", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_call_daily_day_provider_session_source",
                table: "call_daily",
                columns: new[] { "day_number", "provider_id", "session_id", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_token_daily_day_provider_session_source",
                table: "token_daily",
                columns: new[] { "day_number", "provider_id", "session_id", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "call_daily");

            migrationBuilder.DropTable(
                name: "token_daily");
        }
    }
}
