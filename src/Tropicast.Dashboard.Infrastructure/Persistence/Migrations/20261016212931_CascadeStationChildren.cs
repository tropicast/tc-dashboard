using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CascadeStationChildren : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auto_dj_settings_stations_station_id",
                table: "auto_dj_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_broadcast_credentials_stations_station_id",
                table: "broadcast_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_directory_listings_stations_station_id",
                table: "directory_listings");

            migrationBuilder.DropForeignKey(
                name: "fk_encoding_profiles_stations_station_id",
                table: "encoding_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_live_sessions_stations_station_id",
                table: "live_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_playlists_stations_station_id",
                table: "playlists");

            migrationBuilder.DropForeignKey(
                name: "fk_station_stats_rollups_stations_station_id",
                table: "station_stats_rollups");

            migrationBuilder.DropForeignKey(
                name: "fk_stations_tenants_tenant_id",
                table: "stations");

            migrationBuilder.DropForeignKey(
                name: "fk_stream_assignments_stations_station_id",
                table: "stream_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_tracks_stations_station_id",
                table: "tracks");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_dj_settings_stations_station_id",
                table: "auto_dj_settings",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_broadcast_credentials_stations_station_id",
                table: "broadcast_credentials",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_directory_listings_stations_station_id",
                table: "directory_listings",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_encoding_profiles_stations_station_id",
                table: "encoding_profiles",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_live_sessions_stations_station_id",
                table: "live_sessions",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_playlists_stations_station_id",
                table: "playlists",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_station_stats_rollups_stations_station_id",
                table: "station_stats_rollups",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_stations_tenants_tenant_id",
                table: "stations",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_stream_assignments_stations_station_id",
                table: "stream_assignments",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tracks_stations_station_id",
                table: "tracks",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auto_dj_settings_stations_station_id",
                table: "auto_dj_settings");

            migrationBuilder.DropForeignKey(
                name: "fk_broadcast_credentials_stations_station_id",
                table: "broadcast_credentials");

            migrationBuilder.DropForeignKey(
                name: "fk_directory_listings_stations_station_id",
                table: "directory_listings");

            migrationBuilder.DropForeignKey(
                name: "fk_encoding_profiles_stations_station_id",
                table: "encoding_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_live_sessions_stations_station_id",
                table: "live_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_playlists_stations_station_id",
                table: "playlists");

            migrationBuilder.DropForeignKey(
                name: "fk_station_stats_rollups_stations_station_id",
                table: "station_stats_rollups");

            migrationBuilder.DropForeignKey(
                name: "fk_stations_tenants_tenant_id",
                table: "stations");

            migrationBuilder.DropForeignKey(
                name: "fk_stream_assignments_stations_station_id",
                table: "stream_assignments");

            migrationBuilder.DropForeignKey(
                name: "fk_tracks_stations_station_id",
                table: "tracks");

            migrationBuilder.AddForeignKey(
                name: "fk_auto_dj_settings_stations_station_id",
                table: "auto_dj_settings",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_broadcast_credentials_stations_station_id",
                table: "broadcast_credentials",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_directory_listings_stations_station_id",
                table: "directory_listings",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_encoding_profiles_stations_station_id",
                table: "encoding_profiles",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_sessions_stations_station_id",
                table: "live_sessions",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_playlists_stations_station_id",
                table: "playlists",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_station_stats_rollups_stations_station_id",
                table: "station_stats_rollups",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stations_tenants_tenant_id",
                table: "stations",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stream_assignments_stations_station_id",
                table: "stream_assignments",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tracks_stations_station_id",
                table: "tracks",
                column: "station_id",
                principalTable: "stations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
