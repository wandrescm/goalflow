using Npgsql;

namespace GoalFlow.Tests.Isolation;

/// <summary>
/// Filas de torneo de las ligas A y B, creadas una sola vez como superusuario (preparación; las pruebas se
/// conectan como goalflow_app). Datos inventados (Spec 005 §4).
/// </summary>
public sealed record GrafoDeTorneos(
    Guid TorneoA, Guid FaseA, Guid ParticipacionA1, Guid ParticipacionA2, Guid JugadorA,
    Guid TorneoA2, Guid FaseA2, Guid ParticipacionA2DelOtroTorneo,
    Guid TorneoB, Guid FaseB, Guid ParticipacionB1, Guid ParticipacionB2, Guid JugadorB,
    Guid EquipoB, Guid EquipoA)
{
    private static readonly SemaphoreSlim Candado = new(1, 1);
    private static readonly Dictionary<string, GrafoDeTorneos> PorBase = [];

    public static async Task<GrafoDeTorneos> ObtenerAsync(BaseDeAislamiento db)
    {
        await Candado.WaitAsync();
        try
        {
            if (PorBase.TryGetValue(db.CadenaSuperusuario, out var existente)) return existente;

            var g = new GrafoDeTorneos(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid());
            var (a, b) = (BaseDeAislamiento.LigaA.Value, BaseDeAislamiento.LigaB.Value);
            var (equipoA2, equipoB2) = (Guid.NewGuid(), Guid.NewGuid());
            var equipoA1 = g.EquipoA;
            var sufijo = Guid.NewGuid().ToString("N")[..8];

            await db.EjecutarComoSuperusuario($"""
                insert into goalflow.torneos (id, tenant_id, codigo, nombre) values
                  ('{g.TorneoA}', '{a}', 'RAW-A1-{sufijo}', 'Torneo Uno A'),
                  ('{g.TorneoA2}', '{a}', 'RAW-A2-{sufijo}', 'Torneo Dos A'),
                  ('{g.TorneoB}', '{b}', 'RAW-B1-{sufijo}', 'Torneo Uno B');
                insert into goalflow.fases (id, tenant_id, torneo_id, orden, tipo, vueltas) values
                  ('{g.FaseA}', '{a}', '{g.TorneoA}', 1, 'TodosContraTodos', 1),
                  ('{g.FaseA2}', '{a}', '{g.TorneoA2}', 1, 'TodosContraTodos', 1),
                  ('{g.FaseB}', '{b}', '{g.TorneoB}', 1, 'TodosContraTodos', 1);
                insert into goalflow.equipos (id, tenant_id, codigo, nombre) values
                  ('{equipoA1}', '{a}', 'RAW-EA1-{sufijo}', 'Equipo A1'),
                  ('{equipoA2}', '{a}', 'RAW-EA2-{sufijo}', 'Equipo A2'),
                  ('{g.EquipoB}', '{b}', 'RAW-EB1-{sufijo}', 'Equipo B1'),
                  ('{equipoB2}', '{b}', 'RAW-EB2-{sufijo}', 'Equipo B2');
                insert into goalflow.equipos_torneo (id, tenant_id, torneo_id, equipo_id, estado) values
                  ('{g.ParticipacionA1}', '{a}', '{g.TorneoA}', '{equipoA1}', 'Inscrito'),
                  ('{g.ParticipacionA2}', '{a}', '{g.TorneoA}', '{equipoA2}', 'Inscrito'),
                  ('{g.ParticipacionA2DelOtroTorneo}', '{a}', '{g.TorneoA2}', '{equipoA1}', 'Inscrito'),
                  ('{g.ParticipacionB1}', '{b}', '{g.TorneoB}', '{g.EquipoB}', 'Inscrito'),
                  ('{g.ParticipacionB2}', '{b}', '{g.TorneoB}', '{equipoB2}', 'Inscrito');
                insert into goalflow.jugadores (id, tenant_id, codigo, nombre) values
                  ('{g.JugadorA}', '{a}', 'RAW-JA-{sufijo}', 'Jugador Uno A'),
                  ('{g.JugadorB}', '{b}', 'RAW-JB-{sufijo}', 'Jugador Uno B');
                """);

            PorBase[db.CadenaSuperusuario] = g;
            return g;
        }
        finally
        {
            Candado.Release();
        }
    }
}
