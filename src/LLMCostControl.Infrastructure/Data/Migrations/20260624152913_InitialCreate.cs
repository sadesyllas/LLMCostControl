using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLMCostControl.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "group_budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "group_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_id = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StaleSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    price_cache_read = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    price_cache_write = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    price_input = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    price_output = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_pricing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usage_events",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    caller_id = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    effective_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    BudgetSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tokens_input = table.Column<long>(type: "bigint", nullable: false),
                    tokens_output = table.Column<long>(type: "bigint", nullable: false),
                    tokens_cache_read = table.Column<long>(type: "bigint", nullable: false),
                    tokens_cache_write = table.Column<long>(type: "bigint", nullable: false),
                    cost_amount = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    cost_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    running_spend_after = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unit_price_cache_read = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    unit_price_cache_write = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    unit_price_input = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    unit_price_output = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "user_budget_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_id = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_budget_overrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_group_budgets_GroupId_period",
                table: "group_budgets",
                columns: new[] { "GroupId", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_memberships_GroupId_caller_id",
                table: "group_memberships",
                columns: new[] { "GroupId", "caller_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_memberships_caller_id",
                table: "group_memberships",
                column: "caller_id");

            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Model",
                table: "model_pricing",
                column: "Model",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usage_events_caller_id_period",
                table: "usage_events",
                columns: new[] { "caller_id", "period" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_events_captured_at",
                table: "usage_events",
                column: "captured_at");

            migrationBuilder.CreateIndex(
                name: "IX_user_budget_overrides_caller_id_period",
                table: "user_budget_overrides",
                columns: new[] { "caller_id", "period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_budgets");

            migrationBuilder.DropTable(
                name: "group_memberships");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "model_pricing");

            migrationBuilder.DropTable(
                name: "usage_events");

            migrationBuilder.DropTable(
                name: "user_budget_overrides");
        }
    }
}
