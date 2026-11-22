import type { Marcador, PartidoDeLaLista } from '../api/tipos'
import { fechaHoraEnBogota } from './fechas'

/** Lo que se muestra de un partido según su estado (Spec 011 R-42, C-22, A1): el frontend solo elige el campo, nunca calcula un marcador. */
export interface TextoDeResultado {
  principal: string | null
  etiqueta: string | null
}

const marcador = (m: Marcador | null): string | null => (m === null ? null : `${m.local} – ${m.visitante}`)

const ETIQUETAS_DE_ADJUDICACION: Record<string, string> = {
  WalkOver: 'Resultado oficial: W.O.',
  Sancion: 'Resultado oficial por sanción',
  Otro: 'Resultado oficial (otro motivo)',
}

const ETIQUETAS_DE_SUSPENSION: Record<string, string> = {
  Clima: 'Suspendido por clima',
  Luz: 'Suspendido por falta de luz',
  Disturbios: 'Suspendido por disturbios',
  Otro: 'Suspendido (otro motivo)',
}

export function textoDeResultado(
  partido: Pick<PartidoDeLaLista, 'estado' | 'marcador' | 'oficial' | 'motivo' | 'programadoPara'>,
): TextoDeResultado {
  switch (partido.estado) {
    case 'Jugado':
      return { principal: marcador(partido.marcador), etiqueta: null }
    case 'Adjudicado':
      // Con o sin marcador deportivo, un Adjudicado muestra su resultado oficial (C-22).
      return {
        principal: marcador(partido.oficial),
        etiqueta: ETIQUETAS_DE_ADJUDICACION[partido.motivo ?? ''] ?? ETIQUETAS_DE_ADJUDICACION.Otro ?? null,
      }
    case 'Suspendido':
      // Un Suspendido sin marcador no es válido (Spec 006 R-11 b): la UI lo tolera y muestra solo la etiqueta.
      return {
        principal: marcador(partido.marcador),
        etiqueta: ETIQUETAS_DE_SUSPENSION[partido.motivo ?? ''] ?? ETIQUETAS_DE_SUSPENSION.Otro ?? null,
      }
    case 'Programado':
      return { principal: partido.programadoPara === null ? 'Por programar' : fechaHoraEnBogota(partido.programadoPara), etiqueta: null }
    case 'Cancelado':
      return { principal: 'Cancelado', etiqueta: null }
  }
}
