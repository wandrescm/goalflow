using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoalFlow.Application.IA;

namespace GoalFlow.Infrastructure.IA;

/// <summary>
/// El adaptador de la API de Mensajes de Anthropic, con un <see cref="HttpClient"/> propio (Spec 007 R-28 a R-30, ADR-12 §2.1):
/// sin paquetes nuevos y sin reintentos en memoria. Rutas y encabezados verificados en la documentación oficial el 2026-11-11:
/// <c>POST /v1/messages</c>, <c>POST /v1/messages/count_tokens</c>, <c>x-api-key</c> y <c>anthropic-version</c>.
/// Nunca registra nada: ni la clave, ni el cuerpo, ni los mensajes de excepción (R-27); el mensaje de error del proveedor no se copia a
/// ninguna excepción.
/// </summary>
public sealed class AdaptadorAnthropic(HttpClient http, OpcionesDeIA opciones) : IGeneradorDeTexto
{
    public const string RutaDeMensajes = "/v1/messages";
    public const string RutaDeConteo = "/v1/messages/count_tokens";
    public const string VersionDeApi = "2026-10-21";
    public const string UrlBase = "https://api.anthropic.com";

    public async Task<int> ContarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken)
    {
        // Los mismos miembros que la generación, menos max_tokens: el endpoint de conteo no lo define (R-12 lista modelo, sistema,
        // mensaje, formato de salida y razonamiento). Ver el registro del paso 0 en la descripción del PR.
        var (estado, cuerpo) = await EnviarAsync(RutaDeConteo, CuerpoDe(solicitud, conMaxTokens: false), cancellationToken);
        if (estado is < 200 or > 299) throw new RespuestaConErrorException(estado, UsoDeError(cuerpo));

        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            if (json.RootElement.TryGetProperty("input_tokens", out var n) && n.TryGetInt32(out var tokens) && tokens >= 0)
                return tokens;
        }
        catch (JsonException)
        {
            // cae a la salida inválida
        }

        throw new SalidaInvalidaException(null);
    }

    public async Task<RespuestaGenerada> GenerarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken)
    {
        var (estado, cuerpo) = await EnviarAsync(RutaDeMensajes, CuerpoDe(solicitud, conMaxTokens: true), cancellationToken);
        if (estado is < 200 or > 299) throw new RespuestaConErrorException(estado, UsoDeError(cuerpo));

        UsoReportado? uso = null;
        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            var raiz = json.RootElement;
            uso = LeerUso(raiz);

            // Los bloques se eligen por tipo, nunca por posición: una respuesta puede empezar con un bloque de razonamiento (R-24).
            var texto = new StringBuilder();
            foreach (var bloque in raiz.GetProperty("content").EnumerateArray())
                if (bloque.TryGetProperty("type", out var tipo) && tipo.GetString() == "text")
                    texto.Append(bloque.GetProperty("text").GetString());

            var stop = raiz.GetProperty("stop_reason").GetString();
            if (uso is null || string.IsNullOrEmpty(stop)) throw new SalidaInvalidaException(uso);

            return new RespuestaGenerada(texto.ToString(), stop, uso.TokensEntrada, uso.TokensSalida);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // InvalidOperationException y KeyNotFoundException: un miembro faltante o de otro tipo.
            throw new SalidaInvalidaException(uso);
        }
    }

    private JsonObject CuerpoDe(SolicitudDeGeneracion s, bool conMaxTokens)
    {
        var cuerpo = new JsonObject { ["model"] = opciones.Modelo };
        if (conMaxTokens) cuerpo["max_tokens"] = s.MaxTokensDeSalida;
        cuerpo["system"] = s.TextoDeSistema;
        cuerpo["messages"] = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["content"] = InstruccionesDeResumen.MensajeDeUsuario(s.EntradaCanonica),
        });
        cuerpo["output_config"] = new JsonObject
        {
            ["format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["schema"] = JsonNode.Parse(InstruccionesDeResumen.EsquemaDeSalida),
            },
        };

        // Con Adaptativo el miembro se omite y aplica el razonamiento adaptativo del modelo (R-29).
        if (s.Razonamiento == RazonamientoDeIA.Deshabilitado)
            cuerpo["thinking"] = new JsonObject { ["type"] = "disabled" };
        return cuerpo;
    }

    private async Task<(int Estado, string Cuerpo)> EnviarAsync(string ruta, JsonObject cuerpo, CancellationToken cancellationToken)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta)
        {
            Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Add("x-api-key", opciones.ClaveApi);
        peticion.Headers.Add("anthropic-version", VersionDeApi);

        try
        {
            using var respuesta = await http.SendAsync(peticion, cancellationToken);
            return ((int)respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // lo decide quien llama: apagado o su propio límite
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or IOException)
        {
            // Sin respuesta: tiempo agotado o conexión perdida. Pudo cobrarse (R-21 f).
            throw new ResultadoInciertoException(interna: ex);
        }
    }

    private static UsoReportado? LeerUso(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("usage", out var uso) || uso.ValueKind != JsonValueKind.Object) return null;
        if (uso.TryGetProperty("input_tokens", out var e) && e.TryGetInt32(out var entrada)
            && uso.TryGetProperty("output_tokens", out var s) && s.TryGetInt32(out var salida))
            return new UsoReportado(entrada, salida);
        return null;
    }

    // Un cuerpo de error rara vez trae uso; si lo trae se conserva para conciliar (R-21 c). Nunca se copia su mensaje.
    private static UsoReportado? UsoDeError(string cuerpo)
    {
        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            return json.RootElement.ValueKind == JsonValueKind.Object ? LeerUso(json.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
