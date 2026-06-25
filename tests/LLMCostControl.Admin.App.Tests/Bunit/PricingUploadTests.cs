using Bunit;
using LLMCostControl.Admin.App.Components.Pages.Admin;
using LLMCostControl.Admin.App.Services;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LLMCostControl.Admin.App.Tests.Bunit;

/// <summary>
/// bUnit tests for M18 pricing admin page: view renders with staleness badge,
/// upload valid / invalid pricing files (§12.3).
/// </summary>
public sealed class PricingUploadTests : BunitContext
{
    private readonly IAdminReadService _readService;
    private readonly IPricingImportService _importService;

    public PricingUploadTests()
    {
        _readService = Substitute.For<IAdminReadService>();
        _importService = Substitute.For<IPricingImportService>();

        _readService.GetAllPricingAsync(Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<ModelPricing>>([]));

        Services.AddSingleton(_readService);
        Services.AddSingleton(_importService);
        Services.AddAuthorizationCore();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Pricing_page_renders_heading()
    {
        var cut = Render<PricingAdmin>();
        cut.Find("#pricing-heading").TextContent.Should().Contain("Pricing");
    }

    [Fact]
    public void Pricing_table_shows_stale_badge_for_stale_pricing()
    {
        var stale = ModelPricing.Create(Provider.OpenAI, "gpt-4o",
            TokenPrices.Create(2.5m, 10m, 1.25m));
        stale.StaleSince = DateTimeOffset.UtcNow.AddHours(-5); // mark stale

        _readService.GetAllPricingAsync(Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<IReadOnlyList<ModelPricing>>([stale]));

        var cut = Render<PricingAdmin>();
        cut.FindAll(".badge-stale").Count.Should().BeGreaterThan(0,
            "stale pricing should show a stale badge");
    }

    [Fact]
    public void Import_empty_content_shows_validation_error()
    {
        var cut = Render<PricingAdmin>();
        // Leave the textarea empty.
        cut.Find("#import-pricing-btn").Click();

        cut.Find("#upload-errors").TextContent.Should().Contain("required");
        _importService.DidNotReceive().ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_valid_file_shows_success_message()
    {
        _importService.ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .Returns(Task.FromResult(PricingImportResult.Success(3)));

        var cut = Render<PricingAdmin>();
        cut.Find("#pricing-json-input").Change("{}"); // non-empty triggers service call
        cut.Find("#import-pricing-btn").Click();

        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.Find("#upload-success").TextContent.Should().Contain("3");
    }

    [Fact]
    public async Task Import_invalid_file_shows_errors()
    {
        _importService.ImportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .Returns(Task.FromResult(PricingImportResult.Failure(
                          ["Invalid JSON.", "Missing required field."])));

        var cut = Render<PricingAdmin>();
        cut.Find("#pricing-json-input").Change("bad json");
        cut.Find("#import-pricing-btn").Click();

        await cut.InvokeAsync(() => Task.CompletedTask);
        var errors = cut.Find("#upload-errors").TextContent;
        errors.Should().Contain("Invalid JSON");
        errors.Should().Contain("Missing required field");
    }
}
