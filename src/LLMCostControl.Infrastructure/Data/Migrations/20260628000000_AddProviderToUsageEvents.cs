using LLMCostControl.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLMCostControl.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds the <c>provider</c> column to <c>usage_events</c> so each audit row is
    /// self-describing now that pricing is keyed by (provider, model) (§8.5, §9.4).
    /// Existing rows (which predate per-provider pricing) backfill to an empty string.
    /// </summary>
    [DbContext(typeof(CostTrackerDbContext))]
    [Migration("20260628000000_AddProviderToUsageEvents")]
    public partial class AddProviderToUsageEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "usage_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "provider",
                table: "usage_events");
        }
    }
}
