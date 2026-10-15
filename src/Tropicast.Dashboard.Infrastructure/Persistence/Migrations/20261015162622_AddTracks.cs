using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_dj",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "auto_dj_scheduling",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "storage_quota_bytes",
                table: "plans",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    artist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tracks_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: "free",
                columns: new[] { "auto_dj", "auto_dj_scheduling", "storage_quota_bytes" },
                values: new object[] { false, false, 0L });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: "growth",
                columns: new[] { "auto_dj", "auto_dj_scheduling", "storage_quota_bytes" },
                values: new object[] { true, true, 5368709120L });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: "pro",
                columns: new[] { "auto_dj", "auto_dj_scheduling", "storage_quota_bytes" },
                values: new object[] { true, true, 21474836480L });

            migrationBuilder.UpdateData(
                table: "plans",
                keyColumn: "id",
                keyValue: "starter",
                columns: new[] { "auto_dj", "auto_dj_scheduling", "storage_quota_bytes" },
                values: new object[] { true, false, 1073741824L });

            migrationBuilder.CreateIndex(
                name: "ix_tracks_object_key",
                table: "tracks",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tracks_station_id_created_at",
                table: "tracks",
                columns: new[] { "station_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracks_upload_expires_at",
                table: "tracks",
                column: "upload_expires_at",
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracks");

            migrationBuilder.DropColumn(
                name: "auto_dj",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "auto_dj_scheduling",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "storage_quota_bytes",
                table: "plans");
        }
    }
}
