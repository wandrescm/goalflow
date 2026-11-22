import { afterEach, describe, expect, it, vi } from 'vitest'
import type { DetalleDePartido, PartidosDeLaFase, Posiciones } from '../../src/api/tipos'
import { fixture, instalarApiFalsa, PARTIDOS, SLUG, T1 } from '../ayudantes/api_falsa'
import { respuesta } from '../ayudantes/dobles'
import { abrir } from '../ayudantes/montaje'

afterEach(() => vi.unstubAllGlobals())

const POSICIONES = `/${SLUG}/torneos/${T1}/fases/1/posiciones`
const GOLEADORES = `/${SLUG}/torneos/${T1}/goleadores`
const VALLA = `/${SLUG}/torneos/${T1}/valla`
const DETALLE_E1E2 = `${PARTIDOS}/HUMO-T1-E1/HUMO-T1-E2`
const DETALLE_E1E6 = `${PARTIDOS}/HUMO-T1-E1/HUMO-T1-E6`
const DETALLE_E3E4 = `${PARTIDOS}/HUMO-T1-E3/HUMO-T1-E4`
const HOSTIL = '<img src=x onerror=alert(1)>'

const filas = (app: Awaited<ReturnType<typeof abrir>>) => app.envoltorio.findAll('tbody tr').map((f) => f.findAll('td').map((c) => c.text()))

describe('T-21 (vista): la lista de partidos muestra el campo que elige cada estado (R-42)', () => {
  it('los cinco estados de la lista real: marcador, oficial con motivo, suspendido con motivo, «Por programar»', async () => {
    instalarApiFalsa()

    const app = await abrir(PARTIDOS)
    const texto = app.envoltorio.text()

    expect(texto).toContain('2 – 1') // Jugado
    expect(texto).toContain('3 – 0') // Adjudicado, de oficial
    expect(texto).toContain('Resultado oficial: W.O.')
    expect(texto).toContain('Suspendido por disturbios')
    expect(texto).toContain('1 – 0') // marcador parcial del Suspendido
    expect(texto).toContain('Por programar')
  })

  it('una variante con Cancelado y un Programado con hora: «Cancelado» y la fecha y hora de Bogotá, sin la hora del equipo', async () => {
    const lista = fixture<PartidosDeLaFase>('S011-02')
    lista.partidos[2] = { ...lista.partidos[2]!, estado: 'Cancelado' }
    lista.partidos[5] = { ...lista.partidos[5]!, programadoPara: '2026-11-21T01:00:00Z' }
    instalarApiFalsa({ [PARTIDOS]: () => respuesta(200, lista) })

    const app = await abrir(PARTIDOS)
    const texto = app.envoltorio.text()

    expect(texto).toContain('Cancelado')
    expect(texto).toContain('20/11/2026 20:00')
    expect(texto).not.toContain('21/11/2026 10:00')
  })

  it('los enlaces al detalle se construyen solo con los códigos de la lista', async () => {
    instalarApiFalsa()

    const app = await abrir(PARTIDOS)
    const enlaces = app.envoltorio.findAll('a').map((a) => a.attributes('href'))

    expect(enlaces).toContain(DETALLE_E1E2)
    expect(enlaces).toContain(`${PARTIDOS}/HUMO-T1-E4/HUMO-T1-E5`)
  })
})

describe('T-22 (vista): la fecha programada se muestra en Bogotá', () => {
  it('el detalle de un Programado a 2026-11-21T01:00:00Z muestra el 20 a las 20, aunque el proceso esté en Asia/Tokyo', async () => {
    const detalle = fixture<DetalleDePartido>('S011-03-E3E6')
    detalle.programadoPara = '2026-11-21T01:00:00Z'
    instalarApiFalsa({ [`${PARTIDOS}/HUMO-T1-E3/HUMO-T1-E6`]: () => respuesta(200, detalle) })

    const app = await abrir(`${PARTIDOS}/HUMO-T1-E3/HUMO-T1-E6`)

    expect(app.envoltorio.text()).toContain('20/11/2026 20:00')
  })
})

