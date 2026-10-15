using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoDjCredentialAndEncoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials");

            migrationBuilder.AddColumn<string>(
                name: "protected_secret",
                table: "broadcast_credentials",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "broadcast_credentials",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Broadcast");

            migrationBuilder.CreateTable(
                name: "encoding_profiles",
                columns: table => new
                {
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bitrate_kbps = table.Column<int>(type: "integer", nullable: false),
                    sample_rate = table.Column<int>(type: "integer", nullable: false),
                    channels = table.Column<int>(type: "integer", nullable: false),
                    pinned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_encoding_profiles", x => new { x.station_id, x.format });
                    table.ForeignKey(
                        name: "fk_encoding_profiles_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id_autodj",
                table: "broadcast_credentials",
                column: "station_id",
                unique: true,
                filter: "revoked_at IS NULL AND purpose = 'AutoDj'");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials",
                columns: new[] { "station_id", "device_label" },
                unique: true,
                filter: "revoked_at IS NULL AND device_session_id IS NULL AND purpose = 'Broadcast'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "encoding_profiles");

            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_station_id_autodj",
                table: "broadcast_credentials");

            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials");

            migrationBuilder.DropColumn(
                name: "protected_secret",
                table: "broadcast_credentials");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "broadcast_credentials");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials",
                columns: new[] { "station_id", "device_label" },
                unique: true,
                filter: "revoked_at IS NULL AND device_session_id IS NULL");
        }
    }
}
