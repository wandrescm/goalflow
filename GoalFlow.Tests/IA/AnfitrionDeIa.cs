using System.Text.Json;
using GoalFlow.Application.IA;
using GoalFlow.Application.Mensajeria;
using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Application.Torneos.Contracts;
using GoalFlow.Domain.Tenancy;
using GoalFlow.Infrastructure.IA;
using GoalFlow.Infrastructure.Mensajeria;
using GoalFlow.Infrastructure.Persistencia;
using GoalFlow.Tests.Isolation;
using GoalFlow.Tests.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GoalFlow.Tests.IA;

/// <summary>Colección propia, sin paralelismo y con su propia base: las pruebas de IA crean ligas y contadores por mes.</summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public class ColeccionDeIa : ICollectionFixture<BaseDeAislamiento>
{
    public const string Nombre = "IA";
}

/// <summary>Un reloj que solo cambia cuando la prueba lo fija (Spec 007 T-30).</summary>
public sealed class RelojFijo(DateTimeOffset inicio) : TimeProvider
{
    private readonly object _candado = new();
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_candado) return _ahora;
    }

    public void Fijar(DateTimeOffset ahora)
    {
        lock (_candado) _ahora = ahora;
    }
}

/// <summary>El estado compartido del generador falso de un anfitrión: qué responde, qué recibió y qué observó (Spec 007 §4).</summary>
public sealed class ControlDeGenerador
{
    /// <summary>
    /// El <b>centinela</b> del texto válido (Spec 008 C-13): cuando una respuesta guionizada trae exactamente este texto, el
    /// generador falso devuelve en su lugar <see cref="TextoValidoPara"/> de la solicitud recibida, que el verificador v1 aprueba.
    /// Su literal no se usa como salida real: "2-1" y los dos goleadores no coinciden con la planilla por defecto. Cualquier otro
    /// texto guionizado se devuelve tal cual.
    /// </summary>
    public const string TextoValido =
        "Equipo Uno venció 2-1 a Equipo Dos con goles de Jugador Uno y Jugador Dos en un partido muy disputado de principio a fin.";

    private readonly List<SolicitudDeGeneracion> _solicitudesDeGeneracion = [];

    /// <summary>Cada <see cref="SolicitudDeGeneracion"/> que llegó a <c>GenerarAsync</c>, en orden (Spec 008 C-13).</summary>
    public IReadOnlyList<SolicitudDeGeneracion> SolicitudesDeGeneracion
    {
        get
        {
            lock (_solicitudesDeGeneracion) return [.. _solicitudesDeGeneracion];
        }
    }

    internal void AnotarSolicitudDeGeneracion(SolicitudDeGeneracion solicitud)
    {
        lock (_solicitudesDeGeneracion) _solicitudesDeGeneracion.Add(solicitud);
    }

    /// <summary>
    /// Un texto que el verificador v1 aprueba para esa entrada canónica: solo usa su torneo, fase, jornada, equipos y marcador.
    /// </summary>
    /// <exception cref="InvalidOperationException">La entrada no trae marcador, o el texto mide menos de 80 caracteres.</exception>
    public static string TextoValidoPara(string entradaCanonica)
    {
        using var documento = JsonDocument.Parse(entradaCanonica);
        var raiz = documento.RootElement;
        if (!raiz.TryGetProperty("marcador", out var marcador) || marcador.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("La entrada canónica no trae marcador: no hay texto válido para ella.");

        var texto = $"En la jornada {raiz.GetProperty("jornada").GetInt32()} de la fase {raiz.GetProperty("fase").GetInt32()} "
            + $"del {raiz.GetProperty("torneo").GetString()}, {raiz.GetProperty("local").GetString()} y {raiz.GetProperty("visitante").GetString()} "
            + $"jugaron un partido que terminó {marcador.GetProperty("local").GetInt32()} a {marcador.GetProperty("visitante").GetInt32()}.";
        if (texto.Length < 80) throw new InvalidOperationException("El texto válido mide menos de 80 caracteres.");
        return texto;
    }

    private int _conteos;
    private int _generaciones;

    public int Conteos => Volatile.Read(ref _conteos);

    public int Generaciones => Volatile.Read(ref _generaciones);

    public List<SolicitudDeGeneracion> Solicitudes { get; } = [];

    /// <summary>Los tokens que devuelve el conteo; por defecto 2000, que reserva 525 µ$ con los valores iniciales.</summary>
    public Func<SolicitudDeGeneracion, int> TokensContados { get; set; } = _ => 2_000;

    /// <summary>Lo que hace la generación número n (1 en la primera). Por defecto, una salida válida con 2000 y 400 tokens.</summary>
    public Func<int, Task<RespuestaGenerada>> AlGenerar { get; set; } =
        _ => Task.FromResult(new RespuestaGenerada(Json(TextoValido), "end_turn", 2_000, 400));

    /// <summary>Lo que hace el conteo n; null = responde TokensContados.</summary>
    public Func<int, Task>? AlContar { get; set; }

    /// <summary>Se invoca justo antes de contar y de generar, con la base del scope del generador: permite observar la transacción.</summary>
    public Action<GoalFlowDbContext>? Observador { get; set; }

    public bool HuboTransaccionAbierta { get; set; }

    public static string Json(string texto) => JsonSerializer.Serialize(new { texto });

    internal int AnotarConteo(SolicitudDeGeneracion s)
    {
        lock (Solicitudes) Solicitudes.Add(s);
        return Interlocked.Increment(ref _conteos);
    }

    internal int AnotarGeneracion() => Interlocked.Increment(ref _generaciones);
}

public sealed class GeneradorFalso(ControlDeGenerador control, GoalFlowDbContext contexto) : IGeneradorDeTexto
{
    public async Task<int> ContarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken)
    {
        Observar();
        var n = control.AnotarConteo(solicitud);
        if (control.AlContar is { } alContar) await alContar(n);
        return control.TokensContados(solicitud);
    }

