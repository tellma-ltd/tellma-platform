using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CollProbe;

/// <summary>
///     A one-table model shaped like the identity engine's: a default schema, no history-table
///     override. PROBE_STAGE picks the model: 0 no collation, 1 UseCollation, 2 UseCollation plus a
///     new column.
/// </summary>
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    /// <summary>The model stage read from PROBE_STAGE (default 1).</summary>
    public static int Stage =>
        int.TryParse(Environment.GetEnvironmentVariable("PROBE_STAGE"), out int s) ? s : 1;

    /// <summary>The probe table.</summary>
    public DbSet<Item> Items => Set<Item>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("idsvr");
        if (Stage >= 1)
        {
            b.UseCollation("Latin1_General_100_CI_AS");
        }

        if (Stage >= 2)
        {
            b.Entity<Item>().Property<string>("Note").HasMaxLength(50);
        }
    }
}

/// <summary>A row with one text column.</summary>
public sealed class Item
{
    /// <summary>The key.</summary>
    public int Id { get; set; }

    /// <summary>A text column with no explicit collation.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Design-time factory for dotnet ef; PROBE_CS overrides the connection.</summary>
public sealed class ProbeDesignTimeFactory : IDesignTimeDbContextFactory<ProbeContext>
{
    /// <inheritdoc />
    public ProbeContext CreateDbContext(string[] args)
    {
        string cs = Environment.GetEnvironmentVariable("PROBE_CS")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=CollProbeDesign;Integrated Security=true;TrustServerCertificate=true";
        return new ProbeContext(new DbContextOptionsBuilder<ProbeContext>().UseSqlServer(cs).Options);
    }
}
