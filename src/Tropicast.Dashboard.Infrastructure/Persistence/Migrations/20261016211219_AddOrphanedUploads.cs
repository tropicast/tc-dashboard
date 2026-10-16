using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrphanedUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "orphaned_uploads",
                columns: table => new
                {
                    object_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    delete_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orphaned_uploads", x => x.object_key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orphaned_uploads_delete_after",
                table: "orphaned_uploads",
                column: "delete_after");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orphaned_uploads");
        }
    }
}
