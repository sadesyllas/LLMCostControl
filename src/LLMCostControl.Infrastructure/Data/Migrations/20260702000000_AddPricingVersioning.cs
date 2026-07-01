using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace LLMCostControl.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Migration that converts model_pricing into a versioned, append-only history table
    /// and updates usage_events to store a reference to model_pricing(id) instead of
    /// a flat copy of unit prices (§8.7, §9.4).
    /// </summary>
    [DbContext(typeof(CostTrackerDbContext))]
    [Migration("20260702000000_AddPricingVersioning")]
    public partial class AddPricingVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop StaleSince from model_pricing
            migrationBuilder.DropColumn(
                name: "StaleSince",
                table: "model_pricing");

            // 2. Add effective_from to model_pricing
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "effective_from",
                table: "model_pricing",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: DateTimeOffset.UnixEpoch);

            // Backfill effective_from to FetchedAt values for existing model_pricing
            migrationBuilder.Sql("UPDATE model_pricing SET effective_from = \"FetchedAt\";");

            // 3. Drop composite unique index on model_pricing
            migrationBuilder.DropIndex(
                name: "IX_model_pricing_Provider_Model",
                table: "model_pricing");

            // 4. Create versioned composite index on model_pricing
            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Provider_Model_effective_from",
                table: "model_pricing",
                columns: new[] { "Provider", "Model", "effective_from" });

            // 5. Add pricing_version_id to usage_events
            migrationBuilder.AddColumn<Guid>(
                name: "pricing_version_id",
                table: "usage_events",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            // Since we can't easily backfill a real foreign key to historical usage_events,
            // we will seed a placeholder model_pricing version for legacy data if any exists.
            var placeholderId = Guid.NewGuid();
            migrationBuilder.Sql($@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM usage_events) THEN
                        INSERT INTO model_pricing (
                            ""Id"", ""Provider"", ""Model"", price_input, price_output, price_cache_read, price_cache_write, ""Currency"", ""Unit"", ""FetchedAt"", effective_from
                        ) VALUES (
                            '{placeholderId}', 'OpenAI', 'placeholder-legacy-migration', 0.0, 0.0, 0.0, 0.0, 'USD', 'per-1M-tokens', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                        );
                        UPDATE usage_events SET pricing_version_id = '{placeholderId}';
                    END IF;
                END $$;
            ");

            // 6. Drop embedded unit price columns from usage_events
            migrationBuilder.DropColumn(name: "unit_price_input", table: "usage_events");
            migrationBuilder.DropColumn(name: "unit_price_output", table: "usage_events");
            migrationBuilder.DropColumn(name: "unit_price_cache_read", table: "usage_events");
            migrationBuilder.DropColumn(name: "unit_price_cache_write", table: "usage_events");

            // 7. Add foreign key from usage_events to model_pricing(Id)
            migrationBuilder.AddForeignKey(
                name: "FK_usage_events_model_pricing_pricing_version_id",
                table: "usage_events",
                column: "pricing_version_id",
                principalTable: "model_pricing",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop foreign key
            migrationBuilder.DropForeignKey(
                name: "FK_usage_events_model_pricing_pricing_version_id",
                table: "usage_events");

            // Re-add embedded unit price columns
            migrationBuilder.AddColumn<decimal>(name: "unit_price_input", table: "usage_events", type: "numeric(18,8)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "unit_price_output", table: "usage_events", type: "numeric(18,8)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "unit_price_cache_read", table: "usage_events", type: "numeric(18,8)", nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "unit_price_cache_write", table: "usage_events", type: "numeric(18,8)", nullable: true);

            // Drop version column
            migrationBuilder.DropColumn(
                name: "pricing_version_id",
                table: "usage_events");

            // Drop versioned index
            migrationBuilder.DropIndex(
                name: "IX_model_pricing_Provider_Model_effective_from",
                table: "model_pricing");

            // Re-create composite unique index
            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Provider_Model",
                table: "model_pricing",
                columns: new[] { "Provider", "Model" },
                unique: true);

            // Re-add stale_since column
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StaleSince",
                table: "model_pricing",
                type: "timestamp with time zone",
                nullable: true);

            // Drop effective_from column
            migrationBuilder.DropColumn(
                name: "effective_from",
                table: "model_pricing");
        }
    }
}
