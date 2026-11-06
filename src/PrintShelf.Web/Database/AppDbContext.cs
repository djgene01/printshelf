using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<StlModel> Models => Set<StlModel>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ModelTag> ModelTags => Set<ModelTag>();
    public DbSet<PrintRun> PrintRuns => Set<PrintRun>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<StlModel>(e =>
        {
            e.ToTable("models");
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.FileSha256).IsRequired().HasMaxLength(64).IsFixedLength();
            e.Property(x => x.Format).IsRequired().HasMaxLength(16);
            e.Property(x => x.PrintUnit).IsRequired().HasMaxLength(8);
            e.Property(x => x.BboxX).HasColumnType("numeric(10,3)");
            e.Property(x => x.BboxY).HasColumnType("numeric(10,3)");
            e.Property(x => x.BboxZ).HasColumnType("numeric(10,3)");
            e.Property(x => x.VolumeMm3).HasColumnType("numeric(14,3)");
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.FileSha256);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        mb.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.Property(x => x.Slug).IsRequired().HasMaxLength(64);
            e.Property(x => x.Label).IsRequired().HasMaxLength(64);
            // Slug is normalized to lowercase in the service layer; unique index is plain.
            e.HasIndex(x => x.Slug).IsUnique();
        });

        mb.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.Property(x => x.Color).IsRequired().HasMaxLength(7);
            e.HasIndex(x => x.Name).IsUnique();
        });

        mb.Entity<ModelTag>(e =>
        {
            e.ToTable("model_tags");
            e.HasKey(x => new { x.ModelId, x.TagId });
            e.HasOne(x => x.Model).WithMany(m => m.ModelTags).HasForeignKey(x => x.ModelId);
            e.HasOne(x => x.Tag).WithMany(t => t.ModelTags).HasForeignKey(x => x.TagId);
            // Matching filter so soft-deleted models drop their links too
            // (silences EF model-validation warning 10622).
            e.HasQueryFilter(x => x.Model.DeletedAt == null);
        });

        mb.Entity<PrintRun>(e =>
        {
            e.ToTable("print_runs", t =>
                t.HasCheckConstraint("CK_print_runs_result", "\"Result\" IN ('success','failed','partial')"));
            e.Property(x => x.Printer).IsRequired().HasMaxLength(120);
            e.Property(x => x.Material).HasMaxLength(80);
            e.Property(x => x.Color).HasMaxLength(40);
            e.Property(x => x.NozzleMm).HasColumnType("numeric(4,2)");
            e.Property(x => x.LayerMm).HasColumnType("numeric(4,3)");
            e.Property(x => x.Cost).HasColumnType("numeric(10,2)");
            e.Property(x => x.Result).IsRequired().HasMaxLength(16);
            e.HasIndex(x => new { x.ModelId, x.PrintedAt });
            e.HasOne(x => x.Model).WithMany(m => m.PrintRuns).HasForeignKey(x => x.ModelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.Model.DeletedAt == null);
        });
    }
}
