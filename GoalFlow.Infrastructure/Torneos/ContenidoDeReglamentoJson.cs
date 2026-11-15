using System.Text.Json;
using GoalFlow.Domain.Torneos;

namespace GoalFlow.Infrastructure.Torneos;

/// <summary>
/// Forma canónica del contenido del reglamento en la columna <c>contenido</c> (Spec 009 R-01, R-03): un objeto JSON sin
/// <c>estado</c>, con las secciones ausentes omitidas. Se escribe y se lee a mano para que el dominio no dependa de
/// atributos de serialización y para que el orden de los miembros sea fijo.
/// </summary>
internal static class ContenidoDeReglamentoJson
{
    public static string Escribir(ContenidoDeReglamento contenido)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria))
        {
            escritor.WriteStartObject();
            if (contenido.Puntos is { } p)
            {
                escritor.WriteStartObject("puntos");
                escritor.WriteNumber("victoria", p.Victoria);
                escritor.WriteNumber("empate", p.Empate);
                escritor.WriteNumber("derrota", p.Derrota);
                escritor.WriteEndObject();
            }

            if (contenido.Desempate is { } d)
            {
                escritor.WriteStartObject("desempate");
                escritor.WriteStartArray("criterios");
                foreach (var criterio in d.Criterios) escritor.WriteStringValue(criterio.ToString());
                escritor.WriteEndArray();
                escritor.WriteBoolean("directoRecursivo", d.DirectoRecursivo);
                escritor.WriteEndObject();
            }

            if (contenido.FairPlay is { } f)
            {
                escritor.WriteStartObject("fairPlay");
                escritor.WriteNumber("amarilla", f.Amarilla);
                escritor.WriteNumber("azul", f.Azul);
                escritor.WriteNumber("roja", f.Roja);
                escritor.WriteEndObject();
            }

            if (contenido.Adjudicacion is { } a)
            {
                escritor.WriteStartObject("adjudicacion");
                Motivo(escritor, nameof(MotivoDeResultado.WalkOver), a.WalkOver);
                Motivo(escritor, nameof(MotivoDeResultado.Sancion), a.Sancion);
                Motivo(escritor, nameof(MotivoDeResultado.Otro), a.Otro);
                escritor.WriteEndObject();
            }

            escritor.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(memoria.ToArray());
    }

    private static void Motivo(Utf8JsonWriter escritor, string nombre, AdjudicacionDeMotivo? motivo)
    {
        if (motivo is null) return;
        escritor.WriteStartObject(nombre);
        escritor.WriteNumber("puntosGanador", motivo.PuntosGanador);
        escritor.WriteNumber("puntosPerdedor", motivo.PuntosPerdedor);
        escritor.WriteNumber("puntosEmpate", motivo.PuntosEmpate);
        escritor.WriteNumber("deduccionPerdedor", motivo.DeduccionPerdedor);
        escritor.WriteNumber("deduccionEmpate", motivo.DeduccionEmpate);
        escritor.WriteBoolean("cuentaComoJugado", motivo.CuentaComoJugado);
        escritor.WriteBoolean("golesOficialesEnTabla", motivo.GolesOficialesEnTabla);
        escritor.WriteBoolean("golesOficialesEnValla", motivo.GolesOficialesEnValla);
        escritor.WriteBoolean("golesDeportivosEnGoleadores", motivo.GolesDeportivosEnGoleadores);
        escritor.WriteBoolean("tarjetasEnFairPlay", motivo.TarjetasEnFairPlay);
        escritor.WriteEndObject();
    }

    public static ContenidoDeReglamento Leer(string json)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;

        PuntosDeReglamento? puntos = null;
        if (raiz.TryGetProperty("puntos", out var p))
            puntos = new PuntosDeReglamento(Int(p, "victoria"), Int(p, "empate"), Int(p, "derrota"));

        DesempateDeReglamento? desempate = null;
        if (raiz.TryGetProperty("desempate", out var d))
        {
            var criterios = d.GetProperty("criterios").EnumerateArray()
                .Select(c => Enum.Parse<CriterioDeDesempate>(c.GetString()!)).ToList();
            desempate = new DesempateDeReglamento(criterios, d.GetProperty("directoRecursivo").GetBoolean());
        }

        FairPlayDeReglamento? fairPlay = null;
        if (raiz.TryGetProperty("fairPlay", out var f))
            fairPlay = new FairPlayDeReglamento(Int(f, "amarilla"), Int(f, "azul"), Int(f, "roja"));

        AdjudicacionDeReglamento? adjudicacion = null;
        if (raiz.TryGetProperty("adjudicacion", out var a))
            adjudicacion = new AdjudicacionDeReglamento(
                Motivo(a, nameof(MotivoDeResultado.WalkOver)), Motivo(a, nameof(MotivoDeResultado.Sancion)), Motivo(a, nameof(MotivoDeResultado.Otro)));

        return new ContenidoDeReglamento(puntos, desempate, fairPlay, adjudicacion);
    }

    private static int Int(JsonElement elemento, string nombre) => elemento.GetProperty(nombre).GetInt32();

    private static AdjudicacionDeMotivo? Motivo(JsonElement adjudicacion, string nombre)
    {
        if (!adjudicacion.TryGetProperty(nombre, out var m)) return null;
        return new AdjudicacionDeMotivo(
            Int(m, "puntosGanador"), Int(m, "puntosPerdedor"), Int(m, "puntosEmpate"), Int(m, "deduccionPerdedor"), Int(m, "deduccionEmpate"),
            m.GetProperty("cuentaComoJugado").GetBoolean(), m.GetProperty("golesOficialesEnTabla").GetBoolean(),
            m.GetProperty("golesOficialesEnValla").GetBoolean(), m.GetProperty("golesDeportivosEnGoleadores").GetBoolean(),
            m.GetProperty("tarjetasEnFairPlay").GetBoolean());
    }
}
