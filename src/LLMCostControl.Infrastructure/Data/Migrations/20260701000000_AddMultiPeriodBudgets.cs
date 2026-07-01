using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLMCostControl.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Migration adding multi-period budgeting support:
    /// - Adds usage_event_period_accruals child table
    /// - Drops legacy columns from usage_events
    /// - Changes period to period_type in group_budgets and user_budget_overrides
    /// </summary>
    [DbContext(typeof(CostTrackerDbContext))]
    [Migration("20260701000000_AddMultiPeriodBudgets")]
    public partial class AddMultiPeriodBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create table usage_event_period_accruals
            migrationBuilder.CreateTable(
                name: "usage_event_period_accruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    effective_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    budget_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    running_spend_after = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    effective_budget_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    effective_budget_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_event_period_accruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_usage_event_period_accruals_usage_events_event_id",
                        column: x => x.event_id,
                        principalTable: "usage_events",
                        principalColumn: "EventId",
                        onDelete: ReferentialAction.Cascade);
                });

            // 2. Drop legacy columns from usage_events
            migrationBuilder.DropColumn(name: "effective_group_id", table: "usage_events");
            migrationBuilder.DropColumn(name: "BudgetSource", table: "usage_events");
            migrationBuilder.DropColumn(name: "running_spend_after", table: "usage_events");
            migrationBuilder.DropColumn(name: "period", table: "usage_events");

            // 3. Update group_budgets column name and index
            migrationBuilder.DropIndex(name: "IX_group_budgets_GroupId_period", table: "group_budgets");
            migrationBuilder.RenameColumn(name: "period", table: "group_budgets", newName: "period_type");
            migrationBuilder.CreateIndex(
                name: "IX_group_budgets_GroupId_period_type",
                table: "group_budgets",
                columns: new[] { "GroupId", "period_type" },
                unique: true);

            // 4. Update user_budget_overrides column name and index
            migrationBuilder.DropIndex(name: "IX_user_budget_overrides_caller_id_period", table: "user_budget_overrides");
            migrationBuilder.RenameColumn(name: "period", table: "user_budget_overrides", newName: "period_type");
            migrationBuilder.CreateIndex(
                name: "IX_user_budget_overrides_caller_id_period_type",
                table: "user_budget_overrides",
                columns: new[] { "caller_id", "period_type" },
                unique: true);

            // 5. Add unique index to usage_event_period_accruals
            migrationBuilder.CreateIndex(
                name: "IX_usage_event_period_accruals_event_id_period_type",
                table: "usage_event_period_accruals",
                columns: new[] { "event_id", "period_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Drop table usage_event_period_accruals
            migrationBuilder.DropTable(name: "usage_event_period_accruals");

            // 2. Re-add legacy columns to usage_events
            migrationBuilder.AddColumn<Guid>(name: "effective_group_id", table: "usage_events", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "BudgetSource", table: "usage_events", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<decimal>(name: "running_spend_after", table: "usage_events", type: "numeric(18,8)", precision: 18, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>(name: "period", table: "usage_events", type: "character varying(7)", maxLength: 7, nullable: false, defaultValue: "");

            // 3. Revert group_budgets column name and index
            migrationBuilder.DropIndex(name: "IX_group_budgets_GroupId_period_type", table: "group_budgets");
            migrationBuilder.RenameColumn(name: "period_type", table: "group_budgets", newName: "period");
            migrationBuilder.CreateIndex(
                name: "IX_group_budgets_GroupId_period",
                table: "group_budgets",
                columns: new[] { "GroupId", "period" },
                unique: true);

            // 4. Revert user_budget_overrides column name and index
            migrationBuilder.DropIndex(name: "IX_user_budget_overrides_caller_id_period_type", table: "user_budget_overrides");
            migrationBuilder.RenameColumn(name: "period_type", table: "user_budget_overrides", newName: "period");
            migrationBuilder.CreateIndex(
                name: "IX_user_budget_overrides_caller_id_period",
                table: "user_budget_overrides",
                columns: new[] { "caller_id", "period" },
                unique: true);
        }
    }
}
