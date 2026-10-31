using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoalFlow.Infrastructure.Persistencia;

/// <summary>Para dotnet ef: genera migraciones y scripts sin conectarse a ninguna base.</summary>
public class GoalFlowDbContextFactory : IDesignTimeDbContextFactory<GoalFlowDbContext>
{
    public GoalFlowDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<GoalFlowDbContext>();
        RegistroDePersistencia.ConfigurarNpgsql(opciones, cadena: null);
        return new GoalFlowDbContext(opciones.Options, new ContextoDeTenant());
    }
}
