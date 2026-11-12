using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GoalFlow.Infrastructure.IA;

/// <summary>Traducción de los únicos 23505 que nombra la Spec 007 (R-19 a, R-19 c, R-22). Cualquier otro error pasa sin cambios.</summary>
internal static class ErroresDeBase
{
    public static bool EsUnicidadDe(DbUpdateException ex, string indice) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == indice;
}
