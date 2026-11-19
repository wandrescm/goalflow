using Microsoft.Extensions.Options;

namespace GoalFlow.Api.Hosting;

/// <summary>
/// <c>LimiteDeFrecuencia:{Publica|Admin}:{PorCliente|Global}</c> (Spec 010 R-11). Los valores por defecto viven aquí, en código, y son
/// provisionales (C-11): no se agregan a <c>appsettings.json</c>.
/// </summary>
public sealed class OpcionesDeLimiteDeFrecuencia
{
    public const string Seccion = "LimiteDeFrecuencia";

    public LimitesDeClase Publica { get; set; } = new() { PorCliente = 120, Global = 600 };

    public LimitesDeClase Admin { get; set; } = new() { PorCliente = 60, Global = 120 };
}

public sealed class LimitesDeClase
{
    public int PorCliente { get; set; }

    public int Global { get; set; }
}

/// <summary>Cada límite es un entero mayor o igual a 1; uno inválido detiene el arranque (R-11).</summary>
public sealed class ValidadorDeLimiteDeFrecuencia : IValidateOptions<OpcionesDeLimiteDeFrecuencia>
{
    public ValidateOptionsResult Validate(string? name, OpcionesDeLimiteDeFrecuencia opciones)
    {
        var errores = new List<string>();
        Revisar(errores, "Publica:PorCliente", opciones.Publica.PorCliente);
        Revisar(errores, "Publica:Global", opciones.Publica.Global);
        Revisar(errores, "Admin:PorCliente", opciones.Admin.PorCliente);
        Revisar(errores, "Admin:Global", opciones.Admin.Global);

        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }

    private static void Revisar(List<string> errores, string clave, int valor)
    {
        if (valor < 1) errores.Add($"{OpcionesDeLimiteDeFrecuencia.Seccion}:{clave} must be an integer greater than or equal to 1.");
    }
}