    public async Task<RespuestaGenerada> GenerarAsync(SolicitudDeGeneracion solicitud, CancellationToken cancellationToken)
    {
        Observar();
        control.AnotarSolicitudDeGeneracion(solicitud);
        var respuesta = await control.AlGenerar(control.AnotarGeneracion());

        // C-13: el centinela se reemplaza por un texto que el verificador aprueba para esta solicitud; los demás textos, tal cual.
        return respuesta.Texto == ControlDeGenerador.Json(ControlDeGenerador.TextoValido)
            ? respuesta with { Texto = ControlDeGenerador.Json(ControlDeGenerador.TextoValidoPara(solicitud.EntradaCanonica)) }
            : respuesta;
    }

    private void Observar()
    {
        if (contexto.Database.CurrentTransaction is not null) control.HuboTransaccionAbierta = true;
        control.Observador?.Invoke(contexto);
    }
}

/// <summary>Una planilla para sembrar: valores simples, nombres de <see cref="PartidoSembrado"/>.</summary>
public sealed record PlanillaSembrada(
    string Estado,
    (int Local, int Visitante)? Marcador = null,
    (int Local, int Visitante)? Oficial = null,
    string? Motivo = null,
    string? MotivoDeRectificacion = null,
    bool? TarjetasInformadas = true,
    (string Jugador, int Cantidad)[]? Goles = null,
    (string Jugador, int Cantidad)[]? Autogoles = null,
    (int Local, int Visitante)? SinAutor = null,
    (string Jugador, string Color, int Cantidad)[]? Tarjetas = null)
{
    /// <summary>Un 1-0 con un goleador local; el caso narrativo más simple.</summary>
    public static PlanillaSembrada ConGoleador(string jugador = "Jugador Uno", bool? informadas = true) =>
        new("Jugado", Marcador: (1, 0), TarjetasInformadas: informadas, Goles: [(jugador, 1)]);
}

/// <summary>
/// Un torneo con un partido entre "Equipo Uno" (local: Jugador Uno y Dos) y "Equipo Dos" (visitante: Jugador Tres y Cuatro),
/// sembrado como superusuario en la liga indicada. Cada guardado agrega la versión siguiente y deja el partido en ese estado,
/// como el caso de uso real (Spec 006 R-07). Datos inventados.
/// </summary>
public sealed class PartidoSembrado
{
    private readonly BaseDeAislamiento _db;
    private readonly Guid _localId = Guid.NewGuid();
    private readonly Guid _visitanteId = Guid.NewGuid();
    private readonly Dictionary<string, (Guid Inscripcion, bool EsLocal)> _jugadores = [];
    private int _version;

    private PartidoSembrado(BaseDeAislamiento db, TenantId tenant, string sufijo)
    {
        _db = db;
        Tenant = tenant;
        Sufijo = sufijo;
    }

    public TenantId Tenant { get; }

    public string Sufijo { get; }

    public Guid TorneoId { get; } = Guid.NewGuid();

    public Guid Id { get; } = Guid.NewGuid();

    public string NombreLocal => "Equipo Uno" + Sufijo;

    public string NombreVisitante => "Equipo Dos" + Sufijo;

    public int Version => _version;

    public static string NombreDeJugador(string baseDelNombre, string sufijo) => baseDelNombre + sufijo;

