using Bunit;
using LLMCostControl.Admin.App.Components.Pages;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Infrastructure.Pricing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMCostControl.Admin.App.Tests;

/// <summary>
/// bUnit tests for the pricing-file upload page (M18, §12.3): a valid file is
/// imported via the shared <see cref="PricingImportService"/> path; an invalid
/// file shows the validator's errors and writes nothing.
/// </summary>
public sealed class PricingUploadPageTests : BunitContext
{
    private const string ValidFile = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            { "provider": "openai", "models": [
              { "model": "gpt-4o", "fetchedAt": "2026-06-24T12:00:00Z",
                "prices": { "input": 2.50, "output": 10.00, "cacheRead": 1.25, "cacheWrite": null } } ] }
          ]
        }
        """;

    // Missing the mandatory 'output' price.
    private const string InvalidFile = """
        {
          "generatedAt": "2026-06-24T12:00:00Z",
          "currency": "USD",
          "unit": "per-1M-tokens",
          "providers": [
            { "provider": "openai", "models": [
              { "model": "bad", "fetchedAt": "2026-06-24T12:00:00Z",
                "prices": { "input": 2.50, "cacheRead": 1.25 } } ] }
          ]
        }
        """;

    private RecordingPricingWriter SetupImporter()
    {
        var writer = new RecordingPricingWriter();
        var importer = new PricingImportService(writer, new NoOpPricingUpdatePublisher(),
            NullLogger<PricingImportService>.Instance);
        Services.AddSingleton(importer);
        return writer;
    }

    [Fact]
    public void Valid_file_imports_and_reports_success()
    {
        var writer = SetupImporter();
        var cut = Render<PricingManagement>();

        cut.Find("[data-testid=pricing-content]").Change(ValidFile);
        cut.Find("[data-testid=import-pricing]").Click();

        cut.Find("[data-testid=import-success]").TextContent.Should().Contain("1");
        writer.Writes.Should().ContainSingle().Which.Should().Be(Domain.Pricing.Provider.OpenAI);
    }

    [Fact]
    public void Invalid_file_shows_errors_and_writes_nothing()
    {
        var writer = SetupImporter();
        var cut = Render<PricingManagement>();

        cut.Find("[data-testid=pricing-content]").Change(InvalidFile);
        cut.Find("[data-testid=import-pricing]").Click();

        cut.FindAll("[data-testid=import-error]").Should().NotBeEmpty();
        cut.FindAll("[data-testid=import-success]").Should().BeEmpty();
        writer.Writes.Should().BeEmpty();
    }
}
