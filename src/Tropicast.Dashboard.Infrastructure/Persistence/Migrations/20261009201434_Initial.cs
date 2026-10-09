using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    max_stations = table.Column<int>(type: "integer", nullable: false),
                    max_listeners = table.Column<int>(type: "integer", nullable: false),
                    max_bitrate_kbps = table.Column<int>(type: "integer", nullable: false),
                    formats = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    directory_listing = table.Column<bool>(type: "boolean", nullable: false),
                    embed = table.Column<bool>(type: "boolean", nullable: false),
                    analytics = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    plan_id = table.Column<string>(type: "character varying(32)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenants_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    genre = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    website = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    list_in_directory = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stations", x => x.id);
                    table.ForeignKey(
                        name: "fk_stations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<string>(type: "character varying(32)", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "broadcast_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    secret_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcast_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_broadcast_credentials_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_sessions_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "station_stats_rollups",
                columns: table => new
                {
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    peak_listeners = table.Column<int>(type: "integer", nullable: false),
                    listener_hours = table.Column<double>(type: "double precision", nullable: false),
                    egress_bytes = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_station_stats_rollups", x => new { x.station_id, x.interval, x.period_start });
                    table.ForeignKey(
                        name: "fk_station_stats_rollups_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stream_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    mount_base = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stream_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_stream_assignments_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "plans",
                columns: new[] { "id", "analytics", "directory_listing", "embed", "formats", "max_bitrate_kbps", "max_listeners", "max_stations", "name", "sort_order" },
                values: new object[,]
                {
                    { "free", true, false, false, "Mp3, Opus", 64, 100, 1, "Free", 0 },
                    { "growth", true, true, true, "Mp3, Opus", 192, 5000, 3, "Growth", 2 },
                    { "pro", true, true, true, "Mp3, Opus", 320, 25000, 5, "Pro", 3 },
                    { "starter", true, true, false, "Mp3, Opus", 128, 500, 1, "Starter", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_broadcast_credentials_station_id",
                table: "broadcast_credentials",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_sessions_station_id_format",
                table: "live_sessions",
                columns: new[] { "station_id", "format" },
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_live_sessions_station_id_started_at",
                table: "live_sessions",
                columns: new[] { "station_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id_user_id",
                table: "memberships",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stations_public_id",
                table: "stations",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stations_tenant_id_slug",
                table: "stations",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stream_assignments_station_id",
                table: "stream_assignments",
                column: "station_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_provider_subscription_id",
                table: "subscriptions",
                column: "provider_subscription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_tenant_id",
                table: "subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_plan_id",
                table: "tenants",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcast_credentials");

            migrationBuilder.DropTable(
                name: "live_sessions");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "station_stats_rollups");

            migrationBuilder.DropTable(
                name: "stream_assignments");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "plans");
        }
    }
}