    public static async Task<PartidoSembrado> CrearAsync(BaseDeAislamiento db, TenantId tenant, string sufijo = "")
    {
        var p = new PartidoSembrado(db, tenant, sufijo);
        var t = tenant.Value;
        var s = Guid.NewGuid().ToString("N")[..8];
        var (fase, equipoLocal, equipoVisitante) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var nombres = new (string Nombre, bool Local)[]
        {
            ("Jugador Uno", true), ("Jugador Dos", true), ("Jugador Tres", false), ("Jugador Cuatro", false),
        };
        var sql = new System.Text.StringBuilder($"""
            insert into goalflow.torneos (id, tenant_id, codigo, nombre) values ('{p.TorneoId}', '{t}', 'IA-{s}', 'Torneo Uno{sufijo}');
            insert into goalflow.fases (id, tenant_id, torneo_id, orden, tipo, vueltas) values ('{fase}', '{t}', '{p.TorneoId}', 1, 'TodosContraTodos', 1);
            insert into goalflow.equipos (id, tenant_id, codigo, nombre) values
              ('{equipoLocal}', '{t}', 'IA-EL-{s}', '{p.NombreLocal}'), ('{equipoVisitante}', '{t}', 'IA-EV-{s}', '{p.NombreVisitante}');
            insert into goalflow.equipos_torneo (id, tenant_id, torneo_id, equipo_id, estado) values
              ('{p._localId}', '{t}', '{p.TorneoId}', '{equipoLocal}', 'Inscrito'), ('{p._visitanteId}', '{t}', '{p.TorneoId}', '{equipoVisitante}', 'Inscrito');
            insert into goalflow.partidos (id, tenant_id, torneo_id, fase_id, jornada, local_id, visitante_id, estado)
              values ('{p.Id}', '{t}', '{p.TorneoId}', '{fase}', 3, '{p._localId}', '{p._visitanteId}', 'Programado');
            """);
        var n = 0;
        foreach (var (nombre, esLocal) in nombres)
        {
            var (jugador, inscripcion) = (Guid.NewGuid(), Guid.NewGuid());
            p._jugadores[nombre] = (inscripcion, esLocal);
            sql.AppendLine($"""
                insert into goalflow.jugadores (id, tenant_id, codigo, nombre) values ('{jugador}', '{t}', 'IA-J{++n}-{s}', '{nombre}{sufijo}');
                insert into goalflow.inscripciones_jugador (id, tenant_id, torneo_id, equipo_torneo_id, jugador_id)
                  values ('{inscripcion}', '{t}', '{p.TorneoId}', '{(esLocal ? p._localId : p._visitanteId)}', '{jugador}');
                """);
        }

        await db.EjecutarComoSuperusuario(sql.ToString());
        return p;
    }

    /// <summary>Guarda la versión siguiente de la planilla y deja el partido en su estado. Devuelve el número de versión.</summary>
    public async Task<int> GuardarAsync(PlanillaSembrada planilla)
    {
        var (t, planillaId, version) = (Tenant.Value, Guid.NewGuid(), ++_version);
        static string N<T>(T? v) where T : struct => v is null ? "null" : v.ToString()!;
        static string S(string? v) => v is null ? "null" : $"'{v}'";
        var sql = new System.Text.StringBuilder($"""
            insert into goalflow.planillas (id, tenant_id, torneo_id, partido_id, version, estado, marcador_local, marcador_visitante,
                oficial_local, oficial_visitante, motivo, motivo_rectificacion, tarjetas_informadas, guardado_por)
            values ('{planillaId}', '{t}', '{TorneoId}', '{Id}', {version}, '{planilla.Estado}',
                {N(planilla.Marcador?.Local)}, {N(planilla.Marcador?.Visitante)}, {N(planilla.Oficial?.Local)}, {N(planilla.Oficial?.Visitante)},
                {S(planilla.Motivo)}, {S(planilla.MotivoDeRectificacion)}, {(planilla.TarjetasInformadas is { } b ? (b ? "true" : "false") : "null")},
                '{BaseDeAislamiento.UsuarioU1}');

            """);

        foreach (var (jugador, cantidad) in planilla.Goles ?? [])
        {
            var (inscripcion, esLocal) = _jugadores[jugador];
            sql.AppendLine($"""
                insert into goalflow.goles (id, tenant_id, torneo_id, planilla_id, tipo, equipo_torneo_id, inscripcion_id, cantidad)
                values ('{Guid.NewGuid()}', '{t}', '{TorneoId}', '{planillaId}', 'Jugador', '{(esLocal ? _localId : _visitanteId)}', '{inscripcion}', {cantidad});
                """);
        }

        foreach (var (jugador, cantidad) in planilla.Autogoles ?? [])
        {
            var (inscripcion, esLocal) = _jugadores[jugador];
            sql.AppendLine($"""
                insert into goalflow.goles (id, tenant_id, torneo_id, planilla_id, tipo, equipo_torneo_id, inscripcion_id, cantidad)
                values ('{Guid.NewGuid()}', '{t}', '{TorneoId}', '{planillaId}', 'Autogol', '{(esLocal ? _visitanteId : _localId)}', '{inscripcion}', {cantidad});
                """);
        }

        if (planilla.SinAutor is { } sinAutor)
        {
            foreach (var (cantidad, equipo) in new[] { (sinAutor.Local, _localId), (sinAutor.Visitante, _visitanteId) })
                if (cantidad > 0)
                    sql.AppendLine($"""
                        insert into goalflow.goles (id, tenant_id, torneo_id, planilla_id, tipo, equipo_torneo_id, inscripcion_id, cantidad)
                        values ('{Guid.NewGuid()}', '{t}', '{TorneoId}', '{planillaId}', 'SinAutor', '{equipo}', null, {cantidad});
                        """);
        }

        foreach (var (jugador, color, cantidad) in planilla.Tarjetas ?? [])
            sql.AppendLine($"""
                insert into goalflow.tarjetas (id, tenant_id, torneo_id, planilla_id, inscripcion_id, color, cantidad)
                values ('{Guid.NewGuid()}', '{t}', '{TorneoId}', '{planillaId}', '{_jugadores[jugador].Inscripcion}', '{color}', {cantidad});
                """);

        sql.AppendLine($"update goalflow.partidos set version = {version}, estado = '{planilla.Estado}' where id = '{Id}';");
        await _db.EjecutarComoSuperusuario(sql.ToString());
        return version;
    }
}