describe('T-23 (render): los textos no confiables se muestran como texto, nunca como HTML (R-46)', () => {
  it('un equipo, un jugador y un resumen con <img onerror> salen literales y no crean ningún elemento img', async () => {
    const detalle = fixture<DetalleDePartido>('S011-03-E1E2')
    detalle.local.nombre = HOSTIL
    detalle.goles[0]!.jugador.nombre = HOSTIL
    detalle.resumen = { texto: HOSTIL, origen: 'Modelo' }
    instalarApiFalsa({ [DETALLE_E1E2]: () => respuesta(200, detalle) })

    const app = await abrir(DETALLE_E1E2)

    expect(app.envoltorio.find('img').exists()).toBe(false)
    expect(app.envoltorio.element.querySelector('img')).toBeNull()
    expect(app.envoltorio.text()).toContain(HOSTIL)
    expect(app.envoltorio.text().split(HOSTIL).length - 1).toBeGreaterThanOrEqual(3) // equipo, jugador y resumen
  })

  it('un nombre de equipo hostil en la lista y en las posiciones tampoco crea elementos', async () => {
    const lista = fixture<PartidosDeLaFase>('S011-02')
    lista.partidos[0]!.local.nombre = HOSTIL
    const posiciones = fixture<Posiciones>('S009-01')
    posiciones.filas[0]!.equipo.nombre = HOSTIL
    instalarApiFalsa({ [PARTIDOS]: () => respuesta(200, lista), [POSICIONES]: () => respuesta(200, posiciones) })

    const enLista = await abrir(PARTIDOS)
    const enPosiciones = await abrir(POSICIONES)

    for (const app of [enLista, enPosiciones]) {
      expect(app.envoltorio.element.querySelector('img')).toBeNull()
      expect(app.envoltorio.text()).toContain(HOSTIL)
    }
  })
})

describe('T-24: las tablas se muestran tal como llegan y el detalle respeta lo que dicen los datos (R-43, R-44)', () => {
  it('las posiciones: marca «Provisional», cobertura, sin empates y las filas en el orden recibido', async () => {
    instalarApiFalsa()

    const app = await abrir(POSICIONES)
    const texto = app.envoltorio.text()

    expect(texto).toContain('Reglamento provisional')
    expect(texto).toContain('Partidos con tarjetas computables: 9')
    expect(texto).toContain('Partidos con tarjetas sin informar: 2')
    expect(texto).not.toContain('Empate sin resolver')
    // El orden es el de la API (E1, E2, E3, E5, E6, E4): el frontend no reordena ni recalcula nada.
    expect(filas(app).map((f) => f[1])).toEqual(['Equipo E1', 'Equipo E2', 'Equipo E3', 'Equipo E5', 'Equipo E6', 'Equipo E4'])
  })

  it('un empate sin resolver se muestra con sus equipos y su posición, sin reordenar las filas', async () => {
    const posiciones = fixture<Posiciones>('S009-01')
    posiciones.empates = [{ posicion: 3, equipos: ['HUMO-T1-E3', 'HUMO-T1-E5'], motivo: 'criterios_agotados', criteriosOmitidos: [] }]
    instalarApiFalsa({ [POSICIONES]: () => respuesta(200, posiciones) })

    const app = await abrir(POSICIONES)

    expect(app.envoltorio.text()).toContain('Empate sin resolver en la posición 3: Equipo E3, Equipo E5')
    expect(filas(app).map((f) => f[1])).toEqual(['Equipo E1', 'Equipo E2', 'Equipo E3', 'Equipo E5', 'Equipo E6', 'Equipo E4'])
  })

  it('un reglamento Confirmado no se marca como provisional', async () => {
    const posiciones = fixture<Posiciones>('S009-01')
    posiciones.reglamento = { version: 2, estado: 'Confirmado' }
    instalarApiFalsa({ [POSICIONES]: () => respuesta(200, posiciones) })

    const app = await abrir(POSICIONES)

    expect(app.envoltorio.text()).not.toContain('Reglamento provisional')
  })

  it('los goleadores y la valla: marca «Provisional», los datos recibidos y la cobertura de los goleadores', async () => {
    instalarApiFalsa()

    const goleadores = await abrir(GOLEADORES)
    const valla = await abrir(VALLA)

    expect(goleadores.envoltorio.text()).toContain('Reglamento provisional')
    expect(filas(goleadores).map((f) => f[1])).toEqual([
      'Jugador P21', 'Jugador P51', 'Jugador P11', 'Jugador P12', 'Jugador P22', 'Jugador P61', 'Jugador P62',
    ])
    expect(goleadores.envoltorio.text()).toContain('Goles sin autor registrado: 4')
    expect(valla.envoltorio.text()).toContain('Reglamento provisional')
    expect(filas(valla).map((f) => f[1])).toEqual(['Equipo E6', 'Equipo E1', 'Equipo E3', 'Equipo E5', 'Equipo E4', 'Equipo E2'])
  })

  it('el detalle con tarjetasInformadas true muestra las tarjetas; con la lista vacía, «Sin tarjetas»', async () => {
    instalarApiFalsa()

    const conTarjetas = await abrir(DETALLE_E1E2)
    const sinTarjetas = await abrir(DETALLE_E1E6)

    expect(conTarjetas.envoltorio.text()).toContain('Amarilla')
    expect(conTarjetas.envoltorio.text()).not.toContain('Sin tarjetas')
    expect(sinTarjetas.envoltorio.text()).toContain('Sin tarjetas')
  })

  it('con tarjetasInformadas false dice «Tarjetas no informadas»; con null no dice nada de tarjetas', async () => {
    const informadasFalse = fixture<DetalleDePartido>('S011-03-E1E6')
    informadasFalse.tarjetasInformadas = false
    instalarApiFalsa({ [DETALLE_E1E6]: () => respuesta(200, informadasFalse) })

    const enFalse = await abrir(DETALLE_E1E6)
    const enNull = await abrir(DETALLE_E3E4)

    expect(enFalse.envoltorio.text()).toContain('Tarjetas no informadas')
    expect(enFalse.envoltorio.text()).not.toContain('Sin tarjetas')
    expect(enNull.envoltorio.text().toLowerCase()).not.toContain('tarjeta')
  })

  it('con resumen nulo no hay sección ni mensaje sobre resúmenes; con un resumen se muestra su texto marcado por su origen', async () => {
    const conModelo = fixture<DetalleDePartido>('S011-03-E1E2')
    conModelo.resumen = { texto: 'El local ganó 2-1.', origen: 'Modelo' }
    const conPlantilla = fixture<DetalleDePartido>('S011-03-E1E6')
    conPlantilla.resumen = { texto: 'Empate 1-1.', origen: 'Plantilla' }
    instalarApiFalsa({ [DETALLE_E1E2]: () => respuesta(200, conModelo), [DETALLE_E1E6]: () => respuesta(200, conPlantilla) })

    const nulo = await abrir(DETALLE_E3E4)
    const modelo = await abrir(DETALLE_E1E2)
    const plantilla = await abrir(DETALLE_E1E6)

    expect(nulo.envoltorio.text().toLowerCase()).not.toContain('resumen')
    expect(modelo.envoltorio.text()).toContain('El local ganó 2-1.')
    expect(modelo.envoltorio.text()).toContain('Generado por el modelo')
    expect(plantilla.envoltorio.text()).toContain('Empate 1-1.')
    expect(plantilla.envoltorio.text()).toContain('Generado con una plantilla')
  })

  it('el detalle muestra los hechos de la planilla: goles, autogoles y el resultado del partido', async () => {
    instalarApiFalsa()

    const app = await abrir(DETALLE_E1E6)
    const texto = app.envoltorio.text()

    expect(texto).toContain('Equipo E1')
    expect(texto).toContain('1 – 1')
    expect(texto).toContain('Jugador P62') // gol del visitante
    expect(texto).toContain('Jugador P61') // autogol, a favor del local
    expect(texto).toContain('Autogoles')
  })
})

