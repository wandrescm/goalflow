import { afterEach, describe, expect, it, vi } from 'vitest'
import { pedir } from '../../src/api/cliente'
import { rutaCatalogo, rutaDetalle, rutaGoleadores, rutaPartidos, rutaPosiciones, rutaValla } from '../../src/api/rutas'
import { instalarFetch, respuesta, URL_BASE } from '../ayudantes/dobles'

afterEach(() => vi.unstubAllGlobals())

const rutas = [
  rutaCatalogo('liga-humo'),
  rutaPartidos('liga-humo', 'HUMO-T1', 1),
  rutaDetalle('liga-humo', 'HUMO-T1', 1, 'HUMO-T1-E1', 'HUMO-T1-E2'),
  rutaPosiciones('liga-humo', 'HUMO-T1', 1),
  rutaGoleadores('liga-humo', 'HUMO-T1'),
  rutaValla('liga-humo', 'HUMO-T1'),
]

describe('T-17: cada petición es un GET simple, sin credenciales y sin más cabecera que Accept (R-36, C-18)', () => {
  it.each(rutas)('%s: método GET, credentials omit, solo Accept y una señal', async (ruta) => {
    const { llamadas } = instalarFetch(() => respuesta(200, {}))

    await pedir(ruta)

    expect(llamadas).toHaveLength(1)
    const [llamada] = llamadas
    expect(llamada?.url).toBe(`${URL_BASE}${ruta}`)
    const opciones = llamada?.opciones ?? {}
    expect(Object.keys(opciones).sort()).toEqual(['credentials', 'headers', 'method', 'signal'])
    expect(opciones.method).toBe('GET')
    expect(opciones.credentials).toBe('omit')
    expect(opciones.headers).toEqual({ Accept: 'application/json' })
    expect(opciones.signal).toBeInstanceOf(AbortSignal)
  })
})