/// <summary>
/// Un host real (Production, con validación de DI al construir) con la persistencia, la mensajería y el módulo IA de producción,
/// conectado como goalflow_app, con un generador falso y un reloj fijo (Spec 007 §4). El ciclo en segundo plano está apagado
/// salvo que la prueba lo pida: cada prueba llama al consumidor a mano, con el orden que quiere.
/// </summary>
public sealed class AnfitrionDeIa : IAsyncDisposable
{
    private static int _siguienteAnio = 2100;
    private bool _iniciado;

    private AnfitrionDeIa(IHost host, ControlDeGenerador control, RelojFijo reloj, BaseDeAislamiento db)
    {
        Host = host;
        Control = control;
        Reloj = reloj;
        Db = db;
    }

    public IHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public ControlDeGenerador Control { get; }

    public RelojFijo Reloj { get; }

    public BaseDeAislamiento Db { get; }

    /// <summary>Un año libre, único en el proceso: así cada prueba tiene sus propios contadores mensuales (R-03).</summary>
    public static int AnioUnico() => Interlocked.Increment(ref _siguienteAnio);

    public static Dictionary<string, string?> ValoresDePrueba(BaseDeAislamiento db) => new()
    {
        ["ConnectionStrings:GoalFlow"] = db.CadenaApp,
        ["Mensajeria:CicloHabilitado"] = "false",
        ["Mensajeria:PeriodoDelTemporizador"] = "00:00:01",
        ["Mensajeria:DuracionDelLease"] = "00:00:10",
        ["Mensajeria:TimeoutDelConsumidor"] = "00:00:05",
        ["Mensajeria:BackoffInicial"] = "00:00:01",
        ["IA:Habilitada"] = "true",
        ["IA:ClaveApi"] = "clave-falsa-de-prueba",
        ["IA:TimeoutDeLlamada"] = "00:00:03",
    };

