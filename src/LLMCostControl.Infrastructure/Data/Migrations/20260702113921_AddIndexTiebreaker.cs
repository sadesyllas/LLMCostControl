using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLMCostControl.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexTiebreaker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_pricing_Provider_Model_effective_from",
                table: "model_pricing");

            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Provider_Model_effective_from_Id",
                table: "model_pricing",
                columns: new[] { "Provider", "Model", "effective_from", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_pricing_Provider_Model_effective_from_Id",
                table: "model_pricing");

            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Provider_Model_effective_from",
                table: "model_pricing",
                columns: new[] { "Provider", "Model", "effective_from" });
        }
    }
}
