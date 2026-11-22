// @vitest-environment node
import { spawnSync } from 'node:child_process'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { archivosFuente, escanearFuente, leer } from '../ayudantes/escaner'

const raiz = fileURLToPath(new URL('../..', import.meta.url))
const vite = join(raiz, 'node_modules', 'vite', 'bin', 'vite.js')

/** Un build de producción en un proceso hijo, con el entorno controlado por la prueba y sin la variable heredada. */
function construir(variable: string | undefined) {
  const entorno: NodeJS.ProcessEnv = { ...process.env }
  delete entorno.VITE_API_BASE_URL
  if (variable !== undefined) entorno.VITE_API_BASE_URL = variable
  const salida = mkdtempSync(join(tmpdir(), 'goalflow-web-'))
  return spawnSync(process.execPath, [vite, 'build', '--outDir', salida, '--emptyOutDir'], {
    cwd: raiz,
    env: entorno,
    encoding: 'utf8',
  })
}

describe('T-25: el bundle no incrusta secretos ni más entorno que VITE_API_BASE_URL (R-47)', () => {
  it('un build de producción sin VITE_API_BASE_URL falla y nombra la variable', () => {
    const resultado = construir(undefined)
    expect(resultado.status).not.toBe(0)
    expect(`${resultado.stdout}${resultado.stderr}`).toContain('VITE_API_BASE_URL')
  }, 120_000)

  it('un build de producción con http:// falla y nombra la variable', () => {
    const resultado = construir('http://api.goalflow.test')
    expect(resultado.status).not.toBe(0)
    expect(`${resultado.stdout}${resultado.stderr}`).toContain('VITE_API_BASE_URL')
  }, 120_000)

  it('control positivo: con una URL https:// el mismo build termina bien', () => {
    const resultado = construir('https://api.goalflow.test')
    expect(`${resultado.stdout}${resultado.stderr}`).not.toContain('VITE_API_BASE_URL debe')
    expect(resultado.status).toBe(0)
  }, 120_000)

  it('el escaneo estático de src/ no encuentra claves de entorno no permitidas, almacenamiento ni cookies', () => {
    const archivos = archivosFuente(join(raiz, 'src'))
    expect(archivos.length).toBeGreaterThan(0) // antivacío
    const hallazgos = archivos.flatMap((archivo) => escanearFuente(archivo, leer(archivo)))
    expect(hallazgos).toEqual([])
  })

  it('control negativo: el escáner detecta una clave ajena, el objeto entero, el almacenamiento y las cookies', () => {
    const texto = [
      'const a = import.meta.env.SECRETO',
      "const b = import.meta.env['OTRA']",
      'const c = { ...import.meta.env }',
      'localStorage.getItem("x")',
      'sessionStorage.clear()',
      'document.cookie',
    ].join('\n')
    const motivos = escanearFuente('canario.ts', texto).map((h) => h.motivo)
    expect(motivos).toEqual(
      expect.arrayContaining([
        'lee la clave no permitida SECRETO',
        'lee la clave no permitida OTRA',
        'lee import.meta.env sin una clave permitida',
        'usa localStorage',
        'usa sessionStorage',
        'usa document.cookie',
      ]),
    )
    expect(escanearFuente('limpio.ts', 'const u = import.meta.env.VITE_API_BASE_URL; const m = import.meta.env.MODE')).toEqual([])
  })
})
