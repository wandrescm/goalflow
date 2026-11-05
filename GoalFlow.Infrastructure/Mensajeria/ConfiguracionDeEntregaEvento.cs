using GoalFlow.Domain.Ligas;
using GoalFlow.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoalFlow.Infrastructure.Mensajeria;

public static class ConfiguracionDeEntregaEvento
{
    public const string Tabla = "entregas_eventos";

    /// <summary>El nombre que usa la traducción del 23505 (Spec 004 R-09).</summary>
    public const string IndiceDeClaveLogica = "ux_entregas_eventos_clave_logica";

    /// <summary>El filtro con nombre lo agrega el DbContext, porque referencia uno de sus miembros (ADR-14 §2.2).</summary>
    public static void Configurar(EntityTypeBuilder<EntregaEvento> entrega)
    {
        entrega.ToTable(Tabla);
        entrega.HasKey(e => e.Id);
        entrega.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        entrega.Property(e => e.TenantId).HasColumnName("tenant_id")
            .HasConversion(t => t.Value, v => new TenantId(v)).ValueGeneratedNever();
        entrega.Property(e => e.EventoId).HasColumnName("evento_id");
        entrega.Property(e => e.TipoDeEvento).HasColumnName("tipo_de_evento");
        entrega.Property(e => e.AgregadoId).HasColumnName("agregado_id");
        entrega.Property(e => e.VersionDelAgregado).HasColumnName("version_del_agregado");
        entrega.Property(e => e.Consumidor).HasColumnName("consumidor");
        entrega.Property(e => e.Estado).HasColumnName("estado").HasConversion<string>();
        entrega.Property(e => e.Intentos).HasColumnName("intentos");
        entrega.Property(e => e.TokenDelLease).HasColumnName("token_del_lease");
        entrega.Property(e => e.UltimoError).HasColumnName("ultimo_error");

        // Los tiempos los pone siempre la base (ADR-02 §2.6): nunca viajan desde el reloj de la instancia.
        entrega.Property(e => e.ProximoIntentoEn).HasColumnName("proximo_intento_en")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        entrega.Property(e => e.LeaseVenceEn).HasColumnName("lease_vence_en").HasColumnType("timestamp with time zone");
        entrega.Property(e => e.CreadoEn).HasColumnName("creado_en")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        entrega.Property(e => e.CompletadoEn).HasColumnName("completado_en").HasColumnType("timestamp with time zone");

        entrega.HasIndex(e => new { e.TenantId, e.TipoDeEvento, e.AgregadoId, e.VersionDelAgregado, e.Consumidor })
            .IsUnique().HasDatabaseName(IndiceDeClaveLogica);
        entrega.HasIndex(e => new { e.TenantId, e.Estado, e.ProximoIntentoEn }).HasDatabaseName("ix_entregas_eventos_pendientes");
        entrega.HasIndex(e => new { e.TenantId, e.Estado, e.LeaseVenceEn }).HasDatabaseName("ix_entregas_eventos_leases");

        entrega.HasOne<Liga>().WithMany().HasForeignKey(e => e.TenantId)
            .HasConstraintName("fk_entregas_eventos_ligas").OnDelete(DeleteBehavior.Restrict);
    }
}
