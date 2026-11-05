// Fixtures de los canarios de la Spec 004 (T-25, T-26, T-28 y T-29). Cada raíz es independiente:
// las reglas de producción nunca las recorren.

using GoalFlow.Application.Mensajeria;
using GoalFlow.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace GoalFlow.Tests.Architecture.Fixtures.EventosValidos.Torneos.Contracts
{
    /// <summary>Control positivo: cumple R-02 y vive en .Contracts, así la regla no pasa por estar vacía.</summary>
    public sealed record EventoValido(TenantId TenantId, Guid AgregadoId, int Version) : IEventoDeIntegracion
    {
        public static string NombreDelEvento => "fixture.valido";
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.EventosConDatos.Torneos.Contracts
{
    /// <summary>Violación deliberada: lleva un dato del partido además de identificadores.</summary>
    public sealed record EventoConDatos(TenantId TenantId, Guid AgregadoId, int Version, string Marcador) : IEventoDeIntegracion
    {
        public static string NombreDelEvento => "fixture.con-datos";
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.EventosFueraDeContracts.Torneos
{
    /// <summary>Violación deliberada: cumple R-02 pero no vive en .Contracts.</summary>
    public sealed record EventoFueraDeContracts(TenantId TenantId, Guid AgregadoId, int Version) : IEventoDeIntegracion
    {
        public static string NombreDelEvento => "fixture.fuera";
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.EventosConVersionTexto.Torneos.Contracts
{
    /// <summary>Violación deliberada: la propiedad pública Version es texto; el contrato se cumple de forma explícita.</summary>
    public sealed class EventoConVersionTexto : IEventoDeIntegracion
    {
        public static string NombreDelEvento => "fixture.version-texto";

        public TenantId TenantId { get; init; }

        public Guid AgregadoId { get; init; }

        public string Version { get; init; } = "1";

        int IEventoDeIntegracion.Version => int.Parse(Version);
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.EventosConAgregadoEntero.Torneos.Contracts
{
    /// <summary>Violación deliberada: AgregadoId público es un entero; el contrato se cumple de forma explícita.</summary>
    public sealed class EventoConAgregadoEntero : IEventoDeIntegracion
    {
        public static string NombreDelEvento => "fixture.agregado-entero";

        public TenantId TenantId { get; init; }

        public int AgregadoId { get; init; }

        public int Version { get; init; }

        Guid IEventoDeIntegracion.AgregadoId => Guid.Empty;
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ModeloSinFiltro
{
    public class EntidadConTenantSinFiltro
    {
        public Guid Id { get; set; }

        public TenantId TenantId { get; set; }
    }

    /// <summary>Violación deliberada: una entidad con TenantId y sin el filtro con nombre Tenant.</summary>
    public sealed class ContextoSinFiltro(DbContextOptions<ContextoSinFiltro> opciones) : DbContext(opciones)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<EntidadConTenantSinFiltro>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.TenantId).HasConversion(t => t.Value, v => new TenantId(v));
            });
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.GrafoConMensajeria.Mensajeria
{
    /// <summary>Tipo compartido: no es un módulo.</summary>
    public class AyudanteCompartido;
}

namespace GoalFlow.Tests.Architecture.Fixtures.GrafoConMensajeria.Torneos
{
    public class TorneosEntidad;
}

namespace GoalFlow.Tests.Architecture.Fixtures.GrafoConMensajeria.Estadisticas
{
    using GoalFlow.Tests.Architecture.Fixtures.GrafoConMensajeria.Mensajeria;
    using GoalFlow.Tests.Architecture.Fixtures.GrafoConMensajeria.Torneos;

    /// <summary>Violación deliberada: usar Mensajeria no da derecho a tocar la entidad de otro módulo.</summary>
    public class EstadisticasViolaConMensajeria
    {
        public AyudanteCompartido? Ayudante;
        public TorneosEntidad? Entidad;
    }
}
