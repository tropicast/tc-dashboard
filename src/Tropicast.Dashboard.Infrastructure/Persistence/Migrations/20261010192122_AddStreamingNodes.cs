using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamingNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "streaming_nodes",
                columns: table => new
                {
                    node = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    desired_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    applied_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streaming_nodes", x => x.node);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "streaming_nodes");
        }
    }
}