describe('R-34, R-35 y R-48: la raíz, la liga y la hora de carga', () => {
  it('la raíz muestra solo un texto fijo y no pide nada', async () => {
    const { llamadas } = instalarApiFalsa()

    const app = await abrir('/')

    expect(app.envoltorio.text()).toContain('Cada liga comparte su propio enlace.')
    expect(llamadas).toHaveLength(0)
  })

  it('la página de la liga lista sus torneos con un enlace cada uno y muestra la hora de carga en Bogotá con «Actualizar»', async () => {
    instalarApiFalsa()

    const app = await abrir(`/${SLUG}`)

    expect(app.envoltorio.text()).toContain('Liga de humo')
    expect(app.envoltorio.text()).toContain('Torneo F6')
    expect(app.envoltorio.findAll('a').map((a) => a.attributes('href'))).toContain(`/${SLUG}/torneos/${T1}`)
    expect(app.envoltorio.get('[data-cargado-en]').text()).toMatch(/Cargado a las \d{2}:\d{2}/)
    expect(app.envoltorio.find('[data-accion="actualizar"]').exists()).toBe(true)
  })

  it('«Actualizar» hace una sola petición del recurso de la vista actual', async () => {
    const { rutasLlamadas } = instalarApiFalsa()
    const app = await abrir(PARTIDOS)
    const antes = rutasLlamadas().length

    await app.envoltorio.get('[data-accion="actualizar"]').trigger('click')
    await app.almacen.esperarPendientes()

    expect(rutasLlamadas().slice(antes)).toEqual([PARTIDOS])
  })
})
