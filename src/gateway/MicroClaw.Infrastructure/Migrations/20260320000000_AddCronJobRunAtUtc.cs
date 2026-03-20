using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCronJobRunAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 新增 run_at_utc 列（一次性任务触发时间，与 cron_expression 互斥）
            migrationBuilder.AddColumn<string>(
                name: "run_at_utc",
                table: "cron_jobs",
                type: "TEXT",
                nullable: true);

            // SQLite 不支持直接修改列约束，通过 AlterColumn 标记 cron_expression 为可空
            migrationBuilder.AlterColumn<string>(
                name: "cron_expression",
                table: "cron_jobs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "run_at_utc",
                table: "cron_jobs");

            migrationBuilder.AlterColumn<string>(
                name: "cron_expression",
                table: "cron_jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
