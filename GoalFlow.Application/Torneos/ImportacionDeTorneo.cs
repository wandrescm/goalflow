using GoalFlow.Application.Persistencia;
using GoalFlow.Application.Tenancy;
using GoalFlow.Domain.Torneos;
using Microsoft.Extensions.Logging;

namespace GoalFlow.Application.Torneos;

public sealed record ConteoDeColeccion(int Creados, int Actualizados, int SinCambios);

/// <summary>Conteos de R-19, por colección.</summary>
public sealed record ResultadoDeImportacion(
    ConteoDeColeccion Torneo,
    ConteoDeColeccion Fases,
    ConteoDeColeccion Equipos,
    ConteoDeColeccion Jugadores,
    ConteoDeColeccion Participaciones,
    ConteoDeColeccion Inscripciones,
    ConteoDeColeccion Partidos,
    ConteoDeColeccion Reglamento);

/// <summary>Lo guardado del torneo y del catálogo de la liga que el fixture menciona (Spec 005 R-18).</summary>
public sealed record EstadoGuardado(
    Torneo? Torneo,
    IReadOnlyList<Fase> Fases,
    IReadOnlyList<Equipo> Equipos,
    IReadOnlyList<Jugador> Jugadores,
    IReadOnlyList<EquipoTorneo> Participaciones,
    IReadOnlyList<InscripcionJugador> Inscripciones,
    IReadOnlyList<Partido> Partidos,
    ReglamentoTorneo? Reglamento);

/// <summary>Puerto de almacenamiento del caso de uso. Corre dentro de la transacción de <see cref="IUnidadDeTrabajo"/>.</summary>
public interface IAlmacenDeImportacion
{
    /// <summary>
    /// Carga por clave natural: el torneo con su estructura completa, y los equipos y jugadores de la liga cuyos códigos
    /// aparecen en el fixture o que ya participan en el torneo.
    /// </summary>
    Task<EstadoGuardado> CargarAsync(
        string codigoDeTorneo,
        IReadOnlyCollection<string> codigosDeEquipo,
        IReadOnlyCollection<string> codigosDeJugador,
        CancellationToken cancellationToken);

    void Agregar<T>(T entidad) where T : class;

    /// <exception cref="ConflictoConcurrenteException">Una restricción única de la importación la violó otra petición.</exception>
    Task GuardarAsync(CancellationToken cancellationToken);
}

public sealed record ConflictoDeImportacion(string Tipo, string Clave);

/// <summary>El fixture rompe R-12, R-13 o R-17, o el reglamento rompe Spec 009 R-06. Lleva todos los errores, cada uno con su ruta.</summary>
public class FixtureInvalidoException(IReadOnlyList<ErrorDeFixture> errores)
    : Exception("El fixture no es válido.")
{
    public IReadOnlyList<ErrorDeFixture> Errores { get; } = errores;
}

/// <summary>Lo guardado choca con el fixture (R-21, R-22). Lleva solo claves naturales de la liga del llamante (R-24).</summary>
public class ConflictoDeImportacionException(IReadOnlyList<ConflictoDeImportacion> conflictos)
    : Exception("La importación entra en conflicto con lo guardado.")
{
    public IReadOnlyList<ConflictoDeImportacion> Conflictos { get; } = conflictos;
}

/// <summary>Otra petición concurrente escribió primero (R-23). Sin nombres de restricción.</summary>
public class ConflictoConcurrenteException() : Exception("Otra importación concurrente escribió primero.");

