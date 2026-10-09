// Fixtures del canario T-09. Raíz de módulos: GoalFlow.Tests.Architecture.Fixtures.Modulos
// Todavía no hay módulos reales; estos tipos modelan la tabla del ADR-03 §7.

namespace GoalFlow.Tests.Architecture.Fixtures.Modulos.Tenancy
{
    public class TenancyEntidad;
}

namespace GoalFlow.Tests.Architecture.Fixtures.Modulos.Torneos
{
    public class TorneosEntidad;
}

namespace GoalFlow.Tests.Architecture.Fixtures.Modulos.Torneos.Contracts
{
    public class TorneosContrato;
}

namespace GoalFlow.Tests.Architecture.Fixtures.Modulos.Estadisticas
{
    using GoalFlow.Tests.Architecture.Fixtures.Modulos.Tenancy;
    using GoalFlow.Tests.Architecture.Fixtures.Modulos.Torneos;
    using GoalFlow.Tests.Architecture.Fixtures.Modulos.Torneos.Contracts;

    /// <summary>Permitido: Tenancy y Torneos.Contracts.</summary>
    public class EstadisticasPermitida
    {
        public TenancyEntidad? Tenant;
        public TorneosContrato? Contrato;
    }

    /// <summary>Violación deliberada: depende de una entidad de Torneos, no de su Contracts.</summary>
    public class EstadisticasViolaPorEntidad
    {
        public TorneosEntidad? Entidad;
    }
}
