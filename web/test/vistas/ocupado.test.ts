import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fixture, instalarApiFalsa, PARTIDOS } from '../ayudantes/api_falsa'
import { respuesta } from '../ayudantes/dobles'
import { abrir, avanzar } from '../ayudantes/montaje'

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const SELECTOR_REINTENTAR = '[data-accion="reintentar"]'
const SELECTOR_CUENTA = '[data-cuenta-regresiva]'

/** Una API cuyos partidos dan 429 la primera vez y la lista real después. */
function apiOcupada(cabeceras: Record<string, string>) {
  let pedidos = 0
  const api = instalarApiFalsa({
    [PARTIDOS]: () => (pedidos++ === 0 ? respuesta(429, { error: 'limite_de_frecuencia' }, cabeceras) : respuesta(200, fixture('S011-02'))),
  })
  return { ...api, pedidosDePartidos: () => api.rutasLlamadas().filter((r) => r === PARTIDOS).length }
}

describe('T-19 (vista): un 429 muestra el aviso, una cuenta regresiva y un botón manual (R-39)', () => {
  it('Retry-After 7: botón deshabilitado con cuenta regresiva hasta 0, sin reintento automático y un clic hace una sola petición', async () => {
    const { pedidosDePartidos } = apiOcupada({ 'Retry-After': '7' })
    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    expect(app.envoltorio.text()).toContain('El servicio está ocupado')
    expect(app.envoltorio.get(SELECTOR_REINTENTAR).attributes('disabled')).toBeDefined()
    expect(app.envoltorio.get(SELECTOR_CUENTA).text()).toContain('7')

    await avanzar(3_000)
    expect(app.envoltorio.get(SELECTOR_CUENTA).text()).toContain('4')
    expect(app.envoltorio.get(SELECTOR_REINTENTAR).attributes('disabled')).toBeDefined()

    await avanzar(4_000)
    expect(app.envoltorio.get(SELECTOR_REINTENTAR).attributes('disabled')).toBeUndefined()
    expect(app.envoltorio.find(SELECTOR_CUENTA).exists()).toBe(false)

    await avanzar(120_000)
    expect(pedidosDePartidos()).toBe(1) // sin reintento automático, ni al llegar a 0 ni después
    expect(app.almacen.datosDe(PARTIDOS)).toBeUndefined() // el 429 no se guarda

    await app.envoltorio.get(SELECTOR_REINTENTAR).trigger('click')
    await avanzar(0)
    expect(pedidosDePartidos()).toBe(2)
    expect(app.almacen.datosDe(PARTIDOS)).toBeDefined()
    expect(app.envoltorio.find('table').exists()).toBe(true)
  })

  it.each([
    ['ausente', {}],
    ['abc', { 'Retry-After': 'abc' }],
    ['una fecha HTTP', { 'Retry-After': 'Sat, 21 Nov 2026 07:28:00 GMT' }],
  ])('Retry-After %s: el botón está habilitado de inmediato y no hay cuenta regresiva', async (_nombre, cabeceras) => {
    const { pedidosDePartidos } = apiOcupada(cabeceras)
    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    expect(app.envoltorio.get(SELECTOR_REINTENTAR).attributes('disabled')).toBeUndefined()
    expect(app.envoltorio.find(SELECTOR_CUENTA).exists()).toBe(false)
    await avanzar(120_000)
    expect(pedidosDePartidos()).toBe(1)
  })
})
