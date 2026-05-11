using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LogAnalyzer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IncidentFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FingerprintVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrimaryGroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrimaryLogHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TechnicalSummary = table.Column<string>(type: "text", nullable: false),
                    PossibleRootCause = table.Column<string>(type: "text", nullable: false),
                    RecommendedAction = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    AiModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PipelineVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExternalIssueKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalIssueUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LogHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalLog = table.Column<string>(type: "text", nullable: false),
                    ProcessedLog = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Suggestion = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogAnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawLogs = table.Column<string>(type: "text", nullable: false),
                    AnalysisResult = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogAnalysisRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogSourceCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastProcessedTimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogSourceCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IncidentLogLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IncidentId = table.Column<int>(type: "integer", nullable: false),
                    LogHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LinkedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentLogLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentLogLinks_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentLogLinks_IncidentId_LogHash",
                table: "IncidentLogLinks",
                columns: new[] { "IncidentId", "LogHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_IncidentFingerprint",
                table: "Incidents",
                column: "IncidentFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_IncidentFingerprint_Status_LastSeenUtc",
                table: "Incidents",
                columns: new[] { "IncidentFingerprint", "Status", "LastSeenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LogAnalyses_LogHash",
                table: "LogAnalyses",
                column: "LogHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogSourceCheckpoints_Source",
                table: "LogSourceCheckpoints",
                column: "Source",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentLogLinks");

            migrationBuilder.DropTable(
                name: "LogAnalyses");

            migrationBuilder.DropTable(
                name: "LogAnalysisRuns");

            migrationBuilder.DropTable(
                name: "LogSourceCheckpoints");

            migrationBuilder.DropTable(
                name: "Incidents");
        }
    }
}
