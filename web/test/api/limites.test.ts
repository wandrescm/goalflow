import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { pedir } from '../../src/api/cliente'
import { instalarFetch, instalarFetchConCuerpoPendiente, instalarFetchQueNuncaResuelve, respuesta } from '../ayudantes/dobles'

beforeEach(() => vi.useFakeTimers())
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const RUTA = '/liga-humo/torneos'
const CIENTO_VEINTE_SEGUNDOS = 120_000

describe('T-19 (cliente): 429 y Retry-After (R-39)', () => {
  it('un 429 con Retry-After 7 da ocupado con 7 segundos y no hay ninguna petición más en 120 s simulados', async () => {
    const { doble } = instalarFetch(() => respuesta(429, { error: 'limite_de_frecuencia' }, { 'Retry-After': '7' }))

    const resultado = await pedir(RUTA)
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)

    expect(resultado).toEqual({ tipo: 'ocupado', reintentarEn: 7 })
    expect(doble).toHaveBeenCalledTimes(1)
  })

  it.each([
    ['ausente', undefined],
    ['abc', 'abc'],
    ['una fecha HTTP', 'Wed, 21 Nov 2026 07:28:00 GMT'],
    ['decimal', '1.5'],
    ['negativo', '-3'],
  ])('Retry-After %s: no se inventa una espera (reintentarEn es null)', async (_nombre, valor) => {
    instalarFetch(() => respuesta(429, undefined, valor === undefined ? {} : { 'Retry-After': valor }))

    expect(await pedir(RUTA)).toEqual({ tipo: 'ocupado', reintentarEn: null })
  })

  it('Retry-After 0 es un entero válido', async () => {
    instalarFetch(() => respuesta(429, undefined, { 'Retry-After': '0' }))

    expect(await pedir(RUTA)).toEqual({ tipo: 'ocupado', reintentarEn: 0 })
  })
})

describe('T-20 (cliente): errores sin reintento automático (R-40)', () => {
  it.each([
    ['404', () => respuesta(404), { tipo: 'no-encontrado' }],
    [
      '409 configuracion_pendiente',
      () => respuesta(409, { codigo: 'configuracion_pendiente', faltantes: ['puntos', 'desempate'] }),
      { tipo: 'configuracion-pendiente', faltantes: ['puntos', 'desempate'] },
    ],
    ['409 de otro tipo', () => respuesta(409, { codigo: 'otra_cosa' }), { tipo: 'error' }],
    ['500', () => respuesta(500), { tipo: 'error' }],
    ['200 con un cuerpo que no es JSON', () => new Response('no es json', { status: 200 }), { tipo: 'error' }],
  ])('%s', async (_nombre, manejador, esperado) => {
    const { doble } = instalarFetch(manejador)

    const resultado = await pedir(RUTA)
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)

    expect(resultado).toEqual(esperado)
    expect(doble).toHaveBeenCalledTimes(1)
  })

  it('un fallo de red da error genérico y no hay ninguna petición más', async () => {
    const doble = vi.fn(() => Promise.reject(new TypeError('Failed to fetch')))
    vi.stubGlobal('fetch', doble)

    const resultado = await pedir(RUTA)
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)

    expect(resultado).toEqual({ tipo: 'error' })
    expect(doble).toHaveBeenCalledTimes(1)
  })

  it('una respuesta 200 con datos da ok con el cuerpo', async () => {
    instalarFetch(() => respuesta(200, { liga: { slug: 'liga-humo' } }))

    expect(await pedir(RUTA)).toEqual({ tipo: 'ok', datos: { liga: { slug: 'liga-humo' } } })
  })
})

describe('T-20 (cliente): tiempo límite de 30 s con aviso a los 10 s (R-41, C-19)', () => {
  it('a los 9,9 s no hay aviso; a los 10 s sí; a los 30 s la señal se aborta y el resultado es tiempo-agotado, no error', async () => {
    const { doble, senales } = instalarFetchQueNuncaResuelve()
    const alTardar = vi.fn()

    const promesa = pedir(RUTA, { alTardar })
    await vi.advanceTimersByTimeAsync(9_900)
    expect(alTardar).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(100)
    expect(alTardar).toHaveBeenCalledTimes(1)
    expect(senales[0]?.aborted).toBe(false)

    await vi.advanceTimersByTimeAsync(19_900)
    expect(senales[0]?.aborted).toBe(false) // a los 29,9 s todavía no
    await vi.advanceTimersByTimeAsync(100)

    expect(senales[0]?.aborted).toBe(true)
    expect(await promesa).toEqual({ tipo: 'tiempo-agotado' })
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)
    expect(doble).toHaveBeenCalledTimes(1)
  })

  it('una respuesta que llega a los 29,9 s se devuelve con normalidad y no se aborta nada', async () => {
    let senal: AbortSignal | undefined
    const { doble } = instalarFetch((_url, opciones) => {
      senal = opciones.signal as AbortSignal
      return new Promise<Response>((resolver) => setTimeout(() => resolver(respuesta(200, { a: 1 })), 29_900))
    })

    const promesa = pedir(RUTA)
    await vi.advanceTimersByTimeAsync(29_900)

    expect(await promesa).toEqual({ tipo: 'ok', datos: { a: 1 } })
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)
    expect(senal?.aborted).toBe(false)
    expect(doble).toHaveBeenCalledTimes(1)
  })

  it('el límite cubre la lectura del cuerpo: cabeceras ya recibidas y cuerpo pendiente a los 30 s dan tiempo-agotado', async () => {
    const { doble, senales } = instalarFetchConCuerpoPendiente()

    const promesa = pedir(RUTA)
    await vi.advanceTimersByTimeAsync(30_000)

    expect(senales[0]?.aborted).toBe(true)
    expect(await promesa).toEqual({ tipo: 'tiempo-agotado' })
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)
    expect(doble).toHaveBeenCalledTimes(1)
  })

  it('control: un cuerpo que termina a los 29,9 s se muestra con normalidad', async () => {
    const { senales, terminarCuerpo } = instalarFetchConCuerpoPendiente()

    const promesa = pedir(RUTA)
    await vi.advanceTimersByTimeAsync(29_900)
    terminarCuerpo(JSON.stringify({ a: 1 }))

    expect(await promesa).toEqual({ tipo: 'ok', datos: { a: 1 } })
    await vi.advanceTimersByTimeAsync(CIENTO_VEINTE_SEGUNDOS)
    expect(senales[0]?.aborted).toBe(false)
  })
})
