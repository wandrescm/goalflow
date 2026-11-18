namespace GoalFlow.Api.Hosting;

/// <summary>La clase de una petición para el limitador de frecuencia (Spec 010 R-09). Cada petición pertenece a exactamente una.</summary>
public enum ClaseDeRuta
{
    Salud,
    Admin,
    Publica,
}
