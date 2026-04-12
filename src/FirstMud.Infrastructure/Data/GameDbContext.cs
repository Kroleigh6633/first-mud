using FirstMud.Domain.Entities;
using FirstMud.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Data;

public class GameDbContext : DbContext
{
    public GameDbContext(DbContextOptions<GameDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<Companion> Companions => Set<Companion>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<PlayerFactionReputation> PlayerFactionReputations => Set<PlayerFactionReputation>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<BaseAsset> BaseAssets => Set<BaseAsset>();
    public DbSet<Homestead> Homesteads => Set<Homestead>();
    public DbSet<HomesteadStorageItem> HomesteadStorageItems => Set<HomesteadStorageItem>();
    public DbSet<ResourceNode> ResourceNodes => Set<ResourceNode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PlayerConfiguration());
        modelBuilder.ApplyConfiguration(new CompanionConfiguration());
        modelBuilder.ApplyConfiguration(new ItemConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerFactionReputationConfiguration());
        modelBuilder.ApplyConfiguration(new ZoneConfiguration());
        modelBuilder.ApplyConfiguration(new RecipeConfiguration());
        modelBuilder.ApplyConfiguration(new BaseAssetConfiguration());
        modelBuilder.ApplyConfiguration(new HomesteadConfiguration());
        modelBuilder.ApplyConfiguration(new HomesteadStorageItemConfiguration());
        modelBuilder.ApplyConfiguration(new ResourceNodeConfiguration());
    }
}
