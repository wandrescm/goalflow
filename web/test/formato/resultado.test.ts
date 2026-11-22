import { describe, expect, it } from 'vitest'
import type { PartidoDeLaLista } from '../../src/api/tipos'
import { textoDeResultado } from '../../src/formato/resultado'

type Entrada = Pick<PartidoDeLaLista, 'estado' | 'marcador' | 'oficial' | 'motivo' | 'programadoPara'>

const base: Entrada = { estado: 'Jugado', marcador: null, oficial: null, motivo: null, programadoPara: null }

describe('T-21: el texto de cada estado y el campo que se elige (R-42, C-22, A1)', () => {
  it('Jugado muestra el marcador, sin etiqueta', () => {
    expect(textoDeResultado({ ...base, marcador: { local: 2, visitante: 1 } })).toEqual({ principal: '2 – 1', etiqueta: null })
  })

  it.each([
    ['WalkOver', 'Resultado oficial: W.O.'],
    ['Sancion', 'Resultado oficial por sanción'],
    ['Otro', 'Resultado oficial (otro motivo)'],
  ] as const)('Adjudicado por %s muestra el oficial y la etiqueta «%s», con y sin marcador deportivo', (motivo, etiqueta) => {
    const oficial = { local: 3, visitante: 0 }

    expect(textoDeResultado({ ...base, estado: 'Adjudicado', oficial, motivo })).toEqual({ principal: '3 – 0', etiqueta })
    // Con marcador deportivo también se muestra el oficial: el frontend solo elige el campo (C-22).
    expect(textoDeResultado({ ...base, estado: 'Adjudicado', oficial, motivo, marcador: { local: 2, visitante: 1 } })).toEqual({
      principal: '3 – 0',
      etiqueta,
    })
  })

  it.each([
    ['Clima', 'Suspendido por clima'],
    ['Luz', 'Suspendido por falta de luz'],
    ['Disturbios', 'Suspendido por disturbios'],
    ['Otro', 'Suspendido (otro motivo)'],
  ] as const)('Suspendido por %s muestra el marcador parcial y la etiqueta «%s»', (motivo, etiqueta) => {
    expect(textoDeResultado({ ...base, estado: 'Suspendido', marcador: { local: 1, visitante: 0 }, motivo })).toEqual({
      principal: '1 – 0',
      etiqueta,
    })
  })

  it('RESPUESTA ANÓMALA (no es un caso válido de Spec 006 R-11 b): un Suspendido sin marcador muestra solo la etiqueta y no inventa un marcador', () => {
    expect(textoDeResultado({ ...base, estado: 'Suspendido', motivo: 'Clima' })).toEqual({
      principal: null,
      etiqueta: 'Suspendido por clima',
    })
  })

  it('Programado sin hora dice «Por programar»', () => {
    expect(textoDeResultado({ ...base, estado: 'Programado' })).toEqual({ principal: 'Por programar', etiqueta: null })
  })

  it('Programado con hora muestra la fecha y la hora de Bogotá, nunca las del equipo', () => {
    // La zona del proceso de pruebas es Asia/Tokyo: 2026-11-21T01:00:00Z es 21/11 10:00 allí y 20/11 20:00 en Bogotá.
    expect(textoDeResultado({ ...base, estado: 'Programado', programadoPara: '2026-11-21T01:00:00Z' })).toEqual({
      principal: '20/11/2026 20:00',
      etiqueta: null,
    })
  })

  it('Cancelado dice «Cancelado»', () => {
    expect(textoDeResultado({ ...base, estado: 'Cancelado' })).toEqual({ principal: 'Cancelado', etiqueta: null })
  })

  it('un estado con marcador nulo no produce «null – null»: nunca se calcula ni se inventa un marcador', () => {
    for (const estado of ['Jugado', 'Adjudicado', 'Suspendido'] as const) {
      const { principal } = textoDeResultado({ ...base, estado })
      expect(principal === null || !principal.includes('null')).toBe(true)
    }
  })
})
