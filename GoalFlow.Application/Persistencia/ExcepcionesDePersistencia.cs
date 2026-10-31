namespace GoalFlow.Application.Persistencia;

/// <summary>Se pidió una transacción sin tenant ni identidad (Spec 002 R-23).</summary>
public class ContextoDeTransaccionAusenteException()
    : Exception("No se puede abrir una transacción sin tenant ni identidad.");

/// <summary>SaveChanges sin transacción activa (Spec 002 R-24).</summary>
public class TransaccionRequeridaException()
    : Exception("SaveChanges exige una transacción explícita activa.");

/// <summary>Una entidad intenta guardarse con un tenant ajeno (Spec 002 R-10).</summary>
public class TenantInvalidoException(string entidad)
    : Exception($"La entidad {entidad} tiene un TenantId distinto del tenant actual.");
