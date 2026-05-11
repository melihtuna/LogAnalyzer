using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LogAnalyzer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchIncidentCandidatesCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BatchIncidentCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BatchHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContractVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CandidatesJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchIncidentCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchIncidentCandidates_BatchHash_ContractVersion",
                table: "BatchIncidentCandidates",
                columns: new[] { "BatchHash", "ContractVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchIncidentCandidates");
        }
    }
}
