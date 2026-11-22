import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { instalarApiFalsa } from '../ayudantes/api_falsa'

/**
 * T-25 (C-27), prueba de caracterización: el enrutador real de src/ con createWebHistory solo toca una clave del almacenamiento del
 * navegador, sessionStorage["vue-router:scroll"], que es la contabilidad de scroll propia de vue-router 5. Cada caso reevalúa el módulo de
 * vue-router (que lee esa clave al cargarse) con el almacenamiento limpio.
 *
 * LÍMITE: jsdom no desplaza la página, así que todo valor de posición es 0. Esta prueba verifica la FORMA de lo que se guarda, no posiciones reales.
 */
const CLAVE_DEL_ROUTER = 'vue-router:scroll'

beforeEach(() => {
  sessionStorage.clear()
  localStorage.clear()
  history.scrollRestoration = 'auto'
  vi.resetModules()
})
afterEach(() => {
  vi.unstubAllGlobals()
  sessionStorage.clear()
  localStorage.clear()
  history.scrollRestoration = 'auto'
})

/** El enrutador real, recién importado (con su propio vue-router y su propio almacén), tras tres o más navegaciones (push y replace). */
async function navegarConElEnrutadorReal() {
  instalarApiFalsa()
  const { createPinia } = await import('pinia')
  const { createWebHistory } = await import('vue-router')
  const { crearEnrutador } = await import('../../src/enrutador/enrutador')
  const { useAlmacen } = await import('../../src/almacen/almacen')
  const pinia = createPinia()
  const enrutador = crearEnrutador(createWebHistory(), pinia)

  await enrutador.push('/')
  await enrutador.push('/liga-humo')
  await enrutador.push('/liga-humo/torneos/HUMO-T1/fases/1/partidos')
  await enrutador.replace('/liga-humo/torneos/HUMO-T1/valla')
  await enrutador.push('/liga-humo/torneos/HUMO-T1/fases/1/partidos/HUMO-T1-E1/HUMO-T1-E2')
  await useAlmacen(pinia).esperarPendientes()
}

/** Las aserciones comunes: la única clave es la del router, y no hay localStorage ni cookies. */
function exigirSoloElAlmacenamientoDelRouter() {
  expect(Object.keys(sessionStorage)).toEqual([CLAVE_DEL_ROUTER])
  expect(localStorage.length).toBe(0)
  expect(document.cookie).toBe('')
}

describe('T-25 (C-27): la contabilidad de scroll de vue-router es el único almacenamiento del navegador', () => {
  it('(a) con la configuración de producción (sin scrollBehavior) la clave existe y su valor es exactamente {}', async () => {
    await navegarConElEnrutadorReal()

    exigirSoloElAlmacenamientoDelRouter()
    expect(sessionStorage.getItem(CLAVE_DEL_ROUTER)).toBe('{}')
  })

  it('(b) con scrollRestoration manual (lo que vue-router fija si hay scrollBehavior) guarda al menos una posición { left, top } numérica', async () => {
    history.scrollRestoration = 'manual'

    await navegarConElEnrutadorReal()

    exigirSoloElAlmacenamientoDelRouter()
    const posiciones = JSON.parse(sessionStorage.getItem(CLAVE_DEL_ROUTER) ?? 'null') as Record<string, { left: unknown; top: unknown }>
    const entradas = Object.entries(posiciones)
    expect(entradas.length).toBeGreaterThanOrEqual(1)
    for (const [, posicion] of entradas) {
      expect(Object.keys(posicion).sort()).toEqual(['left', 'top'])
      expect(typeof posicion.left).toBe('number')
      expect(typeof posicion.top).toBe('number')
    }
  })

  it('control positivo: las aserciones detectan un localStorage, una cookie y otra clave de sessionStorage añadidos a propósito', async () => {
    await navegarConElEnrutadorReal()
    exigirSoloElAlmacenamientoDelRouter() // sin nada añadido, pasa

    localStorage.setItem('x', '1')
    expect(exigirSoloElAlmacenamientoDelRouter).toThrow()
    localStorage.clear()

    sessionStorage.setItem('extra', '1')
    expect(exigirSoloElAlmacenamientoDelRouter).toThrow()
    sessionStorage.removeItem('extra')

    document.cookie = 'galleta=1'
    expect(exigirSoloElAlmacenamientoDelRouter).toThrow()
    document.cookie = 'galleta=; expires=Thu, 01 Jan 1970 00:00:00 GMT'
    exigirSoloElAlmacenamientoDelRouter() // limpio otra vez
  })
})
