using System.Data.Common;
using GoalFlow.Application.Ligas;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GoalFlow.Infrastructure.Persistencia;

/// <summary>Fija el contexto de RLS con alcance de transacción (ADR-14 §2.3). Scoped: nunca singleton.</summary>
public class InterceptorDeTransacciones(ITenantContext tenantContext, IIdentidadAutenticada identidad) : DbTransactionInterceptor
{
    // Un valor vacío equivale a no fijado: las políticas lo convierten en NULL con nullif.
    private const string FijarContexto =
        "select set_config('app.tenant_id', @tenant, true), set_config('app.user_id', @usuario, true)";

    public static void ExigirContexto(ITenantContext tenantContext, IIdentidadAutenticada identidad)
    {
        if (!tenantContext.TieneTenant && identidad.UsuarioId is null)
            throw new ContextoDeTransaccionAusenteException();
    }

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
    {
        ExigirContexto(tenantContext, identidad);
        return result;
    }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        ExigirContexto(tenantContext, identidad);
        return ValueTask.FromResult(result);
    }

    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        using var comando = CrearComando(connection, result);
        comando.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var comando = CrearComando(connection, result);
        await comando.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private DbCommand CrearComando(DbConnection connection, DbTransaction transaccion)
    {
        var comando = connection.CreateCommand();
        comando.Transaction = transaccion;
        comando.CommandText = FijarContexto;
        comando.Parameters.Add(new NpgsqlParameter("tenant", tenantContext.TieneTenant ? tenantContext.Current.Value.ToString() : string.Empty));
        comando.Parameters.Add(new NpgsqlParameter("usuario", identidad.UsuarioId?.ToString() ?? string.Empty));
        return comando;
    }
}
