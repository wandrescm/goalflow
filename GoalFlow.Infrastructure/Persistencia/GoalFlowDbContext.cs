using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Torneos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace GoalFlow.Infrastructure.Persistencia;

public class GoalFlowDbContext(DbContextOptions<GoalFlowDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    public const string Esquema = "goalflow";
    public const string FiltroTenant = "Tenant";

    public DbSet<Liga> Ligas => Set<Liga>();
    public DbSet<AdminLiga> AdminsLiga => Set<AdminLiga>();
    public DbSet<Equipo> Equipos => Set<Equipo>();
    public DbSet<EntregaEvento> EntregasEventos => Set<EntregaEvento>();
    public DbSet<Torneo> Torneos => Set<Torneo>();
    public DbSet<Fase> Fases => Set<Fase>();
    public DbSet<Jugador> Jugadores => Set<Jugador>();
    public DbSet<EquipoTorneo> EquiposTorneo => Set<EquipoTorneo>();
    public DbSet<InscripcionJugador> InscripcionesJugador => Set<InscripcionJugador>();
    public DbSet<Partido> Partidos => Set<Partido>();
    public DbSet<Planilla> Planillas => Set<Planilla>();
    public DbSet<Gol> Goles => Set<Gol>();
    public DbSet<Tarjeta> Tarjetas => Set<Tarjeta>();

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
            ConfiguracionDeTorneos.Equipo(equipo);
            equipo.HasIndex(e => e.TenantId).HasDatabaseName("ix_equipos_tenant_id");
            equipo.HasOne<Liga>().WithMany().HasForeignKey(e => e.TenantId)
                .HasConstraintName("fk_equipos_ligas").OnDelete(DeleteBehavior.Restrict);
            equipo.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });

        modelBuilder.Entity<EntregaEvento>(entrega =>
        {
            ConfiguracionDeEntregaEvento.Configurar(entrega);
            entrega.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });

        // Entidades de torneo (Spec 005). El filtro referencia TenantActual, nunca una variable local.
        modelBuilder.Entity<Torneo>(torneo =>
        {
            ConfiguracionDeTorneos.Torneo(torneo);
            torneo.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<Fase>(fase =>
        {
            ConfiguracionDeTorneos.Fase(fase);
            fase.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<Jugador>(jugador =>
        {
            ConfiguracionDeTorneos.Jugador(jugador);
            jugador.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<EquipoTorneo>(participacion =>
        {
            ConfiguracionDeTorneos.EquipoTorneo(participacion);
            participacion.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<InscripcionJugador>(inscripcion =>
        {
            ConfiguracionDeTorneos.InscripcionJugador(inscripcion);
            inscripcion.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<Partido>(partido =>
        {
            ConfiguracionDeTorneos.Partido(partido);
            partido.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });

        // Planilla post-partido (Spec 006 R-06): mismo filtro Tenant, y RLS en la misma migración.
        modelBuilder.Entity<Planilla>(planilla =>
        {
            ConfiguracionDeTorneos.Planilla(planilla);
            planilla.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<Gol>(gol =>
        {
            ConfiguracionDeTorneos.Gol(gol);
            gol.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
        modelBuilder.Entity<Tarjeta>(tarjeta =>
        {
            ConfiguracionDeTorneos.Tarjeta(tarjeta);
            tarjeta.HasQueryFilter(FiltroTenant, e => e.TenantId == TenantActual);
        });
    }

    // Traduce solo el 23505 del índice de la clave lógica (Spec 004 R-09). SaveChanges() y SaveChangesAsync(ct)
    // llaman a estas sobrecargas. No se desactiva AutoSavepointsEnabled: la transacción debe seguir usable.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException ex) when (EsClaveLogicaDuplicada(ex))
        {
            throw new EntregaDuplicadaException(ex, ClavesDeEntregaPendientes());
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (EsClaveLogicaDuplicada(ex))
        {
            throw new EntregaDuplicadaException(ex, ClavesDeEntregaPendientes());
        }
    }

    // Las filas de entrega que el guardado fallido intentaba insertar siguen Added en el rastreador (ADR-02 hecho 10): sus
    // claves lógicas permiten al productor reconocer su propio duplicado (Spec 006 R-20, C-30).
    private List<ClaveLogicaDeEntrega> ClavesDeEntregaPendientes() =>
        [.. ChangeTracker.Entries<EntregaEvento>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => new ClaveLogicaDeEntrega(
                e.Entity.TenantId, e.Entity.TipoDeEvento, e.Entity.AgregadoId, e.Entity.VersionDelAgregado, e.Entity.Consumidor))];

    private static bool EsClaveLogicaDuplicada(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ConfiguracionDeEntregaEvento.IndiceDeClaveLogica,
        };

    private static void ColumnaDeTenant(PropertyBuilder<TenantId> propiedad, string columna) =>
        propiedad.HasColumnName(columna).HasConversion(t => t.Value, v => new TenantId(v)).ValueGeneratedNever();
}
