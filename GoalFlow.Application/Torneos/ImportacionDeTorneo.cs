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
    ConteoDeColeccion Partidos);

/// <summary>Lo guardado del torneo y del catálogo de la liga que el fixture menciona (Spec 005 R-18).</summary>
public sealed record EstadoGuardado(
    Torneo? Torneo,
    IReadOnlyList<Fase> Fases,
    IReadOnlyList<Equipo> Equipos,
    IReadOnlyList<Jugador> Jugadores,
    IReadOnlyList<EquipoTorneo> Participaciones,
    IReadOnlyList<InscripcionJugador> Inscripciones,
    IReadOnlyList<Partido> Partidos);

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

/// <summary>El fixture rompe R-12, R-13 o R-17. Lleva todos los errores.</summary>
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
    public async Task<ResultadoDeImportacion> EjecutarAsync(
        string codigoDeTorneo, FixtureDescripcion fixture, CancellationToken cancellationToken)
    {
        var errores = ValidadorDeFixture.Validar(fixture);
        if (errores.Count > 0) throw new FixtureInvalidoException(errores);

        var tenant = tenantContext.Current;

        await using var transaccion = await unidadDeTrabajo.IniciarAsync(cancellationToken);
        var guardado = await almacen.CargarAsync(
            codigoDeTorneo,
            [.. fixture.Equipos.Select(e => e.Codigo)],
            [.. fixture.Equipos.SelectMany(e => e.Jugadores).Select(j => j.Codigo)],
            cancellationToken);

        var conflictos = DetectarConflictos(fixture, guardado);
        if (conflictos.Count > 0) throw new ConflictoDeImportacionException(conflictos);

        var resultado = Aplicar(codigoDeTorneo, fixture, guardado, tenant);
        await almacen.GuardarAsync(cancellationToken);
        await transaccion.ConfirmarAsync(cancellationToken);

        // Nunca nombres de equipos ni de jugadores (R-25).
        logger.LogInformation(
            "Importación de torneo: liga {Liga}, torneo {Torneo}, creados {Creados}, actualizados {Actualizados}, sin cambios {SinCambios}",
            tenant.Value, codigoDeTorneo, Sumar(resultado, c => c.Creados), Sumar(resultado, c => c.Actualizados), Sumar(resultado, c => c.SinCambios));
        return resultado;
    }

    private static int Sumar(ResultadoDeImportacion r, Func<ConteoDeColeccion, int> campo) =>
        new[] { r.Torneo, r.Fases, r.Equipos, r.Jugadores, r.Participaciones, r.Inscripciones, r.Partidos }.Sum(campo);

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

    private ResultadoDeImportacion Aplicar(
        string codigoDeTorneo, FixtureDescripcion fixture, EstadoGuardado guardado, Domain.Tenancy.TenantId tenant)
    {
        var torneoConteo = new Contador();
        var fasesConteo = new Contador();
        var equiposConteo = new Contador();
        var jugadoresConteo = new Contador();
        var participacionesConteo = new Contador();
        var inscripcionesConteo = new Contador();
        var partidosConteo = new Contador();

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

        return new ResultadoDeImportacion(
            torneoConteo.Valor, fasesConteo.Valor, equiposConteo.Valor, jugadoresConteo.Valor,
            participacionesConteo.Valor, inscripcionesConteo.Valor, partidosConteo.Valor);
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
