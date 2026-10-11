using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tropicast.Dashboard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_tenant_id",
                table: "subscriptions");

            migrationBuilder.AddColumn<bool>(
                name: "cancel_at_period_end",
                table: "subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_event_at",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "past_due_since",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_end",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "trial_used",
                table: "subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "processed_webhook_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_webhook_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_provider_customer_id",
                table: "subscriptions",
                column: "provider_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_tenant_id",
                table: "subscriptions",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_webhook_events_processed_at",
                table: "processed_webhook_events",
                column: "processed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_webhook_events");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_provider_customer_id",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_tenant_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "cancel_at_period_end",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "last_event_at",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "past_due_since",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_end",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_used",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_tenant_id",
                table: "subscriptions",
                column: "tenant_id");
        }
    }
}
