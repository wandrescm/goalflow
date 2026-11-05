using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoalFlow.Infrastructure.Mensajeria;

/// <summary>
/// Envía las pistas con TryWrite cuando la transacción confirma y las descarta si se revierte, falla o se
/// libera sin confirmar (Spec 004 R-10). Una pista antes del commit despertaría al ciclo por una fila que
/// todavía no ve. Scoped: comparte <see cref="AvisosPendientes"/> con el publicador de su scope.
/// </summary>
public sealed class InterceptorDeAvisos(AvisosPendientes avisos, CanalDeAvisos canal) : DbTransactionInterceptor
{
    // EF Core no ofrece un hook de liberación: una transacción liberada sin confirmar ni revertir no avisa.
    // Por eso toda transacción nueva descarta lo anotado antes, y el scope lo descarta al liberarse.
    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        avisos.Descartar();
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        avisos.Descartar();
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Enviar();

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Enviar();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        avisos.Descartar();

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        avisos.Descartar();
        return Task.CompletedTask;
    }

    public override void TransactionFailed(
        DbTransaction transaction, TransactionErrorEventData eventData) => avisos.Descartar();

    public override Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        avisos.Descartar();
        return Task.CompletedTask;
    }


    private void Enviar()
    {
        foreach (var tenant in avisos.Tomar())
            canal.IntentarAvisar(tenant); // Si no cabe, se pierde: el temporizador la recupera. El commit no se afecta.
    }
}
