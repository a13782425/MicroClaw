using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroClaw.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedSubAgentIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedSubAgentIdsJson",
                table: "agents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedSubAgentIdsJson",
                table: "agents");
        }
    }
}
