import { vi } from 'vitest'

export const URL_BASE = 'https://api.goalflow.test'

export interface LlamadaDeFetch {
  url: string
  opciones: RequestInit
}

/** Una respuesta de la API con cuerpo JSON (o sin cuerpo). */
export function respuesta(estado: number, cuerpo?: unknown, cabeceras: Record<string, string> = {}): Response {
  return new Response(cuerpo === undefined ? null : JSON.stringify(cuerpo), { status: estado, headers: cabeceras })
}

/** Instala un doble de fetch que cuenta y registra cada llamada. Devuelve las llamadas y el doble. */
export function instalarFetch(manejador: (url: string, opciones: RequestInit) => Promise<Response> | Response) {
  const llamadas: LlamadaDeFetch[] = []
  const doble = vi.fn((entrada: string | URL | Request, opciones: RequestInit = {}) => {
    const url = String(entrada)
    llamadas.push({ url, opciones })
    return Promise.resolve(manejador(url, opciones))
  })
  vi.stubGlobal('fetch', doble)
  return { llamadas, doble }
}

function abortada(): DOMException {
  return new DOMException('The operation was aborted.', 'AbortError')
}

/**
 * El doble de timeout de la Spec 011 T-20: nunca resuelve por sí mismo, observa la señal que recibe y, al abortarse, rechaza su promesa
 * con un DOMException AbortError, como el fetch nativo.
 */
export function instalarFetchQueNuncaResuelve() {
  const senales: AbortSignal[] = []
  const { llamadas, doble } = instalarFetch((_url, opciones) => {
    const senal = opciones.signal as AbortSignal
    senales.push(senal)
    return new Promise<Response>((_resolver, rechazar) => {
      senal.addEventListener('abort', () => rechazar(abortada()))
    })
  })
  return { llamadas, doble, senales }
}

/**
 * Un fetch que resuelve de inmediato con una 200 cuyas cabeceras ya llegaron, pero cuya lectura del cuerpo queda pendiente: observa la misma
 * señal y rechaza con AbortError al abortarse. `terminarCuerpo` permite terminarla con un cuerpo (el control de 29,9 s).
 */
export function instalarFetchConCuerpoPendiente() {
  const senales: AbortSignal[] = []
  let terminar: (texto: string) => void = () => {}
  const { llamadas, doble } = instalarFetch((_url, opciones) => {
    const senal = opciones.signal as AbortSignal
    senales.push(senal)
    const cuerpo = new Promise<string>((resolver, rechazar) => {
      terminar = resolver
      senal.addEventListener('abort', () => rechazar(abortada()))
    })
    return {
      ok: true,
      status: 200,
      headers: new Headers(),
      text: () => cuerpo,
      json: async () => JSON.parse(await cuerpo) as unknown,
    } as unknown as Response
  })
  return { llamadas, doble, senales, terminarCuerpo: (texto: string) => terminar(texto) }
}
