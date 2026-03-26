using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentExposeAsA2A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "expose_as_a2a",
                table: "agents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expose_as_a2a",
                table: "agents");
        }
    }
}
