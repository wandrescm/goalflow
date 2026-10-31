// Fixtures de los canarios de la Spec 002 (T-01 y T-02). Raíces separadas de Fixtures.Modulos:
// el canario T-09 existente espera exactamente una violación en esa raíz.

namespace GoalFlow.Tests.Architecture.Fixtures.TenancyCrecido.Tenancy
{
    public class TipoQueNoVaEnTenancy;
}

namespace GoalFlow.Tests.Architecture.Fixtures.GrafoConLigas.Ligas
{
    public class LigaEntidad;
}

namespace GoalFlow.Tests.Architecture.Fixtures.GrafoConLigas.Torneos
{
    using GoalFlow.Tests.Architecture.Fixtures.GrafoConLigas.Ligas;

    /// <summary>Violación deliberada: Torneos no tiene permitido depender de Ligas.</summary>
    public class TorneosViolaPorLigas
    {
        public LigaEntidad? Liga;
    }
}
