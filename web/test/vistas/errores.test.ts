import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fixture, instalarApiFalsa, PARTIDOS, SLUG } from '../ayudantes/api_falsa'
import { respuesta } from '../ayudantes/dobles'
import { abrir, avanzar } from '../ayudantes/montaje'

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const SELECTOR_REINTENTAR = '[data-accion="reintentar"]'
const CIENTO_VEINTE_SEGUNDOS = 120_000
const abortada = () => new DOMException('The operation was aborted.', 'AbortError')

describe('T-20 (vistas): 404, 409 y errores sin reintento automático (R-40)', () => {
  it('un 404 muestra la vista de «no encontrado»', async () => {
    instalarApiFalsa({ [PARTIDOS]: () => respuesta(404) })

    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    expect(app.envoltorio.get('[data-estado]').attributes('data-estado')).toBe('no-encontrado')
    expect(app.envoltorio.text()).toContain('No encontrado')
    expect(app.envoltorio.find('table').exists()).toBe(false)
  })

  it.each([
    ['las posiciones', `/${SLUG}/torneos/HUMO-T2/fases/1/posiciones`],
    ['la valla', `/${SLUG}/torneos/HUMO-T2/valla`],
  ])('un 409 configuracion_pendiente en %s dice que la tabla espera la configuración, sin tabla parcial', async (_nombre, ruta) => {
    instalarApiFalsa({ [`/${SLUG}/torneos/HUMO-T2/valla`]: () => respuesta(409, fixture('S009-04')) })

    const app = await abrir(ruta, { temporizadoresFalsos: true })

    expect(app.envoltorio.get('[data-estado]').attributes('data-estado')).toBe('configuracion-pendiente')
    expect(app.envoltorio.text()).toContain('espera la configuración de la liga')
    expect(app.envoltorio.find('table').exists()).toBe(false)
  })

  it.each([
    ['un 500', () => respuesta(500)],
    ['un fallo de red', () => Promise.reject(new TypeError('Failed to fetch')) as unknown as Response],
  ])('%s da un mensaje genérico y un botón manual, sin ninguna petición automática', async (_nombre, manejador) => {
    let pedidos = 0
    instalarApiFalsa({ [PARTIDOS]: () => (pedidos++ === 0 ? manejador() : respuesta(200, fixture('S011-02'))) })

    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    expect(app.envoltorio.get('[data-estado]').attributes('data-estado')).toBe('error')
    expect(app.envoltorio.text()).toContain('No pudimos cargar la información')
    await avanzar(CIENTO_VEINTE_SEGUNDOS)
    expect(pedidos).toBe(1)

    await app.envoltorio.get(SELECTOR_REINTENTAR).trigger('click')
    await avanzar(0)
    expect(pedidos).toBe(2)
    expect(app.envoltorio.find('table').exists()).toBe(true)
  })
})

