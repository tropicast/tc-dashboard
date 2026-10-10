using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials");

            migrationBuilder.AddColumn<Guid>(
                name: "device_session_id",
                table: "broadcast_credentials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_authorizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    user_code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    denied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_authorizations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_device_session_id",
                table: "broadcast_credentials",
                column: "device_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials",
                columns: new[] { "station_id", "device_label" },
                unique: true,
                filter: "revoked_at IS NULL AND device_session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id_device_session_id",
                table: "broadcast_credentials",
                columns: new[] { "station_id", "device_session_id" },
                unique: true,
                filter: "revoked_at IS NULL AND device_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_device_authorizations_expires_at",
                table: "device_authorizations",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_authorizations_user_code",
                table: "device_authorizations",
                column: "user_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_authorizations_user_id",
                table: "device_authorizations",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_broadcast_credentials_device_sessions_device_session_id",
                table: "broadcast_credentials",
                column: "device_session_id",
                principalTable: "device_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_broadcast_credentials_device_sessions_device_session_id",
                table: "broadcast_credentials");

            migrationBuilder.DropTable(
                name: "device_authorizations");

            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_device_session_id",
                table: "broadcast_credentials");

            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials");

            migrationBuilder.DropIndex(
                name: "ix_broadcast_credentials_station_id_device_session_id",
                table: "broadcast_credentials");

            migrationBuilder.DropColumn(
                name: "device_session_id",
                table: "broadcast_credentials");

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id_device_label",
                table: "broadcast_credentials",
                columns: new[] { "station_id", "device_label" },
                unique: true,
                filter: "revoked_at IS NULL");
        }
    }
}
