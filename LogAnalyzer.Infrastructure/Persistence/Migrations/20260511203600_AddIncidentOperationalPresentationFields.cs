using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogAnalyzer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentOperationalPresentationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceLogExcerpt",
                table: "Incidents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalTitle",
                table: "Incidents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceLogExcerpt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "OperationalTitle",
                table: "Incidents");
        }
    }
}
