using GoalFlow.Domain.IA;
using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Domain.Torneos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoalFlow.Infrastructure.IA;

/// <summary>
/// Mapeo de las tres tablas de IA (Spec 007 R-01 a R-04). El filtro con nombre <c>Tenant</c> lo agrega el DbContext,
/// porque referencia uno de sus miembros (ADR-14 §2.2). Los tiempos los pone siempre la base.
/// </summary>
public static class ConfiguracionDeIa
{
    /// <summary>Índice único de (tenant, partido, huella): el adaptador traduce su 23505 a "ya existe" (R-22).</summary>
    public const string IndiceDeResumenPorHuella = "ux_resumenes_partido_tenant_partido_huella";

    /// <summary>Índice único de (tenant, partido, huella, número): el adaptador traduce su 23505 (R-19 c).</summary>
    public const string IndiceDeNumeroDeLlamada = "ux_llamadas_ia_tenant_partido_huella_numero";

    /// <summary>Índice único de (tenant, mes): el adaptador traduce su 23505 (R-19 a).</summary>
    public const string IndiceDeContadorPorMes = "ux_presupuestos_ia_tenant_mes";

    private const string FormatoDeMes = "mes ~ '^[0-9]{4}-(0[1-9]|1[0-2])$'";

    private static void Tenant(PropertyBuilder<TenantId> propiedad) =>
        propiedad.HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v)).ValueGeneratedNever();

    public static void Resumen(EntityTypeBuilder<ResumenPartido> resumen)
    {
        resumen.ToTable("resumenes_partido", t =>
        {
            t.HasCheckConstraint("ck_resumenes_partido_version_origen", "version_origen >= 1");
            t.HasCheckConstraint("ck_resumenes_partido_huella", "huella ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("ck_resumenes_partido_origen", "origen in ('Modelo', 'Plantilla')");
            t.HasCheckConstraint("ck_resumenes_partido_estado", "estado in ('PorVerificar', 'Borrador')");
            t.HasCheckConstraint("ck_resumenes_partido_motivo",
                "motivo_borrador is null or motivo_borrador in "
                + "('presupuesto', 'limite_de_llamadas', 'solicitud_invalida', 'entrada_excedida', 'rechazo_del_modelo')");
            t.HasCheckConstraint("ck_resumenes_partido_motivo_segun_estado", "(estado = 'Borrador') = (motivo_borrador is not null)");
            t.HasCheckConstraint("ck_resumenes_partido_texto", "char_length(texto) between 1 and 4000");
            t.HasCheckConstraint("ck_resumenes_partido_modelo_segun_origen", "(origen = 'Modelo') = (modelo is not null)");
            t.HasCheckConstraint("ck_resumenes_partido_version_prompt_segun_origen", "(origen = 'Modelo') = (version_prompt is not null)");
            t.HasCheckConstraint("ck_resumenes_partido_llamada_segun_origen", "(origen = 'Modelo') = (llamada_id is not null)");
        });
        resumen.HasKey(r => r.Id);
        resumen.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(resumen.Property(r => r.TenantId));
        resumen.Property(r => r.TorneoId).HasColumnName("torneo_id");
        resumen.Property(r => r.PartidoId).HasColumnName("partido_id");
        resumen.Property(r => r.VersionOrigen).HasColumnName("version_origen");
        resumen.Property(r => r.Huella).HasColumnName("huella");
        resumen.Property(r => r.Origen).HasColumnName("origen").HasConversion<string>();
        resumen.Property(r => r.Estado).HasColumnName("estado").HasConversion<string>();
        resumen.Property(r => r.MotivoBorrador).HasColumnName("motivo_borrador")
            .HasConversion(m => m!.Value.Codigo(), c => CodigosDeIa.MotivoDesdeCodigo(c));
        resumen.Property(r => r.Texto).HasColumnName("texto");
        resumen.Property(r => r.Modelo).HasColumnName("modelo");
        resumen.Property(r => r.VersionPrompt).HasColumnName("version_prompt");
        resumen.Property(r => r.LlamadaId).HasColumnName("llamada_id");
        resumen.Property(r => r.CreadoEn).HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        resumen.HasIndex(r => new { r.TenantId, r.PartidoId, r.Huella }).IsUnique().HasDatabaseName(IndiceDeResumenPorHuella);
        resumen.HasOne<Partido>().WithMany().HasForeignKey(r => new { r.TenantId, r.TorneoId, r.PartidoId })
            .HasPrincipalKey(p => new { p.TenantId, p.TorneoId, p.Id })
            .HasConstraintName("fk_resumenes_partido_partidos").OnDelete(DeleteBehavior.Restrict);
        resumen.HasOne<LlamadaIa>().WithMany().HasForeignKey(r => new { r.TenantId, r.LlamadaId })
            .HasPrincipalKey(l => new { l.TenantId, l.Id })
            .HasConstraintName("fk_resumenes_partido_llamadas").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Llamada(EntityTypeBuilder<LlamadaIa> llamada)
    {
        llamada.ToTable("llamadas_ia", t =>
        {
            t.HasCheckConstraint("ck_llamadas_ia_version", "version >= 1");
            t.HasCheckConstraint("ck_llamadas_ia_numero", "numero >= 1");
            t.HasCheckConstraint("ck_llamadas_ia_intento_entrega", "intento_entrega >= 1");
            t.HasCheckConstraint("ck_llamadas_ia_mes", FormatoDeMes);
            t.HasCheckConstraint("ck_llamadas_ia_reserva", "reserva > 0");
            t.HasCheckConstraint("ck_llamadas_ia_estado", "estado in ('Reservada', 'Conciliada')");
            t.HasCheckConstraint("ck_llamadas_ia_tokens", "coalesce(tokens_entrada, 0) >= 0 and coalesce(tokens_salida, 0) >= 0");
            t.HasCheckConstraint("ck_llamadas_ia_costo", "costo is null or costo >= 0");
            t.HasCheckConstraint("ck_llamadas_ia_resultado", "resultado is null or char_length(resultado) between 1 and 64");
            t.HasCheckConstraint("ck_llamadas_ia_tokens_entrada_segun_estado", "(estado = 'Conciliada') = (tokens_entrada is not null)");
            t.HasCheckConstraint("ck_llamadas_ia_tokens_salida_segun_estado", "(estado = 'Conciliada') = (tokens_salida is not null)");
            t.HasCheckConstraint("ck_llamadas_ia_costo_segun_estado", "(estado = 'Conciliada') = (costo is not null)");
            t.HasCheckConstraint("ck_llamadas_ia_resultado_segun_estado", "(estado = 'Conciliada') = (resultado is not null)");
            t.HasCheckConstraint("ck_llamadas_ia_conciliada_en_segun_estado", "(estado = 'Conciliada') = (conciliada_en is not null)");
        });
        llamada.HasKey(l => l.Id);
        llamada.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(llamada.Property(l => l.TenantId));
        llamada.Property(l => l.TorneoId).HasColumnName("torneo_id");
        llamada.Property(l => l.PartidoId).HasColumnName("partido_id");
        llamada.Property(l => l.Version).HasColumnName("version");
        llamada.Property(l => l.Huella).HasColumnName("huella");
        llamada.Property(l => l.Numero).HasColumnName("numero");
        llamada.Property(l => l.IntentoEntrega).HasColumnName("intento_entrega");
        llamada.Property(l => l.Mes).HasColumnName("mes");
        llamada.Property(l => l.Reserva).HasColumnName("reserva");
        llamada.Property(l => l.Estado).HasColumnName("estado").HasConversion<string>();
        llamada.Property(l => l.TokensEntrada).HasColumnName("tokens_entrada");
        llamada.Property(l => l.TokensSalida).HasColumnName("tokens_salida");
        llamada.Property(l => l.Costo).HasColumnName("costo");
        llamada.Property(l => l.Resultado).HasColumnName("resultado");
        llamada.Property(l => l.ConciliadaEn).HasColumnName("conciliada_en").HasColumnType("timestamp with time zone");
        llamada.Property(l => l.CreadoEn).HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAdd();

        llamada.HasAlternateKey(l => new { l.TenantId, l.Id }).HasName("ak_llamadas_ia_tenant_id");
        llamada.HasIndex(l => new { l.TenantId, l.PartidoId, l.Huella, l.Numero }).IsUnique()
            .HasDatabaseName(IndiceDeNumeroDeLlamada);
        llamada.HasOne<Partido>().WithMany().HasForeignKey(l => new { l.TenantId, l.TorneoId, l.PartidoId })
            .HasPrincipalKey(p => new { p.TenantId, p.TorneoId, p.Id })
            .HasConstraintName("fk_llamadas_ia_partidos").OnDelete(DeleteBehavior.Restrict);
    }

    public static void Presupuesto(EntityTypeBuilder<PresupuestoIa> presupuesto)
    {
        presupuesto.ToTable("presupuestos_ia", t =>
        {
            t.HasCheckConstraint("ck_presupuestos_ia_mes", FormatoDeMes);
            t.HasCheckConstraint("ck_presupuestos_ia_tope", "tope > 0");
            t.HasCheckConstraint("ck_presupuestos_ia_reservado", "reservado >= 0");
            t.HasCheckConstraint("ck_presupuestos_ia_consumido", "consumido >= 0");
        });
        presupuesto.HasKey(p => p.Id);
        presupuesto.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        Tenant(presupuesto.Property(p => p.TenantId));
        presupuesto.Property(p => p.Mes).HasColumnName("mes");
        presupuesto.Property(p => p.Tope).HasColumnName("tope");
        presupuesto.Property(p => p.Reservado).HasColumnName("reservado");
        presupuesto.Property(p => p.Consumido).HasColumnName("consumido");

        presupuesto.HasIndex(p => new { p.TenantId, p.Mes }).IsUnique().HasDatabaseName(IndiceDeContadorPorMes);
        presupuesto.HasOne<Liga>().WithMany().HasForeignKey(p => p.TenantId)
            .HasConstraintName("fk_presupuestos_ia_ligas").OnDelete(DeleteBehavior.Restrict);
    }
}
