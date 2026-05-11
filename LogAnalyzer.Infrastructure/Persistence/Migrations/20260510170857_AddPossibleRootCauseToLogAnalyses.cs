using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogAnalyzer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPossibleRootCauseToLogAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PossibleRootCause",
                table: "LogAnalyses",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PossibleRootCause",
                table: "LogAnalyses");
        }
    }
}
