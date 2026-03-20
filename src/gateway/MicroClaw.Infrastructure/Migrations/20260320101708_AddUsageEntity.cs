using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    input_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    output_tokens = table.Column<int>(type: "INTEGER", nullable: false),
                    input_price_per_m_token = table.Column<decimal>(type: "TEXT", nullable: true),
                    output_price_per_m_token = table.Column<decimal>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usages_created_at_utc",
                table: "usages",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usages");
        }
    }
}
