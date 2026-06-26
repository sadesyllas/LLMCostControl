using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LLMCostControl.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModelPricingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_pricing_Model",
                table: "model_pricing");

            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Provider_Model",
                table: "model_pricing",
                columns: new[] { "Provider", "Model" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_pricing_Provider_Model",
                table: "model_pricing");

            migrationBuilder.CreateIndex(
                name: "IX_model_pricing_Model",
                table: "model_pricing",
                column: "Model",
                unique: true);
        }
    }
}