/// <summary>
/// Importación idempotente de un torneo completo (Spec 005 R-14 a R-26): el fixture es el estado deseado; se crea lo
/// que falta, se actualiza lo que difiere y nunca se borra.
/// </summary>
public class ImportacionDeTorneo(
    IUnidadDeTrabajo unidadDeTrabajo,
    IAlmacenDeImportacion almacen,
    ITenantContext tenantContext,
    ILogger<ImportacionDeTorneo> logger)
{
    /// <param name="reglamento">El reglamento opcional del cuerpo (Spec 009 R-05); <c>null</c> si no vino.</param>
    /// <param name="guardadoPor">El <c>sub</c> del Admin autenticado, que se guarda en cada versión nueva del reglamento (R-07).</param>
    public async Task<ResultadoDeImportacion> EjecutarAsync(
        string codigoDeTorneo, FixtureDescripcion fixture, ReglamentoDescripcion? reglamento, Guid guardadoPor,
        CancellationToken cancellationToken)
    {
        // R-06: el reglamento se valida junto con el fixture y se devuelven todos los errores, cada uno con su ruta.
        var errores = new List<ErrorDeFixture>(ValidadorDeFixture.Validar(fixture));
        var reglamentoResuelto = reglamento is null ? null : ValidadorDeReglamento.Validar(reglamento);
        if (reglamentoResuelto is not null)
            errores.AddRange(reglamentoResuelto.Errores.Select(e => new ErrorDeFixture(e.Ruta, e.Codigo)));
        if (errores.Count > 0) throw new FixtureInvalidoException(errores);

        var tenant = tenantContext.Current;

        await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
        var guardado = await almacen.CargarAsync(
            codigoDeTorneo,
            [.. fixture.Equipos.Select(e => e.Codigo)],
            [.. fixture.Equipos.SelectMany(e => e.Jugadores).Select(j => j.Codigo)],
            cancellationToken);

        var conflictos = DetectarConflictos(fixture, guardado);

        // Spec 009 R-07 (e): omitir el reglamento nunca lo borra (Spec 005 R-21).
        if (reglamento is null && guardado.Reglamento is not null)
            conflictos.Add(new ConflictoDeImportacion("reglamento", codigoDeTorneo));
        if (conflictos.Count > 0) throw new ConflictoDeImportacionException(conflictos);

        var resultado = Aplicar(codigoDeTorneo, fixture, reglamentoResuelto, guardadoPor, guardado, tenant);
        await almacen.GuardarAsync(cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);

        // Nunca nombres de equipos ni de jugadores (R-25), ni el sub de quien guarda (Spec 009 R-10).
        var versionDelReglamento = (guardado.Reglamento?.Version ?? 0) + resultado.Reglamento.Creados + resultado.Reglamento.Actualizados;
        logger.LogInformation(
            "Importación de torneo: liga {Liga}, torneo {Torneo}, creados {Creados}, actualizados {Actualizados}, sin cambios {SinCambios}, "
            + "reglamento versión {VersionDelReglamento} estado {EstadoDelReglamento}",
            tenant.Value, codigoDeTorneo, Sumar(resultado, c => c.Creados), Sumar(resultado, c => c.Actualizados), Sumar(resultado, c => c.SinCambios),
            versionDelReglamento, reglamentoResuelto?.Estado?.ToString() ?? guardado.Reglamento?.Estado.ToString() ?? "ninguno");
        return resultado;
    }

    private static int Sumar(ResultadoDeImportacion r, Func<ConteoDeColeccion, int> campo) =>
        new[] { r.Torneo, r.Fases, r.Equipos, r.Jugadores, r.Participaciones, r.Inscripciones, r.Partidos, r.Reglamento }.Sum(campo);

    private static List<ConflictoDeImportacion> DetectarConflictos(FixtureDescripcion fixture, EstadoGuardado guardado)
    {
        var conflictos = new List<ConflictoDeImportacion>();
        var equiposPorId = guardado.Equipos.ToDictionary(e => e.Id);
        var jugadoresPorId = guardado.Jugadores.ToDictionary(j => j.Id);
        var participacionesPorId = guardado.Participaciones.ToDictionary(p => p.Id);

        var ordenesDelFixture = fixture.Fases.Select(f => f.Orden).ToHashSet();
        var codigosDeEquipo = fixture.Equipos.Select(e => e.Codigo).ToHashSet(StringComparer.Ordinal);
        var codigosDeJugador = fixture.Equipos.SelectMany(e => e.Jugadores).Select(j => j.Codigo).ToHashSet(StringComparer.Ordinal);
        var partidosDelFixture = fixture.Partidos.Select(p => (p.Fase, p.Local, p.Visitante)).ToHashSet();

        // R-21: lo guardado que falta en el payload. Omitir nunca borra.
        foreach (var fase in guardado.Fases.Where(f => !ordenesDelFixture.Contains(f.Orden)).OrderBy(f => f.Orden))
            conflictos.Add(new ConflictoDeImportacion("fase", fase.Orden.ToString()));

        foreach (var participacion in guardado.Participaciones)
        {
            var codigo = equiposPorId[participacion.EquipoId].Codigo;
            if (!codigosDeEquipo.Contains(codigo)) conflictos.Add(new ConflictoDeImportacion("equipo", codigo));
        }

        foreach (var inscripcion in guardado.Inscripciones)
        {
            var codigo = jugadoresPorId[inscripcion.JugadorId].Codigo;
            if (!codigosDeJugador.Contains(codigo)) conflictos.Add(new ConflictoDeImportacion("inscripcion", codigo));
        }

        var fasesPorId = guardado.Fases.ToDictionary(f => f.Id);
        foreach (var partido in guardado.Partidos)
        {
            var clave = (fasesPorId[partido.FaseId].Orden,
                equiposPorId[participacionesPorId[partido.LocalId].EquipoId].Codigo,
                equiposPorId[participacionesPorId[partido.VisitanteId].EquipoId].Codigo);
            if (!partidosDelFixture.Contains(clave))
                conflictos.Add(new ConflictoDeImportacion("partido", $"{clave.Orden}/{clave.Item2}/{clave.Item3}"));
        }

        // Spec 006 R-27 a R-29: lo que ya tiene resultado no se cancela, no se retira ni se reprograma fuera de lo permitido.
        DetectarConflictosConResultados(fixture, guardado, equiposPorId, participacionesPorId, conflictos);

        // R-22: un jugador no cambia de equipo dentro del torneo.
        var equipoPorJugadorGuardado = guardado.Inscripciones.ToDictionary(
            i => jugadoresPorId[i.JugadorId].Codigo,
            i => equiposPorId[participacionesPorId[i.EquipoTorneoId].EquipoId].Codigo,
            StringComparer.Ordinal);
        foreach (var equipo in fixture.Equipos)
        foreach (var jugador in equipo.Jugadores)
            if (equipoPorJugadorGuardado.TryGetValue(jugador.Codigo, out var guardadoEn) && guardadoEn != equipo.Codigo)
                conflictos.Add(new ConflictoDeImportacion("jugador_en_otro_equipo", jugador.Codigo));

        return conflictos;
    }

    private static bool TieneResultado(EstadoDePartido estado) =>
        estado is EstadoDePartido.Jugado or EstadoDePartido.Suspendido or EstadoDePartido.Adjudicado;

    /// <summary>
    /// R-27: cancelar un partido con resultado. R-28: cambiar la jornada de uno que no sea <c>Programado</c> ni
    /// <c>Cancelado</c>, o la hora de uno <c>Jugado</c> o <c>Adjudicado</c> (la de uno <c>Suspendido</c> sí puede cambiar, C-09).
    /// R-29: retirar un equipo que tiene un partido con resultado. Solo claves naturales de la liga del llamante (R-24).
    /// </summary>
    private static void DetectarConflictosConResultados(
        FixtureDescripcion fixture,
        EstadoGuardado guardado,
        Dictionary<Guid, Equipo> equiposPorId,
        Dictionary<Guid, EquipoTorneo> participacionesPorId,
        List<ConflictoDeImportacion> conflictos)
    {
        var codigoDe = (Guid participacionId) => equiposPorId[participacionesPorId[participacionId].EquipoId].Codigo;
        var retiradosEnElFixture = fixture.Equipos.Where(e => e.Retirado).Select(e => e.Codigo).ToHashSet(StringComparer.Ordinal);
        var fasesPorOrden = guardado.Fases.ToDictionary(f => f.Orden);
        var participacionPorCodigo = guardado.Participaciones.ToDictionary(p => equiposPorId[p.EquipoId].Codigo, StringComparer.Ordinal);
        var partidosGuardados = guardado.Partidos.ToDictionary(p => (p.FaseId, p.LocalId, p.VisitanteId));
        var agregados = new HashSet<(string, string)>();

        void Agregar(string tipo, string clave)
        {
            if (agregados.Add((tipo, clave))) conflictos.Add(new ConflictoDeImportacion(tipo, clave));
        }

        // R-29, en el orden de los equipos del fixture.
        foreach (var equipo in fixture.Equipos.Where(e => e.Retirado))
        {
            if (!participacionPorCodigo.TryGetValue(equipo.Codigo, out var participacion)) continue;
            if (guardado.Partidos.Any(p => TieneResultado(p.Estado) && (p.LocalId == participacion.Id || p.VisitanteId == participacion.Id)))
                Agregar("retiro_con_resultados", equipo.Codigo);
        }

        // R-27 y R-28, en el orden de los partidos del fixture.
        foreach (var descripcion in fixture.Partidos)
        {
            if (!fasesPorOrden.TryGetValue(descripcion.Fase, out var fase)) continue;
            if (!participacionPorCodigo.TryGetValue(descripcion.Local, out var local)) continue;
            if (!participacionPorCodigo.TryGetValue(descripcion.Visitante, out var visitante)) continue;
            if (!partidosGuardados.TryGetValue((fase.Id, local.Id, visitante.Id), out var partido)) continue;

            var clave = $"{descripcion.Fase}/{descripcion.Local}/{descripcion.Visitante}";
            DateTimeOffset? hora = null;
            if (descripcion.ProgramadoEn is { } texto && HoraProgramada.Interpretar(texto, out var interpretada) is null)
                hora = interpretada;

            // Si el partido es de un equipo que se retira, ya lo explica el conflicto de R-29: no se repite como R-27.
            var loExplicaElRetiro = retiradosEnElFixture.Contains(descripcion.Local) || retiradosEnElFixture.Contains(descripcion.Visitante);
            if (descripcion.Cancelado && TieneResultado(partido.Estado) && !loExplicaElRetiro) Agregar("partido_con_resultado", clave);

            var abierto = partido.Estado is EstadoDePartido.Programado or EstadoDePartido.Cancelado;
            if (descripcion.Jornada != partido.Jornada && !abierto) Agregar("partido_con_resultado", clave);
            if (hora != partido.ProgramadoEn && partido.Estado is EstadoDePartido.Jugado or EstadoDePartido.Adjudicado)
                Agregar("partido_con_resultado", clave);
        }
    }

    private ResultadoDeImportacion Aplicar(
        string codigoDeTorneo, FixtureDescripcion fixture, ResultadoDeValidacionDeReglamento? reglamento, Guid guardadoPor,
        EstadoGuardado guardado, Domain.Tenancy.TenantId tenant)
    {
        var torneoConteo = new Contador();
        var fasesConteo = new Contador();
        var equiposConteo = new Contador();
        var jugadoresConteo = new Contador();
        var participacionesConteo = new Contador();
        var inscripcionesConteo = new Contador();
        var partidosConteo = new Contador();
        var reglamentoConteo = new Contador();

        // Torneo.
        var torneo = guardado.Torneo;
        if (torneo is null)
        {
            torneo = new Torneo(Guid.CreateVersion7(), tenant, codigoDeTorneo, fixture.Nombre);
            almacen.Agregar(torneo);
            torneoConteo.Creado();
        }
        else torneoConteo.Resultado(torneo.Renombrar(fixture.Nombre));

        // Fases, por su orden.
        var fasesPorOrden = guardado.Fases.ToDictionary(f => f.Orden);
        foreach (var descripcion in fixture.Fases)
        {
            var tipo = Enum.Parse<TipoDeFase>(descripcion.Tipo);
            if (fasesPorOrden.TryGetValue(descripcion.Orden, out var existente))
            {
                fasesConteo.Resultado(existente.Configurar(tipo, descripcion.Vueltas));
                continue;
            }

            var nueva = new Fase(Guid.CreateVersion7(), tenant, torneo.Id, descripcion.Orden, tipo, descripcion.Vueltas);
            almacen.Agregar(nueva);
            fasesPorOrden[nueva.Orden] = nueva;
            fasesConteo.Creado();
        }

        // Catálogo de la liga: equipos y jugadores por código.
        var equiposPorCodigo = guardado.Equipos.ToDictionary(e => e.Codigo, StringComparer.Ordinal);
        var jugadoresPorCodigo = guardado.Jugadores.ToDictionary(j => j.Codigo, StringComparer.Ordinal);
        foreach (var descripcion in fixture.Equipos)
        {
            if (equiposPorCodigo.TryGetValue(descripcion.Codigo, out var equipo))
                equiposConteo.Resultado(equipo.Renombrar(descripcion.Nombre));
            else
            {
                equipo = new Equipo(Guid.CreateVersion7(), tenant, descripcion.Codigo, descripcion.Nombre);
                almacen.Agregar(equipo);
                equiposPorCodigo[equipo.Codigo] = equipo;
                equiposConteo.Creado();
            }

            foreach (var j in descripcion.Jugadores)
            {
                if (jugadoresPorCodigo.TryGetValue(j.Codigo, out var jugador))
                    jugadoresConteo.Resultado(jugador.Renombrar(j.Nombre));
                else
                {
                    jugador = new Jugador(Guid.CreateVersion7(), tenant, j.Codigo, j.Nombre);
                    almacen.Agregar(jugador);
                    jugadoresPorCodigo[jugador.Codigo] = jugador;
                    jugadoresConteo.Creado();
                }
            }
        }

        // Participaciones (torneo, equipo) con su estado, e inscripciones (torneo, jugador).
        var participacionesPorEquipo = guardado.Participaciones.ToDictionary(p => p.EquipoId);
        var inscritos = guardado.Inscripciones.Select(i => i.JugadorId).ToHashSet();
        var participacionPorCodigo = new Dictionary<string, EquipoTorneo>(StringComparer.Ordinal);
        foreach (var descripcion in fixture.Equipos)
        {
            var equipo = equiposPorCodigo[descripcion.Codigo];
            if (participacionesPorEquipo.TryGetValue(equipo.Id, out var participacion))
                participacionesConteo.Resultado(descripcion.Retirado ? participacion.Retirar() : participacion.Reinscribir());
            else
            {
                participacion = new EquipoTorneo(Guid.CreateVersion7(), tenant, torneo.Id, equipo.Id);
                if (descripcion.Retirado) participacion.Retirar();
                almacen.Agregar(participacion);
                participacionesConteo.Creado();
            }

            participacionPorCodigo[descripcion.Codigo] = participacion;

            foreach (var j in descripcion.Jugadores)
            {
                var jugador = jugadoresPorCodigo[j.Codigo];
                if (inscritos.Contains(jugador.Id))
                {
                    inscripcionesConteo.Resultado(false);
                    continue;
                }

                almacen.Agregar(new InscripcionJugador(Guid.CreateVersion7(), tenant, torneo.Id, participacion.Id, jugador.Id));
                inscripcionesConteo.Creado();
            }
        }

        // Partidos, por (fase, local, visitante).
        var partidosGuardados = guardado.Partidos.ToDictionary(p => (p.FaseId, p.LocalId, p.VisitanteId));
        foreach (var descripcion in fixture.Partidos)
        {
            var fase = fasesPorOrden[descripcion.Fase];
            var local = participacionPorCodigo[descripcion.Local];
            var visitante = participacionPorCodigo[descripcion.Visitante];
            DateTimeOffset? hora = null;
            if (descripcion.ProgramadoEn is { } texto && HoraProgramada.Interpretar(texto, out var interpretada) is null)
                hora = interpretada;

            if (partidosGuardados.TryGetValue((fase.Id, local.Id, visitante.Id), out var partido))
            {
                var cambioHora = partido.Reprogramar(descripcion.Jornada, hora);

                // R-26: "no cancelado" coincide con cualquier estado guardado salvo Cancelado; solo un Cancelado vuelve a Programado.
                var cambioEstado = descripcion.Cancelado ? partido.Cancelar() : partido.Reactivar();
                partidosConteo.Resultado(cambioHora | cambioEstado);
            }
            else
            {
                partido = new Partido(Guid.CreateVersion7(), tenant, torneo.Id, fase.Id, descripcion.Jornada, local.Id, visitante.Id, hora);
                if (descripcion.Cancelado) partido.Cancelar();
                almacen.Agregar(partido);
                partidosConteo.Creado();
            }
        }

        // Reglamento (Spec 009 R-07): la versión más alta es la vigente; una versión nueva solo si el estado o el contenido
        // difieren. Sin reglamento en el cuerpo no se escribe ni se cuenta nada: el caso (e) ya se rechazó como conflicto.
        if (reglamento is { Estado: { } estado, Contenido: { } contenido })
        {
            if (guardado.Reglamento is not { } vigente)
            {
                almacen.Agregar(new ReglamentoTorneo(Guid.CreateVersion7(), tenant, torneo.Id, 1, estado, contenido, guardadoPor));
                reglamentoConteo.Creado();
            }
            else if (vigente.Equivale(estado, contenido)) reglamentoConteo.Resultado(false);
            else
            {
                almacen.Agregar(new ReglamentoTorneo(Guid.CreateVersion7(), tenant, torneo.Id, vigente.Version + 1, estado, contenido, guardadoPor));
                reglamentoConteo.Resultado(true);
            }
        }

        return new ResultadoDeImportacion(
            torneoConteo.Valor, fasesConteo.Valor, equiposConteo.Valor, jugadoresConteo.Valor,
            participacionesConteo.Valor, inscripcionesConteo.Valor, partidosConteo.Valor, reglamentoConteo.Valor);
    }

    private sealed class Contador
    {
        private int _creados;
        private int _actualizados;
        private int _sinCambios;

        public ConteoDeColeccion Valor => new(_creados, _actualizados, _sinCambios);

        public void Creado() => _creados++;

        public void Resultado(bool cambio)
        {
            if (cambio) _actualizados++;
            else _sinCambios++;
        }
    }
}
