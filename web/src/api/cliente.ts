import { urlBaseDeLaApi } from './configuracion'

/** El resultado de una petición a la API (Spec 011 R-36, R-39 a R-41). */
export type Resultado<T> =
  | { tipo: 'ok'; datos: T }
  | { tipo: 'no-encontrado' }
  | { tipo: 'configuracion-pendiente'; faltantes: string[] }
  | { tipo: 'ocupado'; reintentarEn: number | null }
  | { tipo: 'tiempo-agotado' }
  | { tipo: 'error' }

export interface OpcionesDePeticion {
  /** Se llama a los 10 s sin respuesta completa: la vista avisa que el servidor puede estar arrancando (R-41). */
  alTardar?: () => void
}

const AVISO_MS = 10_000
const LIMITE_MS = 30_000

/** Segundos enteros de Retry-After; cualquier otra cosa (ausente, texto, fecha HTTP, decimal, negativo) es null: no se inventa una espera (R-39). */
function segundosDeEspera(valor: string | null): number | null {
  return valor !== null && /^\d+$/.test(valor) ? Number(valor) : null
}

async function interpretar<T>(respuesta: Response): Promise<Resultado<T>> {
  // El límite cubre hasta el final del cuerpo (R-41): el cuerpo se lee siempre, también en los errores, con la misma señal.
  const texto = await respuesta.text()

  if (respuesta.status === 200) return { tipo: 'ok', datos: JSON.parse(texto) as T }
  if (respuesta.status === 404) return { tipo: 'no-encontrado' }
  if (respuesta.status === 429) return { tipo: 'ocupado', reintentarEn: segundosDeEspera(respuesta.headers.get('Retry-After')) }
  if (respuesta.status === 409) {
    const cuerpo = JSON.parse(texto) as { codigo?: unknown; faltantes?: unknown }
    if (cuerpo.codigo === 'configuracion_pendiente') {
      const faltantes = Array.isArray(cuerpo.faltantes) ? cuerpo.faltantes.filter((f): f is string => typeof f === 'string') : []
      return { tipo: 'configuracion-pendiente', faltantes }
    }
  }
  return { tipo: 'error' }
}

/**
 * Un GET a la API (Spec 011 R-36): sin credenciales y sin más cabecera que Accept, de modo que el navegador no mande preflight. Un
 * AbortController por petición: aviso a los 10 s y aborto a los 30 s, que cubren hasta el final del cuerpo. Nunca reintenta (R-39, R-40, R-41).
 */
export async function pedir<T>(ruta: string, opciones: OpcionesDePeticion = {}): Promise<Resultado<T>> {
  const controlador = new AbortController()
  let agotado = false
  const aviso = setTimeout(() => opciones.alTardar?.(), AVISO_MS)
  const limite = setTimeout(() => {
    agotado = true
    controlador.abort()
  }, LIMITE_MS)

  try {
    const respuesta = await fetch(urlBaseDeLaApi() + ruta, {
      method: 'GET',
      credentials: 'omit',
      headers: { Accept: 'application/json' },
      signal: controlador.signal,
    })
    return await interpretar<T>(respuesta)
  } catch {
    return agotado ? { tipo: 'tiempo-agotado' } : { tipo: 'error' }
  } finally {
    clearTimeout(aviso)
    clearTimeout(limite)
  }
}
