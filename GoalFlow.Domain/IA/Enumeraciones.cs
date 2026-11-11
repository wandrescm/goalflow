namespace GoalFlow.Domain.IA;

/// <summary>De dónde salió el texto de un resumen (Spec 007 R-01).</summary>
public enum OrigenDeResumen
{
    Modelo,
    Plantilla,
}

/// <summary>Un resumen guardado nunca está publicado en esta fase: la publicación es de H15 (Spec 007 §7).</summary>
public enum EstadoDeResumen
{
    PorVerificar,
    Borrador,
}

/// <summary>Por qué se guardó un borrador en vez de un texto del modelo (Spec 007 R-01).</summary>
public enum MotivoDeBorrador
{
    Presupuesto,
    LimiteDeLlamadas,
    SolicitudInvalida,
    EntradaExcedida,
    RechazoDelModelo,
}

public enum EstadoDeLlamada
{
    Reservada,
    Conciliada,
}

public static class CodigosDeIa
{
    /// <summary>El código estable que se guarda en la columna <c>motivo_borrador</c>.</summary>
    public static string Codigo(this MotivoDeBorrador motivo) => motivo switch
    {
        MotivoDeBorrador.Presupuesto => "presupuesto",
        MotivoDeBorrador.LimiteDeLlamadas => "limite_de_llamadas",
        MotivoDeBorrador.SolicitudInvalida => "solicitud_invalida",
        MotivoDeBorrador.EntradaExcedida => "entrada_excedida",
        MotivoDeBorrador.RechazoDelModelo => "rechazo_del_modelo",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    public static MotivoDeBorrador MotivoDesdeCodigo(string codigo) => codigo switch
    {
        "presupuesto" => MotivoDeBorrador.Presupuesto,
        "limite_de_llamadas" => MotivoDeBorrador.LimiteDeLlamadas,
        "solicitud_invalida" => MotivoDeBorrador.SolicitudInvalida,
        "entrada_excedida" => MotivoDeBorrador.EntradaExcedida,
        "rechazo_del_modelo" => MotivoDeBorrador.RechazoDelModelo,
        _ => throw new ArgumentOutOfRangeException(nameof(codigo)),
    };
}
