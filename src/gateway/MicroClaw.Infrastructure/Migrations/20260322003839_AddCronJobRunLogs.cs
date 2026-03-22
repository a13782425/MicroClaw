using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCronJobRunLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cron_job_run_logs",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    cron_job_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    triggered_at_utc = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    duration_ms = table.Column<long>(type: "INTEGER", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cron_job_run_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cron_job_run_logs_cron_job_id",
                table: "cron_job_run_logs",
                column: "cron_job_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cron_job_run_logs");
        }
    }
}
