import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAlmacen } from '../../src/almacen/almacen'
import { rutaCatalogo, rutaPartidos } from '../../src/api/rutas'
import { instalarApiFalsa, SLUG, T1 } from '../ayudantes/api_falsa'
import { instalarFetch, instalarFetchQueNuncaResuelve, respuesta } from '../ayudantes/dobles'

beforeEach(() => setActivePinia(createPinia()))
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const CATALOGO = rutaCatalogo(SLUG)
const PARTIDOS = rutaPartidos(SLUG, T1, 1)

describe('R-38: el almacén guarda cada respuesta buena por su ruta, solo mientras vive la página', () => {
  it('asegurar guarda la respuesta ok con la hora de carga, y una segunda llamada no pide nada', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-21T15:00:00Z'))
    const { llamadas } = instalarApiFalsa()
    const almacen = useAlmacen()

    await almacen.asegurar(CATALOGO)
    await almacen.asegurar(CATALOGO)

    expect(llamadas).toHaveLength(1)
    expect(almacen.estadoDe(CATALOGO)).toEqual({ tipo: 'ok' })
    expect(almacen.datosDe<{ liga: { slug: string } }>(CATALOGO)?.liga.slug).toBe('liga-humo')
    expect(almacen.cargadoEnDe(CATALOGO)?.toISOString()).toBe('2026-11-21T15:00:00.000Z')
  })

  it('dos asegurar a la vez del mismo recurso hacen una sola petición', async () => {
    const { llamadas } = instalarApiFalsa()
    const almacen = useAlmacen()

    await Promise.all([almacen.asegurar(PARTIDOS), almacen.asegurar(PARTIDOS)])

    expect(llamadas).toHaveLength(1)
  })

  it('«Actualizar» reemplaza solo la entrada de esa ruta, con una petición, y deja las demás intactas', async () => {
    let jornada = 1
    const { llamadas } = instalarApiFalsa({
      [PARTIDOS]: () => respuesta(200, { torneo: { codigo: T1, nombre: 'Torneo F6' }, fase: { orden: 1 }, partidos: [], jornada: jornada++ }),
    })
    const almacen = useAlmacen()
    await almacen.asegurar(CATALOGO)
    await almacen.asegurar(PARTIDOS)
    const catalogoAntes = almacen.datosDe(CATALOGO)

    await almacen.actualizar(PARTIDOS)

    expect(llamadas).toHaveLength(3)
    expect(almacen.datosDe<{ jornada: number }>(PARTIDOS)?.jornada).toBe(2)
    expect(almacen.datosDe(CATALOGO)).toBe(catalogoAntes)
  })

  it.each([
    ['404', () => respuesta(404), 'no-encontrado'],
    ['500', () => respuesta(500), 'error'],
    ['429', () => respuesta(429, undefined, { 'Retry-After': '7' }), 'ocupado'],
    ['409 configuracion_pendiente', () => respuesta(409, { codigo: 'configuracion_pendiente', faltantes: ['puntos'] }), 'configuracion-pendiente'],
  ])('una respuesta %s nunca se guarda y deja su estado para la vista', async (_nombre, manejador, tipo) => {
    instalarFetch(manejador)
    const almacen = useAlmacen()

    await almacen.asegurar(CATALOGO)

    expect(almacen.datosDe(CATALOGO)).toBeUndefined()
    expect(almacen.cargadoEnDe(CATALOGO)).toBeUndefined()
    expect(almacen.estadoDe(CATALOGO).tipo).toBe(tipo)
  })

  it('un fallo de red nunca se guarda', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))))
    const almacen = useAlmacen()

    await almacen.asegurar(CATALOGO)

    expect(almacen.datosDe(CATALOGO)).toBeUndefined()
    expect(almacen.estadoDe(CATALOGO).tipo).toBe('error')
  })

  it('durante la carga el estado es «cargando»; a los 10 s «lento»; a los 30 s «tiempo-agotado» y no se guarda nada', async () => {
    vi.useFakeTimers()
    instalarFetchQueNuncaResuelve()
    const almacen = useAlmacen()

    const pendiente = almacen.asegurar(CATALOGO)
    expect(almacen.estadoDe(CATALOGO)).toEqual({ tipo: 'cargando', lento: false })
    await vi.advanceTimersByTimeAsync(10_000)
    expect(almacen.estadoDe(CATALOGO)).toEqual({ tipo: 'cargando', lento: true })
    await vi.advanceTimersByTimeAsync(20_000)
    await pendiente

    expect(almacen.estadoDe(CATALOGO).tipo).toBe('tiempo-agotado')
    expect(almacen.datosDe(CATALOGO)).toBeUndefined()
  })

  it('un recurso nunca pedido está «sin-cargar»', () => {
    expect(useAlmacen().estadoDe(CATALOGO)).toEqual({ tipo: 'sin-cargar' })
  })

  it('un «Actualizar» que falla deja el estado de error y no borra los datos buenos que ya había', async () => {
    let fallar = false
    instalarApiFalsa({ [PARTIDOS]: () => (fallar ? respuesta(500) : respuesta(200, { partidos: [] })) })
    const almacen = useAlmacen()
    await almacen.asegurar(PARTIDOS)

    fallar = true
    await almacen.actualizar(PARTIDOS)

    expect(almacen.estadoDe(PARTIDOS).tipo).toBe('error')
    expect(almacen.datosDe(PARTIDOS)).toEqual({ partidos: [] })
  })
})
