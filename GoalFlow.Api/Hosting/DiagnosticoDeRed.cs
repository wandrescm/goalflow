namespace GoalFlow.Api.Hosting;

/// <summary>
/// <c>GET /{slug}/admin/diagnostico/red</c> (Spec 010 Parte A, R-01). Diagnóstico temporal solo para el Admin: se mapea dentro del
/// grupo de Admin y solo si <c>Diagnostico:RedHabilitado</c> es <c>true</c> al arrancar. Apagado o ausente, la ruta no existe y
/// responde <c>404</c> a cualquiera. La Parte B de la Spec lo elimina.
/// </summary>
public static class DiagnosticoDeRed
{
    public const string ClaveDelInterruptor = "Diagnostico:RedHabilitado";

    /// <summary>El interruptor se lee al arrancar. Ausente o cualquier valor que no sea <c>true</c> es apagado.</summary>
    public static bool Habilitado(IConfiguration configuracion) => configuracion.GetValue<bool>(ClaveDelInterruptor);

    public static RouteGroupBuilder MapDiagnosticoDeRed(this RouteGroupBuilder admin)
    {
        admin.MapGet("/diagnostico/red", () => Results.Ok());

        return admin;
    }
}