    public static AnfitrionDeIa Construir(
        BaseDeAislamiento db,
        Action<Dictionary<string, string?>>? ajustar = null,
        Action<IServiceCollection>? servicios = null,
        CapturaDeRegistros? registros = null,
        bool conGenerador = true)
    {
        var configuracion = ValoresDePrueba(db);
        ajustar?.Invoke(configuracion);
        var control = new ControlDeGenerador();
        var reloj = new RelojFijo(new DateTimeOffset(AnioUnico(), 6, 15, 12, 0, 0, TimeSpan.Zero));

        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseEnvironment(Environments.Production)
            .UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            })
            .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(configuracion))
            .ConfigureLogging(l =>
            {
                l.ClearProviders();
                if (registros is null) return;
                l.SetMinimumLevel(LogLevel.Trace);
                l.AddProvider(registros);
            })
            .ConfigureServices((contexto, s) =>
            {
                s.AddGoalFlowPersistencia();
                s.AddGoalFlowMensajeria();
                s.AddGoalFlowIA(contexto.Configuration);
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(reloj);
                if (conGenerador)
                {
                    s.RemoveAll<IGeneradorDeTexto>();
                    s.AddSingleton(control);
                    s.AddScoped<IGeneradorDeTexto, GeneradorFalso>();
                }

                servicios?.Invoke(s);
            })
            .Build();
        return new AnfitrionDeIa(host, control, reloj, db);
    }

    public async Task IniciarAsync()
    {
        await Host.StartAsync();
        _iniciado = true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_iniciado) await Host.StopAsync();
        }
        finally
        {
            Host.Dispose();
        }
    }

    public static string Mes(DateTimeOffset instante) => instante.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    public string MesActual => Mes(Reloj.GetUtcNow());

    // ---- Ejecución del consumidor ----

    public AsyncServiceScope ScopeDe(TenantId tenant)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Establecer(tenant);
        return scope;
    }

    /// <summary>Procesa la entrega de (partido, versión) como lo hace el ciclo: un scope con el tenant y el consumidor scoped.</summary>
    public async Task ProcesarAsync(
        TenantId tenant, Guid partidoId, int version, int intento = 1, CancellationToken cancellationToken = default)
    {
        await using var scope = ScopeDe(tenant);
        await scope.ServiceProvider.GetRequiredService<ConsumidorDeResumenDePartido>().ProcesarAsync(
            new EntregaDeEvento<PartidoGuardado>(new PartidoGuardado(tenant, partidoId, version), intento), cancellationToken);
    }

    public Task ProcesarAsync(PartidoSembrado p, int version, int intento = 1) => ProcesarAsync(p.Tenant, p.Id, version, intento);

    // ---- Medición como superusuario ----

    public async Task<List<object?[]>> Filas(string sql)
    {
        await using var conexion = new NpgsqlConnection(Db.CadenaSuperusuario);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        var filas = new List<object?[]>();
        while (await lector.ReadAsync())
        {
            var fila = new object[lector.FieldCount];
            lector.GetValues(fila);
            filas.Add([.. fila.Select(v => v is DBNull ? null : v)]);
        }

        return filas;
    }

    public async Task<long> Contar(string sql) => Convert.ToInt64((await Filas(sql))[0][0]);

    public Task<long> Resumenes(PartidoSembrado p) => Contar($"select count(*) from goalflow.resumenes_partido where partido_id = '{p.Id}'");

    public Task<long> Llamadas(PartidoSembrado p) => Contar($"select count(*) from goalflow.llamadas_ia where partido_id = '{p.Id}'");

    public Task<long> Contadores(TenantId tenant, string mes) =>
        Contar($"select count(*) from goalflow.presupuestos_ia where tenant_id = '{tenant.Value}' and mes = '{mes}'");

    public async Task<(long Reservado, long Consumido)> Contador(TenantId tenant, string mes)
    {
        var fila = (await Filas(
            $"select reservado, consumido from goalflow.presupuestos_ia where tenant_id = '{tenant.Value}' and mes = '{mes}'")).Single();
        return ((long)fila[0]!, (long)fila[1]!);
    }

    /// <summary>(origen, estado, motivo, texto, versión de origen, huella) de los resúmenes del partido, por versión.</summary>
    public async Task<List<(string Origen, string Estado, string? Motivo, string Texto, int Version, string Huella)>> ResumenesDe(PartidoSembrado p) =>
        [.. (await Filas($"""
            select origen, estado, motivo_borrador, texto, version_origen, huella
            from goalflow.resumenes_partido where partido_id = '{p.Id}' order by version_origen, creado_en
            """)).Select(f => ((string)f[0]!, (string)f[1]!, (string?)f[2], (string)f[3]!, (int)f[4]!, (string)f[5]!))];

    /// <summary>(número, estado, reserva, tokens de entrada, tokens de salida, costo, resultado, mes) de las llamadas del partido.</summary>
    public async Task<List<(int Numero, string Estado, long Reserva, int? Entrada, int? Salida, long? Costo, string? Resultado, string Mes)>> LlamadasDe(
        PartidoSembrado p) =>
        [.. (await Filas($"""
            select numero, estado, reserva, tokens_entrada, tokens_salida, costo, resultado, mes
            from goalflow.llamadas_ia where partido_id = '{p.Id}' order by version, numero
            """)).Select(f => ((int)f[0]!, (string)f[1]!, (long)f[2]!, (int?)f[3], (int?)f[4], (long?)f[5], (string?)f[6], (string)f[7]!))];
}
