using GoalFlow.Application.IA;

namespace GoalFlow.Tests.IA;

/// <summary>
/// Spec 007 v3 R-21 (ADR-12 §2.6, C-23): el clasificador puro, fila por fila y con las combinaciones raras. Complementa a T-15 (que lo ejercita
/// de punta a punta contra PostgreSQL): aquí se fija la tabla completa, incluido que un <c>timeout_error</c> informado por el servidor es
/// incierto con cualquier estado y que un estado desconocido nunca se trata como cierto.
/// </summary>
public class ClasificadorDeErrorDelProveedorTests
{
    private const string Ninguno = TiposDeErrorDelProveedor.Desconocido;

    [Theory]
    // (d) la solicitud es nuestra: 400 (incluye el tope de gasto o el saldo agotado), 404, 413
    [InlineData(400, Ninguno, DesenlaceDeError.BorradorPorSolicitudInvalida)]
    [InlineData(400, "invalid_request_error", DesenlaceDeError.BorradorPorSolicitudInvalida)]
    [InlineData(404, "not_found_error", DesenlaceDeError.BorradorPorSolicitudInvalida)]
    [InlineData(413, "request_too_large", DesenlaceDeError.BorradorPorSolicitudInvalida)]
    // (e) credenciales y permisos
    [InlineData(401, "authentication_error", DesenlaceDeError.CredencialInvalida)]
    [InlineData(401, Ninguno, DesenlaceDeError.CredencialInvalida)]
    [InlineData(403, "permission_error", DesenlaceDeError.CredencialInvalida)]
    // (e2) facturación
    [InlineData(402, "billing_error", DesenlaceDeError.FacturacionDelProveedor)]
    [InlineData(402, Ninguno, DesenlaceDeError.FacturacionDelProveedor)]
    // (c) ciertos y reintentables
    [InlineData(409, "conflict_error", DesenlaceDeError.Reintentable)]
    [InlineData(429, "rate_limit_error", DesenlaceDeError.Reintentable)]
    [InlineData(429, Ninguno, DesenlaceDeError.Reintentable)]
    [InlineData(529, "overloaded_error", DesenlaceDeError.Reintentable)]
    [InlineData(500, "api_error", DesenlaceDeError.Reintentable)]
    [InlineData(500, Ninguno, DesenlaceDeError.Reintentable)]
    [InlineData(500, "overloaded_error", DesenlaceDeError.Reintentable)]
    [InlineData(502, Ninguno, DesenlaceDeError.Reintentable)]
    [InlineData(503, Ninguno, DesenlaceDeError.Reintentable)]
    [InlineData(599, Ninguno, DesenlaceDeError.Reintentable)]
    // (f) inciertos: pudieron cobrarse, no se escribe nada
    [InlineData(504, "timeout_error", DesenlaceDeError.Incierto)]
    [InlineData(504, Ninguno, DesenlaceDeError.Incierto)]
    [InlineData(504, "api_error", DesenlaceDeError.Incierto)]
    [InlineData(500, "timeout_error", DesenlaceDeError.Incierto)]
    // un timeout_error informado por el servidor es incierto con CUALQUIER estado (R-21 f: «any status whose error.type is timeout_error»)
    [InlineData(400, "timeout_error", DesenlaceDeError.Incierto)]
    [InlineData(401, "timeout_error", DesenlaceDeError.Incierto)]
    [InlineData(402, "timeout_error", DesenlaceDeError.Incierto)]
    [InlineData(429, "timeout_error", DesenlaceDeError.Incierto)]
    [InlineData(529, "timeout_error", DesenlaceDeError.Incierto)]
    // cualquier estado que la tabla no liste, incluido un 4xx desconocido, es incierto («unknown is treated as possibly billed»)
    [InlineData(418, Ninguno, DesenlaceDeError.Incierto)]
    [InlineData(408, Ninguno, DesenlaceDeError.Incierto)]
    [InlineData(422, "invalid_request_error", DesenlaceDeError.Incierto)] // el tipo no rescata a un estado que la tabla no lista
    [InlineData(100, Ninguno, DesenlaceDeError.Incierto)]
    [InlineData(302, Ninguno, DesenlaceDeError.Incierto)]
    [InlineData(600, Ninguno, DesenlaceDeError.Incierto)]
    public void R21_cada_estado_y_tipo_se_clasifica_como_dice_la_tabla(int estado, string tipo, DesenlaceDeError esperado)
    {
        Assert.Equal(esperado, ClasificadorDeErrorDelProveedor.Clasificar(estado, tipo));
        Assert.Equal(esperado, ClasificadorDeErrorDelProveedor.Clasificar(new RespuestaConErrorException(estado, null, tipoDeError: tipo)));
    }

    [Fact]
    public void R21_el_mensaje_de_la_excepcion_no_influye_en_la_clasificacion()
    {
        var conMensaje = new RespuestaConErrorException(429, null, "spend limit reached, billing problem, timeout_error", "rate_limit_error");
        var sinMensaje = new RespuestaConErrorException(429, null, tipoDeError: "rate_limit_error");

        Assert.Equal(ClasificadorDeErrorDelProveedor.Clasificar(sinMensaje), ClasificadorDeErrorDelProveedor.Clasificar(conMensaje));
        Assert.Equal(DesenlaceDeError.Reintentable, ClasificadorDeErrorDelProveedor.Clasificar(conMensaje));
    }

    [Theory]
    [InlineData("rate_limit_error")]
    [InlineData("RATE_LIMIT_ERROR")]
    [InlineData("tipo_inventado")]
    [InlineData(null)]
    [InlineData("")]
    public void RespuestaConErrorException_solo_conserva_los_tipos_documentados(string? tipo)
    {
        var esperado = tipo == "rate_limit_error" ? tipo : "desconocido";

        Assert.Equal(esperado, new RespuestaConErrorException(429, null, tipoDeError: tipo).TipoDeError);
    }

    [Theory]
    [InlineData(429, "rate_limit_error", "http_429_rate_limit_error")]
    [InlineData(500, "timeout_error", "http_500_timeout_error")]
    [InlineData(418, Ninguno, "http_418")]
    [InlineData(402, "billing_error", "http_402_billing_error")]
    public void El_codigo_de_resultado_lleva_el_estado_y_solo_un_tipo_documentado_y_cabe_en_la_columna(int estado, string tipo, string esperado)
    {
        var codigo = ClasificadorDeErrorDelProveedor.CodigoDeResultado(new RespuestaConErrorException(estado, null, tipoDeError: tipo));

        Assert.Equal(esperado, codigo);
        Assert.InRange(codigo.Length, 1, 64); // ck_llamadas_ia_resultado
    }

    [Fact]
    public void El_codigo_de_resultado_mas_largo_posible_cabe_en_la_columna_de_64_caracteres()
    {
        var masLargo = TiposDeErrorDelProveedor.Documentados
            .Select(t => ClasificadorDeErrorDelProveedor.CodigoDeResultado(new RespuestaConErrorException(599, null, tipoDeError: t)))
            .Max(c => c.Length);

        Assert.True(masLargo <= 64, $"El código más largo mide {masLargo} caracteres.");
    }
}
