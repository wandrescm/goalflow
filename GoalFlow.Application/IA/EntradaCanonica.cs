using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoalFlow.Application.Torneos.Contracts;

namespace GoalFlow.Application.IA;

/// <summary>
/// La entrada canónica de un resumen (Spec 007 R-08): exactamente lo que recibe el modelo o la plantilla, en un orden fijo y sin
/// identificadores, códigos, versión, razón de rectificación ni hora. Las listas se ordenan <b>solo por los valores que
/// emiten</b> (lado, nombre ordinal, color, cantidad): los códigos de inscripción no participan (C-02, C-07), así que cambiar
/// qué inscripción tiene un nombre no cambia los bytes ni la huella (R-09). Los elementos que comparan igual son idénticos en bytes.
/// </summary>
public sealed class EntradaCanonica
{
    private byte[]? _bytes;

    private EntradaCanonica(DatosDePartidoParaResumen d)
    {
        Torneo = d.Torneo;
        Fase = d.Fase;
        Jornada = d.Jornada;
        Local = d.Local;
        Visitante = d.Visitante;
        Estado = d.Estado;
        Marcador = d.Marcador;
        Oficial = d.Oficial;
        Motivo = d.Motivo;
        Goles = [.. d.Goles
            .OrderBy(g => g.Equipo).ThenBy(g => g.Jugador, StringComparer.Ordinal).ThenBy(g => g.Cantidad)];
        Autogoles = [.. d.Autogoles
            .OrderBy(a => a.EquipoDelAutor).ThenBy(a => a.Jugador, StringComparer.Ordinal).ThenBy(a => a.Cantidad)];
        GolesSinAutor = d.GolesSinAutor;
        TarjetasInformadas = d.TarjetasInformadas;
        Tarjetas = [.. d.Tarjetas
            .OrderBy(t => t.Equipo).ThenBy(t => t.Jugador, StringComparer.Ordinal)
            .ThenBy(t => t.Color, StringComparer.Ordinal).ThenBy(t => t.Cantidad)];
    }

    public string Torneo { get; }
    public int Fase { get; }
    public int Jornada { get; }
    public string Local { get; }
    public string Visitante { get; }
    public string Estado { get; }
    public MarcadorDelPartido? Marcador { get; }
    public MarcadorDelPartido? Oficial { get; }
    public string? Motivo { get; }
    public IReadOnlyList<GolDelPartido> Goles { get; }
    public IReadOnlyList<AutogolDelPartido> Autogoles { get; }
    public MarcadorDelPartido GolesSinAutor { get; }
    public bool? TarjetasInformadas { get; }
    public IReadOnlyList<TarjetaDelPartido> Tarjetas { get; }

    public static EntradaCanonica Construir(DatosDePartidoParaResumen datos) => new(datos);

    public string NombreDelEquipo(LadoDelEquipo lado) => lado == LadoDelEquipo.Local ? Local : Visitante;

    /// <summary>Los bytes UTF-8 del JSON canónico: sin indentación y sin más escapes que los que JSON exige.</summary>
    public byte[] Bytes() => _bytes ??= Encoding.UTF8.GetBytes(ComoJson());

    /// <summary>El SHA-256 de los bytes, en hexadecimal minúscula de 64 caracteres (R-09).</summary>
    public string Huella() => Convert.ToHexStringLower(SHA256.HashData(Bytes()));

    private string ComoJson()
    {
        var j = new StringBuilder(512);
        j.Append("{\"torneo\":"); Cadena(j, Torneo);
        j.Append(",\"fase\":").Append(Fase.ToString(CultureInfo.InvariantCulture));
        j.Append(",\"jornada\":").Append(Jornada.ToString(CultureInfo.InvariantCulture));
        j.Append(",\"local\":"); Cadena(j, Local);
        j.Append(",\"visitante\":"); Cadena(j, Visitante);
        j.Append(",\"estado\":"); Cadena(j, Estado);
        j.Append(",\"marcador\":"); Marcad(j, Marcador);
        j.Append(",\"oficial\":"); Marcad(j, Oficial);
        j.Append(",\"motivo\":"); if (Motivo is null) j.Append("null"); else Cadena(j, Motivo);

        j.Append(",\"goles\":[");
        Lista(j, Goles, g =>
        {
            j.Append("{\"equipo\":"); Cadena(j, Lado(g.Equipo));
            j.Append(",\"jugador\":"); Cadena(j, g.Jugador);
            j.Append(",\"cantidad\":").Append(g.Cantidad.ToString(CultureInfo.InvariantCulture)).Append('}');
        });

        j.Append("],\"autogoles\":[");
        Lista(j, Autogoles, a =>
        {
            j.Append("{\"jugador\":"); Cadena(j, a.Jugador);
            j.Append(",\"equipoDelAutor\":"); Cadena(j, Lado(a.EquipoDelAutor));
            j.Append(",\"equipoBeneficiado\":"); Cadena(j, Lado(Contrario(a.EquipoDelAutor)));
            j.Append(",\"cantidad\":").Append(a.Cantidad.ToString(CultureInfo.InvariantCulture)).Append('}');
        });

        j.Append("],\"golesSinAutor\":"); Marcad(j, GolesSinAutor);
        j.Append(",\"tarjetasInformadas\":").Append(TarjetasInformadas is null ? "null" : TarjetasInformadas.Value ? "true" : "false");

        j.Append(",\"tarjetas\":[");
        Lista(j, Tarjetas, t =>
        {
            j.Append("{\"equipo\":"); Cadena(j, Lado(t.Equipo));
            j.Append(",\"jugador\":"); Cadena(j, t.Jugador);
            j.Append(",\"color\":"); Cadena(j, t.Color);
            j.Append(",\"cantidad\":").Append(t.Cantidad.ToString(CultureInfo.InvariantCulture)).Append('}');
        });
        j.Append("]}");
        return j.ToString();
    }

    public static LadoDelEquipo Contrario(LadoDelEquipo lado) =>
        lado == LadoDelEquipo.Local ? LadoDelEquipo.Visitante : LadoDelEquipo.Local;

    private static string Lado(LadoDelEquipo lado) => lado == LadoDelEquipo.Local ? "local" : "visitante";

    private static void Lista<T>(StringBuilder j, IReadOnlyList<T> items, Action<T> escribir)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) j.Append(',');
            escribir(items[i]);
        }
    }

    private static void Marcad(StringBuilder j, MarcadorDelPartido? m)
    {
        if (m is null)
        {
            j.Append("null");
            return;
        }

        j.Append("{\"local\":").Append(m.Local.ToString(CultureInfo.InvariantCulture))
            .Append(",\"visitante\":").Append(m.Visitante.ToString(CultureInfo.InvariantCulture)).Append('}');
    }

    // Solo se escapa lo que JSON exige: comillas, barra invertida y caracteres de control (RFC 8259 §7).
    private static void Cadena(StringBuilder j, string valor)
    {
        j.Append('"');
        foreach (var c in valor)
        {
            switch (c)
            {
                case '"': j.Append("\\\""); break;
                case '\\': j.Append("\\\\"); break;
                case '\b': j.Append("\\b"); break;
                case '\f': j.Append("\\f"); break;
                case '\n': j.Append("\\n"); break;
                case '\r': j.Append("\\r"); break;
                case '\t': j.Append("\\t"); break;
                case < ' ': j.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: j.Append(c); break;
            }
        }

        j.Append('"');
    }
}
