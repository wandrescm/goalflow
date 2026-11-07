// Fixtures de los canarios de la Spec 005 (T-20, T-21, T-22). Un namespace por caso; cada canario
// contiene exactamente UNA instrucción de llamada prohibida. Ninguno se ejecuta: solo se compilan.

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.Comun
{
    public class Entidad
    {
        public string Nombre { get; set; } = "";
    }
}

// ---- F1: IgnoreQueryFilters ----

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F1Metodo
{
    using Comun;

    public class Caso
    {
        public IQueryable<Entidad> Consultar(IQueryable<Entidad> q) => q.IgnoreQueryFilters();
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F1Async
{
    using Comun;

    public class Caso
    {
        public async Task<IQueryable<Entidad>> Consultar(IQueryable<Entidad> q)
        {
            await Task.Yield();
            return q.IgnoreQueryFilters();
        }
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F1LambdaSinCapturas
{
    using Comun;

    public class Caso
    {
        public Func<IQueryable<Entidad>, IQueryable<Entidad>> Obtener() => q => q.IgnoreQueryFilters();
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F1LambdaConCaptura
{
    using Comun;

    public class Caso
    {
        public Func<IQueryable<Entidad>> Obtener(IQueryable<Entidad> origen) => () => origen.IgnoreQueryFilters();
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F1LambdaSoloThis
{
    using Comun;

    public class Caso
    {
        private readonly IQueryable<Entidad> _origen = Array.Empty<Entidad>().AsQueryable();

        public Func<IQueryable<Entidad>> Obtener() => () => _origen.IgnoreQueryFilters();
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F1FuncionLocal
{
    using Comun;

    public class Caso
    {
        public IQueryable<Entidad> Consultar(IQueryable<Entidad> q)
        {
            return Local(q);

            static IQueryable<Entidad> Local(IQueryable<Entidad> origen) => origen.IgnoreQueryFilters();
        }
    }
}

// ---- F2: ExecuteSqlRaw ----

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F2Metodo
{
    public class Caso
    {
        public int Ejecutar(DatabaseFacade bd) => bd.ExecuteSqlRaw("select 1");
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F2Async
{
    public class Caso
    {
        public async Task<int> Ejecutar(DatabaseFacade bd)
        {
            await Task.Yield();
            return await bd.ExecuteSqlRawAsync("select 1", CancellationToken.None);
        }
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F2LambdaSinCapturas
{
    public class Caso
    {
        public Func<DatabaseFacade, int> Obtener() => bd => bd.ExecuteSqlRaw("select 1");
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F2LambdaConCaptura
{
    public class Caso
    {
        public Func<int> Obtener(DatabaseFacade bd) => () => bd.ExecuteSqlRaw("select 1");
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F2LambdaSoloThis
{
    public class Caso
    {
        private readonly DatabaseFacade? _bd = null;

        public Func<int> Obtener() => () => _bd!.ExecuteSqlRaw("select 1");
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F2FuncionLocal
{
    public class Caso
    {
        public int Ejecutar(DatabaseFacade bd)
        {
            return Local(bd);

            static int Local(DatabaseFacade origen) => origen.ExecuteSqlRaw("select 1");
        }
    }
}

// ---- F3: comandos ADO.NET ----

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F3NpgsqlCommand
{
    public class Caso
    {
        public int Ejecutar(NpgsqlCommand comando) => comando.ExecuteNonQuery();
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F3DbCommand
{
    public class Caso
    {
        public int Ejecutar(DbCommand comando) => comando.ExecuteNonQuery();
    }
}

// ---- F4: MigrationBuilder.Sql fuera de una Migration ----

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.F4SinMigration
{
    public class Caso
    {
        public void Aplicar(MigrationBuilder constructor) => constructor.Sql("select 1");
    }
}

// ---- T-21: la lista permitida no se decide por namespace ni por nombre simple ----

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.PermitidasPorNamespace.Migraciones
{
    public class NoEsMigration
    {
        public void Aplicar(MigrationBuilder constructor) => constructor.Sql("select 1");
    }
}

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.PermitidasPorNombre
{
    public class InterceptorDeTransacciones
    {
        public int Ejecutar(DbCommand comando) => comando.ExecuteNonQuery();
    }
}

// ---- T-22: sin falsos positivos ----

namespace GoalFlow.Tests.Architecture.Fixtures.ConsultasProhibidas.UsoLegitimo
{
    using Comun;

    public class Caso
    {
        public async Task Usar(DbContext contexto, IQueryable<Entidad> q, CancellationToken ct)
        {
            _ = q.Where(e => e.Nombre != "").AsNoTracking();
            _ = await q.ToListAsync(ct);
            _ = await q.ExecuteUpdateAsync(s => s.SetProperty(e => e.Nombre, "x"), ct);
            _ = await contexto.SaveChangesAsync(ct);
        }
    }
}
