using System;
using System.Collections.Generic;
using System.Net.Http;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Infrastructure.Pricing;
using LLMCostControl.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Service collection extensions for configuring the pricing refresh system.
/// </summary>
public static class PricingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pricing refresh job, options, configured HTTP clients, and all five pricing adapters.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPricingRefresh(this IServiceCollection services, IConfiguration configuration)
    {
        var refreshSection = configuration.GetSection("Pricing:Refresh");
        services.Configure<PricingRefreshOptions>(refreshSection);
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PricingRefreshOptions>>().Value);

        services.AddScoped<ModelPricingRepository>();
        services.AddHttpClient();

        services.AddTransient<IPricingAdapter>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai");
            var url = refreshSection.GetValue<string>("Sources:openai") ?? "http://localhost:5000/pricing/openai.json";
            var repo = sp.GetRequiredService<ModelPricingRepository>();
            return new OpenAIPricingAdapter(client, url, repo);
        });

        services.AddTransient<IPricingAdapter>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic");
            var url = refreshSection.GetValue<string>("Sources:anthropic") ?? "http://localhost:5000/pricing/anthropic.json";
            var repo = sp.GetRequiredService<ModelPricingRepository>();
            return new AnthropicPricingAdapter(client, url, repo);
        });

        services.AddTransient<IPricingAdapter>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("google");
            var url = refreshSection.GetValue<string>("Sources:google") ?? "http://localhost:5000/pricing/google.json";
            var repo = sp.GetRequiredService<ModelPricingRepository>();
            return new GooglePricingAdapter(client, url, repo);
        });

        services.AddTransient<IPricingAdapter>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("azure-foundry");
            var url = refreshSection.GetValue<string>("Sources:azure-foundry") ?? "http://localhost:5000/pricing/azure-foundry.json";
            var repo = sp.GetRequiredService<ModelPricingRepository>();
            return new AzureFoundryPricingAdapter(client, url, repo);
        });

        services.AddTransient<IPricingAdapter>(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("vertex-ai");
            var url = refreshSection.GetValue<string>("Sources:vertex-ai") ?? "http://localhost:5000/pricing/vertex-ai.json";
            var repo = sp.GetRequiredService<ModelPricingRepository>();
            return new VertexAIPricingAdapter(client, url, repo);
        });

        services.AddHostedService<PricingRefreshJob>();

        return services;
    }
}
