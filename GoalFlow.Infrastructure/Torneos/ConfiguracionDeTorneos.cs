using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Mapeo de las entidades de torneo (Spec 005 R-02 a R-08, ADR-14 §2.11). Toda clave foránea entre estas tablas es
/// compuesta e incluye tenant_id (y torneo_id dentro de un torneo); toda clave referenciada es una clave alterna
/// que empieza por tenant_id; toda unicidad incluye tenant_id; todo borrado es Restrict. El filtro con nombre
/// Tenant lo agrega el DbContext, porque debe referenciar uno de sus miembros.
/// </summary>
internal static class ConfiguracionDeTorneos
{
    /// <summary>
    /// TenantId es parte de las claves alternas (tenant_id, id): EF Core impediría modificarlo al instante. Se deja
    /// modificable para que el rechazo siga siendo el del InterceptorDeGuardado (Spec 002 R-10, T-07).
    /// </summary>
    private static PropertyBuilder<TenantId> Tenant(PropertyBuilder<TenantId> propiedad) =>
        PermitirCambio(propiedad.HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v)).ValueGeneratedNever());

    private static PropertyBuilder<TenantId> PermitirCambio(PropertyBuilder<TenantId> propiedad)
    {
        propiedad.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Save);
        return propiedad;
    }

    public static void Equipo(EntityTypeBuilder<Equipo> equipo)
    {
        PermitirCambio(equipo.Property(e => e.TenantId));
        equipo.Property(e => e.Codigo).HasColumnName("codigo");
        equipo.HasAlternateKey(e => new { e.TenantId, e.Id }).HasName("ak_equipos_tenant_id_id");
        equipo.HasIndex(e => new { e.TenantId, e.Codigo }).IsUnique().HasDatabaseName("ux_equipos_tenant_codigo");
    }

    public static void Jugador(EntityTypeBuilder<Jugador> jugador)
    {
        jugador.ToTable("jugadores");
        jugador.HasKey(j => j.Id);
        jugador.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(jugador.Property(j => j.TenantId));
        jugador.Property(j => j.Codigo).HasColumnName("codigo");
        jugador.Property(j => j.Nombre).HasColumnName("nombre");
        jugador.HasAlternateKey(j => new { j.TenantId, j.Id }).HasName("ak_jugadores_tenant_id_id");
        jugador.HasIndex(j => new { j.TenantId, j.Codigo }).IsUnique().HasDatabaseName("ux_jugadores_tenant_codigo");
        jugador.HasOne<Liga>().WithMany().HasForeignKey(j => j.TenantId)
            .HasConstraintName("fk_jugadores_ligas").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Torneo(EntityTypeBuilder<Torneo> torneo)
    {
        torneo.ToTable("torneos");
        torneo.HasKey(t => t.Id);
        torneo.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(torneo.Property(t => t.TenantId));
        torneo.Property(t => t.Codigo).HasColumnName("codigo");
        torneo.Property(t => t.Nombre).HasColumnName("nombre");
        torneo.HasAlternateKey(t => new { t.TenantId, t.Id }).HasName("ak_torneos_tenant_id_id");
        torneo.HasIndex(t => new { t.TenantId, t.Codigo }).IsUnique().HasDatabaseName("ux_torneos_tenant_codigo");
        torneo.HasOne<Liga>().WithMany().HasForeignKey(t => t.TenantId)
            .HasConstraintName("fk_torneos_ligas").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Fase(EntityTypeBuilder<Fase> fase)
    {
        fase.ToTable("fases", t =>
        {
            t.HasCheckConstraint("ck_fases_orden", "orden >= 1");
            t.HasCheckConstraint("ck_fases_vueltas", "vueltas in (1, 2)");
            t.HasCheckConstraint("ck_fases_tipo", "tipo in ('TodosContraTodos')");
        });
        fase.HasKey(f => f.Id);
        fase.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(fase.Property(f => f.TenantId));
        fase.Property(f => f.TorneoId).HasColumnName("torneo_id");
        fase.Property(f => f.Orden).HasColumnName("orden");
        fase.Property(f => f.Tipo).HasColumnName("tipo").HasConversion<string>();
        fase.Property(f => f.Vueltas).HasColumnName("vueltas");
        fase.HasAlternateKey(f => new { f.TenantId, f.TorneoId, f.Id }).HasName("ak_fases_tenant_torneo_id");
        fase.HasIndex(f => new { f.TenantId, f.TorneoId, f.Orden }).IsUnique().HasDatabaseName("ux_fases_tenant_torneo_orden");
        fase.HasOne<Torneo>().WithMany().HasForeignKey(f => new { f.TenantId, f.TorneoId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id })
            .HasConstraintName("fk_fases_torneos").OnDelete(DeleteBehavior.Restrict);
    }

    public static void EquipoTorneo(EntityTypeBuilder<EquipoTorneo> participacion)
    {
        participacion.ToTable("equipos_torneo", t =>
            t.HasCheckConstraint("ck_equipos_torneo_estado", "estado in ('Inscrito', 'Retirado')"));
        participacion.HasKey(p => p.Id);
        participacion.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(participacion.Property(p => p.TenantId));
        participacion.Property(p => p.TorneoId).HasColumnName("torneo_id");
        participacion.Property(p => p.EquipoId).HasColumnName("equipo_id");
        participacion.Property(p => p.Estado).HasColumnName("estado").HasConversion<string>();
        participacion.HasAlternateKey(p => new { p.TenantId, p.TorneoId, p.Id }).HasName("ak_equipos_torneo_tenant_torneo_id");
        participacion.HasIndex(p => new { p.TenantId, p.TorneoId, p.EquipoId }).IsUnique()
            .HasDatabaseName("ux_equipos_torneo_tenant_torneo_equipo");
        participacion.HasOne<Torneo>().WithMany().HasForeignKey(p => new { p.TenantId, p.TorneoId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id })
            .HasConstraintName("fk_equipos_torneo_torneos").OnDelete(DeleteBehavior.Restrict);
        participacion.HasOne<Equipo>().WithMany().HasForeignKey(p => new { p.TenantId, p.EquipoId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .HasConstraintName("fk_equipos_torneo_equipos").OnDelete(DeleteBehavior.Restrict);
    }

    public static void InscripcionJugador(EntityTypeBuilder<InscripcionJugador> inscripcion)
    {
        inscripcion.ToTable("inscripciones_jugador");
        inscripcion.HasKey(i => i.Id);
        inscripcion.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(inscripcion.Property(i => i.TenantId));
        inscripcion.Property(i => i.TorneoId).HasColumnName("torneo_id");
        inscripcion.Property(i => i.EquipoTorneoId).HasColumnName("equipo_torneo_id");
        inscripcion.Property(i => i.JugadorId).HasColumnName("jugador_id");
        inscripcion.HasAlternateKey(i => new { i.TenantId, i.TorneoId, i.Id }).HasName("ak_inscripciones_jugador_tenant_torneo_id");
        inscripcion.HasIndex(i => new { i.TenantId, i.TorneoId, i.JugadorId }).IsUnique()
            .HasDatabaseName("ux_inscripciones_jugador_tenant_torneo_jugador");
        inscripcion.HasOne<EquipoTorneo>().WithMany().HasForeignKey(i => new { i.TenantId, i.TorneoId, i.EquipoTorneoId })
            .HasPrincipalKey(p => new { p.TenantId, p.TorneoId, p.Id })
            .HasConstraintName("fk_inscripciones_jugador_equipos_torneo").OnDelete(DeleteBehavior.Restrict);
        inscripcion.HasOne<Jugador>().WithMany().HasForeignKey(i => new { i.TenantId, i.JugadorId })
            .HasPrincipalKey(j => new { j.TenantId, j.Id })
            .HasConstraintName("fk_inscripciones_jugador_jugadores").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Partido(EntityTypeBuilder<Partido> partido)
    {
        partido.ToTable("partidos", t =>
        {
            t.HasCheckConstraint("ck_partidos_jornada", "jornada >= 1");
            t.HasCheckConstraint("ck_partidos_local_distinto_visitante", "local_id <> visitante_id");
            t.HasCheckConstraint("ck_partidos_estado", "estado in ('Programado', 'Cancelado', 'Jugado', 'Suspendido', 'Adjudicado')");
            t.HasCheckConstraint("ck_partidos_version", "version >= 0");
        });
        partido.HasKey(p => p.Id);
        partido.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(partido.Property(p => p.TenantId));
        partido.Property(p => p.TorneoId).HasColumnName("torneo_id");
        partido.Property(p => p.FaseId).HasColumnName("fase_id");
        partido.Property(p => p.Jornada).HasColumnName("jornada");
        partido.Property(p => p.LocalId).HasColumnName("local_id");
        partido.Property(p => p.VisitanteId).HasColumnName("visitante_id");
        partido.Property(p => p.ProgramadoEn).HasColumnName("programado_en");
        partido.Property(p => p.Estado).HasColumnName("estado").HasConversion<string>();
        partido.Property(p => p.Version).HasColumnName("version");

        // Token de concurrencia de fila (Spec 006 R-20): la columna de sistema xmin, que cambia en cada UPDATE de la fila,
        // sea de la planilla o de la importación. Npgsql la mapea sin agregar ninguna columna: la migración no la crea.
        partido.Property<uint>("xmin").IsRowVersion();

        partido.HasAlternateKey(p => new { p.TenantId, p.TorneoId, p.Id }).HasName("ak_partidos_tenant_torneo_id");
        partido.HasIndex(p => new { p.TenantId, p.FaseId, p.LocalId, p.VisitanteId }).IsUnique()
            .HasDatabaseName("ux_partidos_tenant_fase_local_visitante");
        partido.HasOne<Fase>().WithMany().HasForeignKey(p => new { p.TenantId, p.TorneoId, p.FaseId })
            .HasPrincipalKey(f => new { f.TenantId, f.TorneoId, f.Id })
            .HasConstraintName("fk_partidos_fases").OnDelete(DeleteBehavior.Restrict);
        partido.HasOne<EquipoTorneo>().WithMany().HasForeignKey(p => new { p.TenantId, p.TorneoId, p.LocalId })
            .HasPrincipalKey(e => new { e.TenantId, e.TorneoId, e.Id })
            .HasConstraintName("fk_partidos_local").OnDelete(DeleteBehavior.Restrict);
        partido.HasOne<EquipoTorneo>().WithMany().HasForeignKey(p => new { p.TenantId, p.TorneoId, p.VisitanteId })
            .HasPrincipalKey(e => new { e.TenantId, e.TorneoId, e.Id })
            .HasConstraintName("fk_partidos_visitante").OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>El nombre del índice único de la versión: el adaptador traduce su 23505 a conflicto de versión (Spec 006 R-20).</summary>
    public const string IndiceDeVersionDePlanilla = "ux_planillas_tenant_partido_version";

    public static void Planilla(EntityTypeBuilder<Planilla> planilla)
    {
        planilla.ToTable("planillas", t =>
        {
            t.HasCheckConstraint("ck_planillas_version", "version >= 1");
            t.HasCheckConstraint("ck_planillas_estado", "estado in ('Programado', 'Jugado', 'Suspendido', 'Adjudicado')");
            t.HasCheckConstraint("ck_planillas_marcador",
                "(marcador_local is null and marcador_visitante is null) or "
                + "(marcador_local between 0 and 99 and marcador_visitante between 0 and 99)");
            t.HasCheckConstraint("ck_planillas_oficial",
                "(oficial_local is null and oficial_visitante is null) or "
                + "(oficial_local between 0 and 99 and oficial_visitante between 0 and 99)");
            t.HasCheckConstraint("ck_planillas_motivo",
                "motivo is null or motivo in ('Clima', 'Luz', 'Disturbios', 'WalkOver', 'Sancion', 'Otro')");
            t.HasCheckConstraint("ck_planillas_motivo_rectificacion",
                "motivo_rectificacion is null or motivo_rectificacion in "
                + "('ErrorDeRegistro', 'PartidoEquivocado', 'DecisionReglamentaria', 'Otro')");
        });
        planilla.HasKey(p => p.Id);
        planilla.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(planilla.Property(p => p.TenantId));
        planilla.Property(p => p.TorneoId).HasColumnName("torneo_id");
        planilla.Property(p => p.PartidoId).HasColumnName("partido_id");
        planilla.Property(p => p.Version).HasColumnName("version");
        planilla.Property(p => p.Estado).HasColumnName("estado").HasConversion<string>();
        planilla.Property(p => p.MarcadorLocal).HasColumnName("marcador_local");
        planilla.Property(p => p.MarcadorVisitante).HasColumnName("marcador_visitante");
        planilla.Property(p => p.OficialLocal).HasColumnName("oficial_local");
        planilla.Property(p => p.OficialVisitante).HasColumnName("oficial_visitante");
        planilla.Property(p => p.Motivo).HasColumnName("motivo").HasConversion<string>();
        planilla.Property(p => p.MotivoRectificacion).HasColumnName("motivo_rectificacion").HasConversion<string>();
        planilla.Property(p => p.TarjetasInformadas).HasColumnName("tarjetas_informadas");
        planilla.Property(p => p.GuardadoPor).HasColumnName("guardado_por");

        // La hora de guardado la pone siempre la base, en UTC (Spec 006 §4); la aplicación nunca envía un timestamp.
        planilla.Property(p => p.GuardadoEn).HasColumnName("guardado_en")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        planilla.HasAlternateKey(p => new { p.TenantId, p.TorneoId, p.Id }).HasName("ak_planillas_tenant_torneo_id");
        planilla.HasIndex(p => new { p.TenantId, p.PartidoId, p.Version }).IsUnique().HasDatabaseName(IndiceDeVersionDePlanilla);
        planilla.HasOne<Partido>().WithMany().HasForeignKey(p => new { p.TenantId, p.TorneoId, p.PartidoId })
            .HasPrincipalKey(m => new { m.TenantId, m.TorneoId, m.Id })
            .HasConstraintName("fk_planillas_partidos").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Gol(EntityTypeBuilder<Gol> gol)
    {
        gol.ToTable("goles", t =>
        {
            t.HasCheckConstraint("ck_goles_tipo", "tipo in ('Jugador', 'Autogol', 'SinAutor')");
            t.HasCheckConstraint("ck_goles_cantidad", "cantidad >= 1");
            t.HasCheckConstraint("ck_goles_inscripcion_segun_tipo", "(tipo = 'SinAutor') = (inscripcion_id is null)");
        });
        gol.HasKey(g => g.Id);
        gol.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(gol.Property(g => g.TenantId));
        gol.Property(g => g.TorneoId).HasColumnName("torneo_id");
        gol.Property(g => g.PlanillaId).HasColumnName("planilla_id");
        gol.Property(g => g.Tipo).HasColumnName("tipo").HasConversion<string>();
        gol.Property(g => g.EquipoTorneoId).HasColumnName("equipo_torneo_id");
        gol.Property(g => g.InscripcionId).HasColumnName("inscripcion_id");
        gol.Property(g => g.Cantidad).HasColumnName("cantidad");

        // Una fila por (planilla, tipo, equipo acreditado, inscripción), y la ausencia de inscripción cuenta como valor:
        // los goles sin autor son una sola fila por planilla y equipo (R-03). Requiere PostgreSQL 15 o posterior.
        gol.HasIndex(g => new { g.TenantId, g.PlanillaId, g.Tipo, g.EquipoTorneoId, g.InscripcionId })
            .IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_goles_tenant_planilla_tipo_equipo_inscripcion");
        gol.HasOne<Planilla>().WithMany().HasForeignKey(g => new { g.TenantId, g.TorneoId, g.PlanillaId })
            .HasPrincipalKey(p => new { p.TenantId, p.TorneoId, p.Id })
            .HasConstraintName("fk_goles_planillas").OnDelete(DeleteBehavior.Restrict);
        gol.HasOne<EquipoTorneo>().WithMany().HasForeignKey(g => new { g.TenantId, g.TorneoId, g.EquipoTorneoId })
            .HasPrincipalKey(e => new { e.TenantId, e.TorneoId, e.Id })
            .HasConstraintName("fk_goles_equipos_torneo").OnDelete(DeleteBehavior.Restrict);
        gol.HasOne<InscripcionJugador>().WithMany().HasForeignKey(g => new { g.TenantId, g.TorneoId, g.InscripcionId })
            .HasPrincipalKey(i => new { i.TenantId, i.TorneoId, i.Id })
            .HasConstraintName("fk_goles_inscripciones_jugador").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Tarjeta(EntityTypeBuilder<Tarjeta> tarjeta)
    {
        tarjeta.ToTable("tarjetas", t =>
        {
            t.HasCheckConstraint("ck_tarjetas_color", "color in ('Amarilla', 'Azul', 'Roja')");
            t.HasCheckConstraint("ck_tarjetas_cantidad", "cantidad between 1 and 9");
        });
        tarjeta.HasKey(c => c.Id);
        tarjeta.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(tarjeta.Property(c => c.TenantId));
        tarjeta.Property(c => c.TorneoId).HasColumnName("torneo_id");
        tarjeta.Property(c => c.PlanillaId).HasColumnName("planilla_id");
        tarjeta.Property(c => c.InscripcionId).HasColumnName("inscripcion_id");
        tarjeta.Property(c => c.Color).HasColumnName("color").HasConversion<string>();
        tarjeta.Property(c => c.Cantidad).HasColumnName("cantidad");

        tarjeta.HasIndex(c => new { c.TenantId, c.PlanillaId, c.InscripcionId, c.Color }).IsUnique()
            .HasDatabaseName("ux_tarjetas_tenant_planilla_inscripcion_color");
        tarjeta.HasOne<Planilla>().WithMany().HasForeignKey(c => new { c.TenantId, c.TorneoId, c.PlanillaId })
            .HasPrincipalKey(p => new { p.TenantId, p.TorneoId, p.Id })
            .HasConstraintName("fk_tarjetas_planillas").OnDelete(DeleteBehavior.Restrict);
        tarjeta.HasOne<InscripcionJugador>().WithMany().HasForeignKey(c => new { c.TenantId, c.TorneoId, c.InscripcionId })
            .HasPrincipalKey(i => new { i.TenantId, i.TorneoId, i.Id })
            .HasConstraintName("fk_tarjetas_inscripciones_jugador").OnDelete(DeleteBehavior.Restrict);
    }
}
