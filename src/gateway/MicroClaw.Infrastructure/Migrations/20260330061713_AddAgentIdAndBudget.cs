using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentIdAndBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usages_session_provider_source_day",
                table: "usages");

            migrationBuilder.AddColumn<string>(
                name: "agent_id",
                table: "usages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_budget_usd",
                table: "agents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_usages_agent_session_provider_source_day",
                table: "usages",
                columns: new[] { "agent_id", "session_id", "provider_id", "source", "day_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usages_agent_session_provider_source_day",
                table: "usages");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "usages");

            migrationBuilder.DropColumn(
                name: "monthly_budget_usd",
                table: "agents");

            migrationBuilder.CreateIndex(
                name: "ix_usages_session_provider_source_day",
                table: "usages",
                columns: new[] { "session_id", "provider_id", "source", "day_number" },
                unique: true);
        }
    }
}
