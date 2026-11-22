import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'
import { useAlmacen } from '../../src/almacen/almacen'
import type { Catalogo } from '../../src/api/tipos'
import { crearEnrutador } from '../../src/enrutador/enrutador'
import { respuesta } from '../ayudantes/dobles'
import { CATALOGO, fixture, instalarApiFalsa, SLUG, T1 } from '../ayudantes/api_falsa'

afterEach(() => vi.unstubAllGlobals())

function montar() {
  const pinia = createPinia()
  setActivePinia(pinia)
  return { enrutador: crearEnrutador(createMemoryHistory(), pinia), almacen: useAlmacen(pinia) }
}

async function ir(m: ReturnType<typeof montar>, ruta: string) {
  await m.enrutador.push(ruta)
  await m.almacen.esperarPendientes()
  await flushPromises()
  return m.enrutador.currentRoute.value
}

describe('R-34 y R-35: las rutas reflejan las de la API y lo demás es «no encontrado»', () => {
  it.each([
    ['/', 'inicio'],
    [`/${SLUG}`, 'liga'],
    [`/${SLUG}/torneos/${T1}/fases/1/partidos`, 'partidos'],
    [`/${SLUG}/torneos/${T1}/fases/1/posiciones`, 'posiciones'],
    [`/${SLUG}/torneos/${T1}/goleadores`, 'goleadores'],
    [`/${SLUG}/torneos/${T1}/valla`, 'valla'],
    [`/${SLUG}/torneos/${T1}/fases/1/partidos/HUMO-T1-E1/HUMO-T1-E2`, 'detalle'],
    [`/${SLUG}/nada/por/aqui`, 'no-encontrado'],
    [`/${SLUG}/torneos`, 'no-encontrado'],
    [`/${SLUG}/torneos/${T1}/fases/uno/partidos`, 'no-encontrado'],
  ])('%s → %s', async (ruta, nombre) => {
    instalarApiFalsa()

    expect((await ir(montar(), ruta)).name).toBe(nombre)
  })

  it('/:slug/torneos/:codigo redirige a los partidos de la fase de menor orden del catálogo', async () => {
    const catalogo = fixture<Catalogo>('S011-01')
    catalogo.torneos[0]!.fases = [
      { orden: 3, tipo: 'TodosContraTodos', vueltas: 1 },
      { orden: 2, tipo: 'TodosContraTodos', vueltas: 1 },
      { orden: 5, tipo: 'TodosContraTodos', vueltas: 2 },
    ]
    instalarApiFalsa({ [CATALOGO]: () => respuesta(200, catalogo) })

    const ruta = await ir(montar(), `/${SLUG}/torneos/${T1}`)

    expect(ruta.name).toBe('partidos')
    expect(ruta.path).toBe(`/${SLUG}/torneos/${T1}/fases/2/partidos`)
  })

  it('un torneo que el catálogo no tiene, o un catálogo 404, dan «no encontrado»', async () => {
    instalarApiFalsa()
    expect((await ir(montar(), `/${SLUG}/torneos/NO-EXISTE`)).name).toBe('no-encontrado')

    instalarApiFalsa({ [CATALOGO]: () => respuesta(404) })
    expect((await ir(montar(), `/${SLUG}/torneos/${T1}`)).name).toBe('no-encontrado')
  })

  it('un error del catálogo al entrar a un torneo no redirige ni reintenta: la ruta del torneo queda con el error', async () => {
    const { llamadas } = instalarApiFalsa({ [CATALOGO]: () => respuesta(500) })
    const m = montar()

    const ruta = await ir(m, `/${SLUG}/torneos/${T1}`)

    expect(ruta.name).toBe('torneo')
    expect(llamadas).toHaveLength(1)
    expect(m.almacen.estadoDe(CATALOGO).tipo).toBe('error')
  })
})
