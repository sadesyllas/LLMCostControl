using LLMCostControl.Domain.Budgets;
using LLMCostControl.Domain.Common;
using LLMCostControl.Domain.Pricing;
using LLMCostControl.Domain.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LLMCostControl.Infrastructure.Data;

/// <summary>
/// EF Core DbContext for the LLM Cost Tracker. Maps all domain entities to
/// PostgreSQL tables and configures value-object columns.
/// </summary>
public class CostTrackerDbContext : DbContext
{
    /// <summary>Groups table.</summary>
    public DbSet<Group> Groups => Set<Group>();

    /// <summary>Group memberships table.</summary>
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    /// <summary>Group budgets table (per group, per period).</summary>
    public DbSet<GroupBudget> GroupBudgets => Set<GroupBudget>();

    /// <summary>Per-user budget overrides table.</summary>
    public DbSet<UserBudgetOverride> UserBudgetOverrides => Set<UserBudgetOverride>();

    /// <summary>Model pricing table (current pricing per model).</summary>
    public DbSet<ModelPricing> ModelPricing => Set<ModelPricing>();

    /// <summary>Append-only usage events (audit ledger).</summary>
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();

    /// <summary>
    /// Creates a <see cref="CostTrackerDbContext"/> with the given options.
    /// </summary>
    public CostTrackerDbContext(DbContextOptions<CostTrackerDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Configures entity-to-table mappings, value-object columns, indexes, and
    /// constraints.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureGroups(modelBuilder);
        ConfigureGroupMemberships(modelBuilder);
        ConfigureGroupBudgets(modelBuilder);
        ConfigureUserBudgetOverrides(modelBuilder);
        ConfigureModelPricing(modelBuilder);
        ConfigureUsageEvents(modelBuilder);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));
                foreach (var property in properties)
                {
                    modelBuilder.Entity(entityType.Name)
                        .Property(property.Name)
                        .HasConversion(new DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }

    private static void ConfigureGroups(ModelBuilder mb)
    {
        mb.Entity<Group>(e =>
        {
            e.ToTable("groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.CreatedAt).IsRequired();
        });
    }

    private static void ConfigureGroupMemberships(ModelBuilder mb)
    {
        mb.Entity<GroupMembership>(e =>
        {
            e.ToTable("group_memberships");
            e.HasKey(x => x.Id);
            e.Property(x => x.GroupId).IsRequired();
            e.Property(x => x.CallerId)
                .HasConversion<CallerIdConverter>()
                .HasColumnName("caller_id")
                .IsRequired()
                .HasMaxLength(320);
            e.Property(x => x.AddedAt).IsRequired();
            e.HasIndex(x => new { x.GroupId, x.CallerId }).IsUnique();
            e.HasIndex(x => x.CallerId);
        });
    }

    private static void ConfigureGroupBudgets(ModelBuilder mb)
    {
        mb.Entity<GroupBudget>(e =>
        {
            e.ToTable("group_budgets");
            e.HasKey(x => x.Id);
            e.Property(x => x.GroupId).IsRequired();
            e.ComplexProperty(x => x.Amount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("amount").HasPrecision(18, 6);
                m.Property(p => p.Currency).HasColumnName("currency").IsRequired().HasMaxLength(3);
            });
            e.Property(x => x.Period)
                .HasConversion<BudgetPeriodConverter>()
                .HasColumnName("period")
                .HasMaxLength(7);
            e.Property(x => x.SetAt).IsRequired();
            e.HasIndex(x => new { x.GroupId, x.Period }).IsUnique();
        });
    }

    private static void ConfigureUserBudgetOverrides(ModelBuilder mb)
    {
        mb.Entity<UserBudgetOverride>(e =>
        {
            e.ToTable("user_budget_overrides");
            e.HasKey(x => x.Id);
            e.Property(x => x.CallerId)
                .HasConversion<CallerIdConverter>()
                .HasColumnName("caller_id")
                .IsRequired()
                .HasMaxLength(320);
            e.ComplexProperty(x => x.Amount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("amount").HasPrecision(18, 6);
                m.Property(p => p.Currency).HasColumnName("currency").IsRequired().HasMaxLength(3);
            });
            e.Property(x => x.Period)
                .HasConversion<BudgetPeriodConverter>()
                .HasColumnName("period")
                .HasMaxLength(7);
            e.Property(x => x.SetAt).IsRequired();
            e.HasIndex(x => new { x.CallerId, x.Period }).IsUnique();
        });
    }

    private static void ConfigureModelPricing(ModelBuilder mb)
    {
        mb.Entity<ModelPricing>(e =>
        {
            e.ToTable("model_pricing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(x => x.Model).IsRequired().HasMaxLength(100);
            e.ComplexProperty(x => x.Prices, "prices", p =>
            {
                p.Property(pp => pp.Input).HasColumnName("price_input").HasPrecision(18, 8);
                p.Property(pp => pp.Output).HasColumnName("price_output").HasPrecision(18, 8);
                p.Property(pp => pp.CacheRead).HasColumnName("price_cache_read").HasPrecision(18, 8);
                p.Property(pp => pp.CacheWrite).HasColumnName("price_cache_write").HasPrecision(18, 8);
            });
            e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
            e.Property(x => x.Unit).IsRequired().HasMaxLength(30);
            e.Property(x => x.FetchedAt).IsRequired();
            e.Property(x => x.StaleSince);
            e.HasIndex(x => x.Model).IsUnique();
        });
    }

    private static void ConfigureUsageEvents(ModelBuilder mb)
    {
        mb.Entity<UsageEvent>(e =>
        {
            e.ToTable("usage_events");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasMaxLength(100);
            e.Property(x => x.CallerId)
                .HasConversion<CallerIdConverter>()
                .HasColumnName("caller_id")
                .IsRequired()
                .HasMaxLength(320);
            e.Property(x => x.EffectiveGroupId).HasColumnName("effective_group_id");
            e.Property(x => x.BudgetSource).HasConversion<string>().IsRequired().HasMaxLength(20);
            e.Property(x => x.Model).IsRequired().HasMaxLength(100);
            e.Property(x => x.TokensInput).HasColumnName("tokens_input");
            e.Property(x => x.TokensOutput).HasColumnName("tokens_output");
            e.Property(x => x.TokensCacheRead).HasColumnName("tokens_cache_read");
            e.Property(x => x.TokensCacheWrite).HasColumnName("tokens_cache_write");
            e.ComplexProperty(x => x.UnitPrices, "unit_prices", p =>
            {
                p.Property(pp => pp.Input).HasColumnName("unit_price_input").HasPrecision(18, 8);
                p.Property(pp => pp.Output).HasColumnName("unit_price_output").HasPrecision(18, 8);
                p.Property(pp => pp.CacheRead).HasColumnName("unit_price_cache_read").HasPrecision(18, 8);
                p.Property(pp => pp.CacheWrite).HasColumnName("unit_price_cache_write").HasPrecision(18, 8);
            });
            e.Property(x => x.CostAmount).HasColumnName("cost_amount").HasPrecision(18, 8);
            e.Property(x => x.CostCurrency).HasColumnName("cost_currency").IsRequired().HasMaxLength(3);
            e.Property(x => x.RunningSpendAfter).HasColumnName("running_spend_after").HasPrecision(18, 8);
            e.Property(x => x.Period)
                .HasConversion<BudgetPeriodConverter>()
                .HasColumnName("period")
                .HasMaxLength(7);
            e.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
            e.HasIndex(x => new { x.CallerId, x.Period });
            e.HasIndex(x => x.CapturedAt);
        });
    }

    private sealed class CallerIdConverter : ValueConverter<CallerId, string>
    {
        public CallerIdConverter()
            : base(v => v.Value, v => CallerId.From(v))
        {
        }
    }

    private sealed class BudgetPeriodConverter : ValueConverter<BudgetPeriod, string>
    {
        public BudgetPeriodConverter()
            : base(v => v.ToString(), v => ParsePeriod(v))
        {
        }

        private static BudgetPeriod ParsePeriod(string value)
        {
            var parts = value.Split('-');
            return new BudgetPeriod(int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }
}
