using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropEmotionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emotion_snapshots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "emotion_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    agent_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    alertness = table.Column<int>(type: "INTEGER", nullable: false),
                    confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    curiosity = table.Column<int>(type: "INTEGER", nullable: false),
                    mood = table.Column<int>(type: "INTEGER", nullable: false),
                    recorded_at_ms = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_emotion_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_emotion_snapshots_agent_id",
                table: "emotion_snapshots",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_emotion_snapshots_agent_recorded",
                table: "emotion_snapshots",
                columns: new[] { "agent_id", "recorded_at_ms" });
        }
    }
}
