// Fixtures de los canarios de la Spec 005 T-23: modelos mínimos de EF Core con una violación deliberada cada uno.

using GoalFlow.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Tests.Architecture.Fixtures.ModeloConFkSimple
{
    public class Padre
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }
    }

    public class Hijo
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }

        public Guid PadreId { get; set; }
    }

    /// <summary>Violación deliberada: una clave foránea entre dos entidades con TenantId que no incluye TenantId.</summary>
    public sealed class ContextoConFkSimple(DbContextOptions<ContextoConFkSimple> opciones) : DbContext(opciones)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Padre>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId).HasConversion(t => t.Value, v => new TenantId(v));
            });
            modelBuilder.Entity<Hijo>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId).HasConversion(t => t.Value, v => new TenantId(v));
                e.HasOne<Padre>().WithMany().HasForeignKey(x => x.PadreId);
            });
        }
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ModeloConIndiceSinTenant
{
    public class EntidadConCodigo
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }

        public string Codigo { get; set; } = "";
    }

    /// <summary>Violación deliberada: un índice único sin TenantId en una entidad con TenantId.</summary>
    public sealed class ContextoConIndiceSinTenant(DbContextOptions<ContextoConIndiceSinTenant> opciones) : DbContext(opciones)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<EntidadConCodigo>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId).HasConversion(t => t.Value, v => new TenantId(v));
                e.HasIndex(x => x.Codigo).IsUnique();
            });
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ModeloConClaveAlternaSinTenant
{
    public class EntidadConClave
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }

        public string Codigo { get; set; } = "";
    }

    /// <summary>Violación deliberada: una clave alterna sin TenantId en una entidad con TenantId.</summary>
    public sealed class ContextoConClaveAlterna(DbContextOptions<ContextoConClaveAlterna> opciones) : DbContext(opciones)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<EntidadConClave>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId).HasConversion(t => t.Value, v => new TenantId(v));
                e.HasAlternateKey(x => x.Codigo);
            });
    }
}
