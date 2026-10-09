// Fixtures de los canarios T-08 (T-02, T-04, T-05): cada namespace contiene UNA violación deliberada.
// Viven solo en el ensamblado de pruebas; los tests reales apuntan a los ensamblados de producción.

namespace GoalFlow.Tests.Architecture.Fixtures.DomainConAspNet
{
    public class DomainViolador
    {
        public Microsoft.AspNetCore.Builder.WebApplication? App;
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.DomainConExtensions
{
    public class DomainViolador
    {
        public Microsoft.Extensions.Logging.ILogger? Logger;
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ApplicationConInfrastructure
{
    public class ApplicationViolador
    {
        public GoalFlow.Infrastructure.AssemblyAppVersion? Adaptador;
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ApplicationConApi
{
    public class ApplicationViolador
    {
        public System.Reflection.Assembly Usar() => GoalFlow.Api.AssemblyReference.Assembly;
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.InfrastructureConApi
{
    public class InfrastructureViolador
    {
        public System.Reflection.Assembly Usar() => GoalFlow.Api.AssemblyReference.Assembly;
    }
}
