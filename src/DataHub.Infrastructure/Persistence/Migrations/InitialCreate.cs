using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // migration builder arrays are tooling-style one-shots

namespace DataHub.Infrastructure.Persistence.Migrations;

/// <summary>
/// SQLite bootstrap for the demo profile. Hand-authored migration (the
/// FinTrack/ModernForms pattern): reviewed like code; the SQL Server/Azure
/// SQL production profile is documented in docs/architecture.md § Persistence.
/// </summary>
[DbContext(typeof(DataHubDbContext))]
[Migration("0001_initial_create")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── data_source ──────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "data_source",
            columns: table => new
            {
                data_source_id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                source_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                display_name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                connection_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                is_enabled = table.Column<int>(type: "INTEGER", nullable: false),
                command_timeout_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                registered_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_data_source", x => x.data_source_id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_data_source_key",
            table: "data_source",
            column: "source_key",
            unique: true);

        // ── report_definition ────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "report_definition",
            columns: table => new
            {
                report_id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                report_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                display_name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                source_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                query_text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                is_cacheable = table.Column<int>(type: "INTEGER", nullable: false),
                cache_seconds_ttl = table.Column<int>(type: "INTEGER", nullable: false),
                sync_interval_minutes = table.Column<int>(type: "INTEGER", nullable: true),
                registered_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_report_definition", x => x.report_id);
            });

        migrationBuilder.CreateIndex(
            name: "ux_report_key",
            table: "report_definition",
            column: "report_key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_report_source",
            table: "report_definition",
            column: "source_key");

        // ── report_permission ────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "report_permission",
            columns: table => new
            {
                report_permission_id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                report_id = table.Column<int>(type: "INTEGER", nullable: false),
                role_name = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_report_permission", x => x.report_permission_id);
                table.ForeignKey(
                    name: "fk_report_permission_report",
                    column: x => x.report_id,
                    principalTable: "report_definition",
                    principalColumn: "report_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_report_permission_report",
            table: "report_permission",
            column: "report_id");

        migrationBuilder.CreateIndex(
            name: "ux_report_permission",
            table: "report_permission",
            columns: ["report_id", "role_name"],
            unique: true);

        // ── sync_run ─────────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "sync_run",
            columns: table => new
            {
                sync_run_id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                report_key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                blob_path = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                row_count = table.Column<int>(type: "INTEGER", nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                triggered_by = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                started_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                completed_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                failure_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_run", x => x.sync_run_id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_sync_run_report_started",
            table: "sync_run",
            columns: ["report_key", "started_utc"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sync_run");
        migrationBuilder.DropTable("report_permission");
        migrationBuilder.DropTable("report_definition");
        migrationBuilder.DropTable("data_source");
    }
}
