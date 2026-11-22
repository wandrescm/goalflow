import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

export interface Hallazgo {
  archivo: string
  motivo: string
}

/** Las únicas claves de import.meta.env que puede leer el código de src/ (Spec 011 R-47, T-25). */
export const CLAVES_PERMITIDAS: ReadonlySet<string> = new Set(['VITE_API_BASE_URL', 'MODE', 'DEV', 'PROD'])

const PROHIBIDOS: ReadonlyArray<readonly [RegExp, string]> = [
  [/\blocalStorage\b/, 'usa localStorage'],
  [/\bsessionStorage\b/, 'usa sessionStorage'],
  [/document\.cookie/, 'usa document.cookie'],
]

/** Revisa el texto de un archivo de src/: claves de entorno no permitidas, almacenamiento del navegador y cookies. */
export function escanearFuente(archivo: string, texto: string): Hallazgo[] {
  const hallazgos: Hallazgo[] = []
  for (const coincidencia of texto.matchAll(/import\.meta\.env(?:\.([A-Za-z0-9_]+)|\[\s*['"]([^'"]+)['"]\s*\])?/g)) {
    const clave = coincidencia[1] ?? coincidencia[2]
    if (clave === undefined) hallazgos.push({ archivo, motivo: 'lee import.meta.env sin una clave permitida' })
    else if (!CLAVES_PERMITIDAS.has(clave)) hallazgos.push({ archivo, motivo: `lee la clave no permitida ${clave}` })
  }
  for (const [patron, motivo] of PROHIBIDOS) if (patron.test(texto)) hallazgos.push({ archivo, motivo })
  return hallazgos
}

/** Todos los .ts y .vue bajo un directorio, recursivamente, en orden. */
export function archivosFuente(directorio: string): string[] {
  const archivos: string[] = []
  for (const nombre of readdirSync(directorio).sort()) {
    const ruta = join(directorio, nombre)
    if (statSync(ruta).isDirectory()) archivos.push(...archivosFuente(ruta))
    else if (/\.(ts|vue)$/.test(nombre)) archivos.push(ruta)
  }
  return archivos
}

export function leer(ruta: string): string {
  return readFileSync(ruta, 'utf8')
}
