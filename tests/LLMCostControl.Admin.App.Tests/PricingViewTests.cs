using System;
using System.Linq;
using System.Threading.Tasks;
using Bunit;
using Bunit.TestDoubles;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Data;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the Model Pricing page (§12.3, M18).
/// Exercises views, staleness badges, and valid/invalid JSON pricing uploads.
/// </summary>
public sealed class PricingViewTests : TestContext, IDisposable
{
    private readonly SqliteTestDbContextFactory _dbFactory;

    private static readonly string ValidJson = """
    {
      "generatedAt": "2026-06-24T12:00:00Z",
      "currency": "USD",
      "unit": "per-1M-tokens",
      "providers": [
        {
          "provider": "openai",
          "models": [
            {
              "model": "gpt-4o",
              "fetchedAt": "2026-06-24T12:00:00Z",
              "prices": {
                "input": 2.50,
                "output": 10.00,
                "cacheRead": 1.25,
                "cacheWrite": null
              }
            }
          ]
        }
      ]
    }
    """;

    /// <summary>
    /// Initializes a new instance of the <see cref="PricingViewTests"/> class.
    /// Setup JSInterop mocks for InputFile integration.
    /// </summary>
    public PricingViewTests()
    {
        _dbFactory = new SqliteTestDbContextFactory();
        Services.AddSingleton<IDbContextFactory<CostTrackerDbContext>>(_dbFactory);

        // Mock InputFile JS initialization
        JSInterop.SetupVoid("Blazor._internal.InputFile.init", _ => true).SetVoidResult();
    }

    /// <summary>Disposes of the SQLite in-memory database connection.</summary>
    public new void Dispose()
    {
        _dbFactory.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Verifies that when no pricing data is seeded, a prompt to upload pricing is displayed.
    /// </summary>
    [Fact]
    public void PricingView_EmptyState_ShowsNoPricingDefined()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        // Act
        var cut = RenderComponent<Pricing>();

        // Assert
        cut.Markup.Should().Contain("No pricing models defined in the database. Upload a pricing file to initialize model rates.");
    }

    /// <summary>
    /// Verifies that model pricing lists and staleness badges are correctly rendered.
    /// </summary>
    [Fact]
    public async Task PricingView_WithSeededData_RendersModelRatesAndBadges()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        using (var db = _dbFactory.CreateDbContext())
        {
            var repo = new ModelPricingRepository(db);

            var pricingFresh = ModelPricing.Create(
                Provider.OpenAI,
                "gpt-4o-fresh",
                TokenPrices.Create(2.5m, 10m, 1.25m),
                fetchedAt: DateTimeOffset.UtcNow
            );

            var pricingStale = ModelPricing.Create(
                Provider.Google,
                "gemini-1.5-pro",
                TokenPrices.Create(1.25m, 5m),
                fetchedAt: DateTimeOffset.UtcNow.AddDays(-2)
            );
            pricingStale.StaleSince = DateTimeOffset.UtcNow.AddDays(-1);

            await repo.ReplaceProviderPricingAsync(Provider.OpenAI, new[] { pricingFresh });
            await repo.ReplaceProviderPricingAsync(Provider.Google, new[] { pricingStale });
        }

        // Act
        var cut = RenderComponent<Pricing>();

        // Assert
        cut.Find("#pricing-table").Should().NotBeNull();
        cut.Find("#pricing-row-gpt-4o-fresh").Should().NotBeNull();
        cut.Find("#pricing-row-gemini-1-5-pro").Should().NotBeNull();

        // Freshness verification
        var freshRow = cut.Find("#pricing-row-gpt-4o-fresh");
        freshRow.TextContent.Should().Contain("gpt-4o-fresh");
        freshRow.TextContent.Should().Contain("Fresh");

        // Staleness verification
        var staleRow = cut.Find("#pricing-row-gemini-1-5-pro");
        staleRow.TextContent.Should().Contain("gemini-1.5-pro");
        staleRow.TextContent.Should().Contain("Stale");
    }

    /// <summary>
    /// Verifies that an administrator can upload a valid JSON pricing definitions file,
    /// which updates the database pricing data.
    /// </summary>
    [Fact]
    public async Task AdminUser_UploadsValidPricingFile_UpdatesDatabase()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        var cut = RenderComponent<Pricing>();

        // Act
        var inputFile = cut.FindComponent<InputFile>();
        var fileToUpload = InputFileContent.CreateFromText(ValidJson, "pricing.json");
        await cut.InvokeAsync(() => inputFile.UploadFiles(fileToUpload));

        // Assert success message is displayed
        var successAlert = cut.Find("#pricing-success-alert");
        successAlert.TextContent.Should().Contain("Pricing file uploaded successfully! Imported 1 model pricing definitions.");

        // Assert database is populated
        using (var db = _dbFactory.CreateDbContext())
        {
            var pricing = await db.ModelPricing.ToListAsync();
            pricing.Should().ContainSingle(p => p.Model == "gpt-4o" && p.Prices.Input == 2.50m);
        }
    }

    /// <summary>
    /// Verifies that uploading an invalid pricing JSON file is rejected with detailed error messages.
    /// </summary>
    [Fact]
    public async Task AdminUser_UploadsInvalidPricingFile_ShowsErrorMessage()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("admin@example.com");
        authContext.SetRoles("CostTracker.Admin");

        var cut = RenderComponent<Pricing>();

        // Act
        var inputFile = cut.FindComponent<InputFile>();
        var fileToUpload = InputFileContent.CreateFromText("{}", "invalid.json");
        await cut.InvokeAsync(() => inputFile.UploadFiles(fileToUpload));

        // Assert error message is displayed
        var errorAlert = cut.Find("#pricing-error-alert");
        errorAlert.TextContent.Should().Contain("Pricing file validation failed");

        // Assert database is still empty
        using (var db = _dbFactory.CreateDbContext())
        {
            var pricing = await db.ModelPricing.AnyAsync();
            pricing.Should().BeFalse();
        }
    }

    /// <summary>
    /// Verifies that read-only users cannot access the pricing file upload interface.
    /// </summary>
    [Fact]
    public void ReadOnlyUser_CannotSeeUploadInterface()
    {
        // Arrange
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized("reader@example.com");
        authContext.SetRoles("CostTracker.ReadOnly");

        // Act
        var cut = RenderComponent<Pricing>();

        // Assert upload inputs are hidden
        cut.FindAll("#pricing-file-input").Should().BeEmpty();
    }
}
