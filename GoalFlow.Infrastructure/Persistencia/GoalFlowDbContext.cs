using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoalFlow.Infrastructure.Persistencia;

public class GoalFlowDbContext(DbContextOptions<GoalFlowDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    public const string Esquema = "goalflow";
    public const string FiltroTenant = "Tenant";

    public DbSet<Liga> Ligas => Set<Liga>();
    public DbSet<AdminLiga> AdminsLiga => Set<AdminLiga>();
    public DbSet<Equipo> Equipos => Set<Equipo>();

    // El filtro referencia este miembro para evaluarse por consulta; EF Core cachea el modelo (ADR-14 §2.2).
    private TenantId TenantActual => tenantContext.Current;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<Liga>(liga =>
        {
            liga.ToTable("ligas");
            liga.HasKey(l => l.Id);
            ColumnaDeTenant(liga.Property(l => l.Id), "id");
            liga.Property(l => l.Slug).HasColumnName("slug");
            liga.Property(l => l.Nombre).HasColumnName("nombre");
            liga.HasIndex(l => l.Slug).IsUnique().HasDatabaseName("ux_ligas_slug");
        });

        modelBuilder.Entity<AdminLiga>(admin =>
        {
            admin.ToTable("admins_liga");
            admin.HasKey(a => new { a.UsuarioId, a.TenantId });
            admin.Property(a => a.UsuarioId).HasColumnName("user_id");
            ColumnaDeTenant(admin.Property(a => a.TenantId), "tenant_id");
            admin.Property(a => a.Rol).HasColumnName("rol");
            admin.HasIndex(a => a.TenantId).HasDatabaseName("ix_admins_liga_tenant_id");
            // Sin navegaciones, y Restrict explícito: el Cascade por defecto borraría una liga entera (ADR-14 §2.3).
            admin.HasOne<Liga>().WithMany().HasForeignKey(a => a.TenantId)
                .HasConstraintName("fk_admins_liga_ligas").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Equipo>(equipo =>
        {
            equipo.ToTable("equipos");
            equipo.HasKey(e => e.Id);
            equipo.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            ColumnaDeTenant(equipo.Property(e => e.TenantId), "tenant_id");
            equipo.Property(e => e.Nombre).HasColumnName("nombre");
            equipo.HasIndex(e => e.TenantId).HasDatabaseName("ix_equipos_tenant_id");
            equipo.HasOne<Liga>().WithMany().HasForeignKey(e => e.TenantId)
                .HasConstraintName("fk_equipos_ligas").OnDelete(DeleteBehavior.Restrict);
            equipo.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
    }

    private static void ColumnaDeTenant(PropertyBuilder<TenantId> propiedad, string columna) =>
        propiedad.HasColumnName(columna).HasConversion(t => t.Value, v => new TenantId(v)).ValueGeneratedNever();
}
