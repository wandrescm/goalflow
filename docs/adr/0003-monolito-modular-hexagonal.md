# ADR-03: Monolito modular con arquitectura Clean/Hexagonal

- **Estado:** Aceptado
- **Fecha:** 2026-10-08 (S2D1)
- **Relacionados:** ADR-07 (runtime), ADR-02 (pipeline post-guardado, se detalla en S2D5)

## Contexto

GoalFlow lo construye y opera una sola persona y se despliega como un único proceso con memoria limitada (512 MB). Sus capacidades (torneos, estadísticas, pollas, IA) cambian a ritmos distintos y no deben acoplarse por accidente. El dominio debe poder probarse sin infraestructura.

## Decisión

1. **Eje principal: módulos por capacidad.** Tenancy, Torneos, Estadisticas, Pollas e IA. El eje secundario son las capas.
2. **Cuatro proyectos de producción más pruebas:** `GoalFlow.Domain`, `GoalFlow.Application`, `GoalFlow.Infrastructure`, `GoalFlow.Api` y `GoalFlow.Tests`. Los módulos son namespaces dentro de cada capa: `GoalFlow.<Capa>.<Modulo>` (por ejemplo `GoalFlow.Domain.Torneos`).
3. **Referencias entre proyectos permitidas (las flechas apuntan hacia adentro):**
   - `Api → Application` y `Api → Infrastructure` (la raíz de composición ve ambos).
   - `Infrastructure → Application` (implementa los puertos que se definen en Application).
   - `Application → Domain`.
   - `Domain →` nada.
4. **Puertos y adaptadores.** Los puertos (interfaces) viven en Application; los adaptadores, en Infrastructure. Application no usa `DbContext`. Las lecturas pesadas, como el recálculo de estadísticas desde los partidos, pasan por puertos de lectura propios del caso de uso, no por un repositorio de agregado completo.
5. **Dependencias externas permitidas:**
   - Domain: ninguna (ni EF Core, ni ASP.NET, ni `Microsoft.Extensions.*`).
   - Application: solo Domain y `Microsoft.Extensions.*.Abstractions` (por ejemplo `ILogger`).
6. **Comunicación entre módulos por eventos.** El módulo emisor (por ejemplo Torneos con `PartidoGuardado`) publica el evento a un puerto, no conoce a los consumidores. El evento lleva identificadores (`TenantId`, `PartidoId`, versión) y no datos del partido: el consumidor relee el estado actual, así una corrección o un reintento no procesa datos viejos. Un módulo solo puede depender de otro a través de su sub-namespace `.Contracts` (eventos, DTO de lectura), nunca de sus entidades.
Excepción: Tenancy es el núcleo compartido (Shared Kernel) y puede usarse completo; solo contiene el identificador y el contexto de tenant y no debe crecer más (se revisa en S2D3).
7. **Grafo de módulos permitido (cada fila nueva exige actualizar este ADR):**

   | Módulo | Puede depender de |
   | --- | --- |
   | Tenancy | nada |
   | Torneos | Tenancy |
   | Estadisticas | Tenancy, Torneos.Contracts |
   | Pollas | Tenancy, Torneos.Contracts, Estadisticas.Contracts |
   | IA | Tenancy, Torneos.Contracts, Estadisticas.Contracts |
   Una entrada "X.Contracts" permite solo ese sub-namespace; una entrada "X" (solo Tenancy) permite el módulo completo.

8. **Contexto de tenant.** La abstracción del tenant actual es un puerto en Application. El `TenantId` es dato del mensaje o comando y nunca estado ambiental del hilo: cada trabajo en background crea su propio scope de DI y fija el tenant desde el mensaje (el pipeline no tiene `HttpContext`).
9. **Un solo proceso con memoria acotada.** Toda cola en memoria es acotada (`Channel` con capacidad máxima y política de espera) y toda caché en memoria tiene `SizeLimit`. El proveedor de LLM es remoto: no consume memoria del modelo, pero sí buffers de las respuestas.

## Alternativas descartadas

- **N-capas simple sin módulos:** menos estructura, pero las capacidades se mezclan y la deriva solo se nota cuando ya duele.
- **Un proyecto por capa y por módulo (≈ 20 proyectos):** frontera fuerte impuesta por el compilador, pero el costo de boilerplate no cabe en el plazo de un solo desarrollador.
- **Microservicios:** sobreingeniería para un solo desarrollador y una sola instancia.
- **Repositorio por agregado para todo:** sirve para escrituras, no para lecturas derivadas con forma propia.

## Consecuencias

- **(−)** La frontera entre módulos no la impone el compilador (son namespaces): depende de las pruebas de arquitectura. Si un módulo crece o se vuelve problemático, se extrae a su propio proyecto; los namespaces consistentes hacen que ese paso sea mecánico.
- **(−)** Disciplina extra en eventos y comandos: llevar el tenant y la versión en cada mensaje; los consumidores deben ser idempotentes.
- **(+)** Dominio probable sin infraestructura; puertos de lectura que mantienen a Application desacoplada de EF Core.

## Verificación

Pruebas de arquitectura en `GoalFlow.Tests/Architecture`: reglas de referencias entre capas, dependencias prohibidas en Domain y Application, y regla del grafo de módulos. Cada regla se prueba también contra un *fixture* con una violación deliberada que debe hacerla fallar (canario); sin esa prueba, una regla verde no demuestra nada.