describe('T-20 (vistas): carga, aviso a los 10 s y tiempo límite de 30 s (R-41, C-19)', () => {
  /** Los partidos nunca responden por sí mismos; el resto de la API responde. La señal de cada petición se guarda. */
  function apiConPartidosColgados() {
    const senales: AbortSignal[] = []
    const api = instalarApiFalsa({
      [PARTIDOS]: (opciones) => {
        const senal = opciones.signal as AbortSignal
        senales.push(senal)
        return new Promise<Response>((_resolver, rechazar) => senal.addEventListener('abort', () => rechazar(abortada())))
      },
    })
    return { ...api, senales, pedidos: () => api.rutasLlamadas().filter((r) => r === PARTIDOS).length }
  }

  it('a los 9,9 s solo «cargando»; a los 10 s el aviso; a los 30 s la señal se aborta y sale el error de tiempo agotado con «Reintentar»', async () => {
    const { senales, pedidos } = apiConPartidosColgados()
    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })
    expect(app.envoltorio.get('[data-estado]').attributes('data-estado')).toBe('cargando')

    await avanzar(9_900)
    expect(app.envoltorio.text()).toContain('Cargando')
    expect(app.envoltorio.text()).not.toContain('El servidor puede estar arrancando')
    await avanzar(100)
    expect(app.envoltorio.text()).toContain('El servidor puede estar arrancando')

    await avanzar(19_900)
    expect(senales[0]?.aborted).toBe(false)
    await avanzar(100)

    expect(senales[0]?.aborted).toBe(true)
    expect(app.envoltorio.get('[data-estado]').attributes('data-estado')).toBe('tiempo-agotado')
    expect(app.envoltorio.text()).toContain('La solicitud tardó demasiado')
    expect(app.envoltorio.text()).not.toContain('No pudimos cargar la información')
    expect(app.envoltorio.find(SELECTOR_REINTENTAR).exists()).toBe(true)
    expect(app.almacen.datosDe(PARTIDOS)).toBeUndefined()

    await avanzar(CIENTO_VEINTE_SEGUNDOS)
    expect(pedidos()).toBe(1)

    await app.envoltorio.get(SELECTOR_REINTENTAR).trigger('click')
    await avanzar(0)
    expect(pedidos()).toBe(2)
    expect(senales[1]).not.toBe(senales[0])
    expect(senales[1]?.aborted).toBe(false)
  })

  it('una respuesta que llega a los 29,9 s se muestra con normalidad y no se aborta nada', async () => {
    const senales: AbortSignal[] = []
    instalarApiFalsa({
      [PARTIDOS]: (opciones) => {
        senales.push(opciones.signal as AbortSignal)
        return new Promise<Response>((resolver) => setTimeout(() => resolver(respuesta(200, fixture('S011-02'))), 29_900))
      },
    })
    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    await avanzar(29_900)

    expect(app.envoltorio.find('table').exists()).toBe(true)
    expect(app.envoltorio.text()).not.toContain('La solicitud tardó demasiado')
    await avanzar(CIENTO_VEINTE_SEGUNDOS)
    expect(senales[0]?.aborted).toBe(false)
  })

  /** Cabeceras ya recibidas, lectura del cuerpo pendiente y atada a la misma señal. */
  function apiConCuerpoPendiente() {
    const senales: AbortSignal[] = []
    let terminar: (texto: string) => void = () => {}
    const api = instalarApiFalsa({
      [PARTIDOS]: (opciones) => {
        const senal = opciones.signal as AbortSignal
        senales.push(senal)
        const cuerpo = new Promise<string>((resolver, rechazar) => {
          terminar = resolver
          senal.addEventListener('abort', () => rechazar(abortada()))
        })
        return { status: 200, headers: new Headers(), text: () => cuerpo } as unknown as Response
      },
    })
    return { ...api, senales, terminarCuerpo: (texto: string) => terminar(texto), pedidos: () => api.rutasLlamadas().filter((r) => r === PARTIDOS).length }
  }

  it('el límite cubre la lectura del cuerpo: a los 30 s el cuerpo pendiente se aborta, sale el tiempo agotado y no se guarda nada', async () => {
    const { senales, pedidos } = apiConCuerpoPendiente()
    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    await avanzar(30_000)

    expect(senales[0]?.aborted).toBe(true)
    expect(app.envoltorio.get('[data-estado]').attributes('data-estado')).toBe('tiempo-agotado')
    expect(app.almacen.datosDe(PARTIDOS)).toBeUndefined()
    await avanzar(CIENTO_VEINTE_SEGUNDOS)
    expect(pedidos()).toBe(1)
  })

  it('control: un cuerpo que termina a los 29,9 s se muestra con normalidad', async () => {
    const { senales, terminarCuerpo } = apiConCuerpoPendiente()
    const app = await abrir(PARTIDOS, { temporizadoresFalsos: true })

    await avanzar(29_900)
    terminarCuerpo(JSON.stringify(fixture('S011-02')))
    await avanzar(0)

    expect(app.envoltorio.find('table').exists()).toBe(true)
    expect(senales[0]?.aborted).toBe(false)
  })
})
