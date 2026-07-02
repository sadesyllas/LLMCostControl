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

    /// <summary>Accrual details per period for usage events.</summary>
    public DbSet<UsageEventPeriodAccrual> UsageEventPeriodAccruals => Set<UsageEventPeriodAccrual>();

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
        ConfigureUsageEventPeriodAccruals(modelBuilder);

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
            e.Property(x => x.PeriodType)
                .HasConversion<string>()
                .HasColumnName("period_type")
                .HasMaxLength(20);
            e.Property(x => x.SetAt).IsRequired();
            e.HasIndex(x => new { x.GroupId, x.PeriodType }).IsUnique();
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
            e.Property(x => x.PeriodType)
                .HasConversion<string>()
                .HasColumnName("period_type")
                .HasMaxLength(20);
            e.Property(x => x.SetAt).IsRequired();
            e.HasIndex(x => new { x.CallerId, x.PeriodType }).IsUnique();
        });
    }

    private static void ConfigureModelPricing(ModelBuilder mb)
    {
        mb.Entity<ModelPricing>(e =>
        {
            e.ToTable("model_pricing");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider)
                .HasConversion<ProviderConverter>()
                .IsRequired()
                .HasMaxLength(20);
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
            e.Property(x => x.EffectiveFrom).IsRequired().HasColumnName("effective_from");
            e.Ignore(x => x.StaleSince);
            e.HasIndex(x => new { x.Provider, x.Model, x.EffectiveFrom, x.Id });
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
            e.Property(x => x.Model).IsRequired().HasMaxLength(100);
            e.Property(x => x.Provider)
                .HasConversion<ProviderConverter>()
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnName("provider");
            e.Property(x => x.TokensInput).HasColumnName("tokens_input");
            e.Property(x => x.TokensOutput).HasColumnName("tokens_output");
            e.Property(x => x.TokensCacheRead).HasColumnName("tokens_cache_read");
            e.Property(x => x.TokensCacheWrite).HasColumnName("tokens_cache_write");
            e.Property(x => x.PricingVersionId).HasColumnName("pricing_version_id").IsRequired();
            e.Property(x => x.CostAmount).HasColumnName("cost_amount").HasPrecision(18, 8);
            e.Property(x => x.CostCurrency).HasColumnName("cost_currency").IsRequired().HasMaxLength(3);
            e.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
            e.HasIndex(x => x.CapturedAt);

            e.HasOne<ModelPricing>()
                .WithMany()
                .HasForeignKey(x => x.PricingVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.PeriodAccruals)
                .WithOne()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureUsageEventPeriodAccruals(ModelBuilder mb)
    {
        mb.Entity<UsageEventPeriodAccrual>(e =>
        {
            e.ToTable("usage_event_period_accruals");
            e.HasKey(x => x.Id);
            
            e.Property(x => x.EventId)
                .HasColumnName("event_id")
                .IsRequired()
                .HasMaxLength(100);

            e.Property(x => x.PeriodType)
                .HasConversion<string>()
                .HasColumnName("period_type")
                .IsRequired()
                .HasMaxLength(20);

            e.Property(x => x.PeriodKey)
                .HasColumnName("period_key")
                .IsRequired()
                .HasMaxLength(10);

            e.Property(x => x.EffectiveGroupId)
                .HasColumnName("effective_group_id");

            e.Property(x => x.BudgetSource)
                .HasConversion<string>()
                .HasColumnName("budget_source")
                .IsRequired()
                .HasMaxLength(20);

            e.ComplexProperty(x => x.EffectiveBudgetAmount, m =>
            {
                m.Property(p => p.Amount).HasColumnName("effective_budget_amount").HasPrecision(18, 6);
                m.Property(p => p.Currency).HasColumnName("effective_budget_currency").IsRequired().HasMaxLength(3);
            });

            e.Property(x => x.RunningSpendAfter)
                .HasColumnName("running_spend_after")
                .HasPrecision(18, 6);

            e.HasIndex(x => new { x.EventId, x.PeriodType }).IsUnique();
        });
    }

    private sealed class ProviderConverter : ValueConverter<Provider, string>
    {
        public ProviderConverter()
            : base(
                v => ProviderResolver.ToCanonicalString(v),
                v => ParseProvider(v))
        {
        }

        private static Provider ParseProvider(string value)
        {
            if (ProviderResolver.TryParseProvider(value, out var p))
            {
                return p;
            }
            throw new FormatException($"Invalid provider value stored in database: '{value}'.");
        }
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
