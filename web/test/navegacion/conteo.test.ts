import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'
import { rutaCatalogo, rutaDetalle, rutaGoleadores, rutaPartidos, rutaPosiciones, rutaValla } from '../../src/api/rutas'
import { useAlmacen } from '../../src/almacen/almacen'
import { crearEnrutador } from '../../src/enrutador/enrutador'
import { instalarApiFalsa, PARTIDOS, SLUG, T1 } from '../ayudantes/api_falsa'

afterEach(() => vi.unstubAllGlobals())

/** Un almacén y un enrutador nuevos: cada enlace profundo «frío» empieza con el almacén vacío. */
function montar() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const enrutador = crearEnrutador(createMemoryHistory(), pinia)
  return { enrutador, almacen: useAlmacen(pinia) }
}

type Montaje = ReturnType<typeof montar>

async function navegar({ enrutador, almacen }: Montaje, ruta: string) {
  await enrutador.push(ruta)
  await almacen.esperarPendientes()
  await flushPromises()
}

const RUTA_PARTIDOS = `/${SLUG}/torneos/${T1}/fases/1/partidos`
const RUTA_POSICIONES = `/${SLUG}/torneos/${T1}/fases/1/posiciones`
const RUTA_GOLEADORES = `/${SLUG}/torneos/${T1}/goleadores`
const RUTA_VALLA = `/${SLUG}/torneos/${T1}/valla`
const RUTA_DETALLE = `${RUTA_PARTIDOS}/HUMO-T1-E1/HUMO-T1-E2`

describe('T-18: las llamadas de cada navegación y el total acumulado (R-34, R-37, R-38)', () => {
  it('recorrido desde un almacén vacío: 1, 1, 1, 0, 1, 1, 0 y «Actualizar» 1, con acumulados 1 a 6', async () => {
    const { llamadas } = instalarApiFalsa()
    const m = montar()
    const pasos: [string, () => Promise<void>, number, number][] = [
      ['/:slug', () => navegar(m, `/${SLUG}`), 1, 1],
      ['pestaña de partidos de la fase 1', () => navegar(m, RUTA_PARTIDOS), 1, 2],
      ['posiciones', () => navegar(m, RUTA_POSICIONES), 1, 3],
      ['de vuelta a partidos', () => navegar(m, RUTA_PARTIDOS), 0, 3],
      ['goleadores', () => navegar(m, RUTA_GOLEADORES), 1, 4],
      ['un detalle', () => navegar(m, RUTA_DETALLE), 1, 5],
      ['de vuelta a partidos', () => navegar(m, RUTA_PARTIDOS), 0, 5],
      ['«Actualizar» en partidos', async () => { await m.almacen.actualizar(rutaPartidos(SLUG, T1, 1)) }, 1, 6],
    ]

    for (const [nombre, paso, delta, acumulado] of pasos) {
      const antes = llamadas.length
      await paso()
      await flushPromises()
      expect(llamadas.length - antes, `llamadas de «${nombre}»`).toBe(delta)
      expect(llamadas.length, `acumulado tras «${nombre}»`).toBe(acumulado)
    }
  })

  it.each([
    ['la pestaña de partidos', RUTA_PARTIDOS, [rutaCatalogo(SLUG), rutaPartidos(SLUG, T1, 1)]],
    ['las posiciones', RUTA_POSICIONES, [rutaCatalogo(SLUG), rutaPosiciones(SLUG, T1, 1)]],
    ['los goleadores', RUTA_GOLEADORES, [rutaCatalogo(SLUG), rutaGoleadores(SLUG, T1)]],
    ['la valla', RUTA_VALLA, [rutaCatalogo(SLUG), rutaValla(SLUG, T1)]],
  ])('enlace profundo frío a %s: 2 llamadas en esa navegación, los torneos y el recurso de la pestaña', async (_nombre, ruta, esperadas) => {
    const { rutasLlamadas } = instalarApiFalsa()
    const m = montar()

    await navegar(m, ruta)

    expect([...rutasLlamadas()].sort()).toEqual([...esperadas].sort())
  })

  it('enlace profundo frío a un detalle: 1 llamada, solo el detalle', async () => {
    const { rutasLlamadas } = instalarApiFalsa()
    const m = montar()

    await navegar(m, RUTA_DETALLE)

    expect(rutasLlamadas()).toEqual([rutaDetalle(SLUG, T1, 1, 'HUMO-T1-E1', 'HUMO-T1-E2')])
  })

  it('entrar a un torneo desde la página de la liga ya cargada cuesta 1 llamada', async () => {
    const { llamadas, rutasLlamadas } = instalarApiFalsa()
    const m = montar()
    await navegar(m, `/${SLUG}`)
    expect(llamadas).toHaveLength(1)

    await navegar(m, `/${SLUG}/torneos/${T1}`) // /:slug/torneos/:codigo redirige a los partidos de la fase de menor orden

    expect(llamadas).toHaveLength(2)
    expect(rutasLlamadas()[1]).toBe(rutaPartidos(SLUG, T1, 1))
    expect(m.enrutador.currentRoute.value.path).toBe(PARTIDOS)
  })

  it('sin sondeo, sin precarga y sin reintento: pasado el tiempo no hay ninguna llamada más', async () => {
    vi.useFakeTimers()
    try {
      const { llamadas } = instalarApiFalsa()
      const m = montar()
      await navegar(m, RUTA_PARTIDOS)
      const despues = llamadas.length

      await vi.advanceTimersByTimeAsync(10 * 60_000)

      expect(despues).toBe(2)
      expect(llamadas).toHaveLength(despues)
    } finally {
      vi.useRealTimers()
    }
  })

  it('la raíz, una ruta desconocida y un orden que no es un número no hacen ninguna llamada', async () => {
    const { llamadas } = instalarApiFalsa()
    const m = montar()

    for (const ruta of ['/', `/${SLUG}/otra/cosa`, `/${SLUG}/torneos/${T1}/fases/abc/partidos`, `/${SLUG}/torneos`]) await navegar(m, ruta)

    expect(llamadas).toHaveLength(0)
  })
})
