# GoalFlow — reglas para Claude Code

SaaS multi-tenant para gestión de torneos de fútbol de barrio. Este archivo solo contiene reglas técnicas: el repositorio es público, así que aquí no va estrategia de negocio ni secretos.

## Cómo trabajamos (SDD estricto)

Cada tarea llega como un **handoff** con: contexto y stack, criterios de aceptación, restricciones, tests que debe pasar, archivos que no debes tocar y definición de terminado. El flujo es Spec → Test-First → implementación → auditoría externa.

- Si al handoff le falta algo o la Spec tiene lagunas, **pregunta antes de escribir código**.
- Escribe o ajusta primero los tests que pide la Spec; luego la implementación mínima que los hace pasar.
- **Nunca modifiques ni borres un test para que pase.** Si un test parece incorrecto, detente y explica por qué.
- No agregues funciones fuera de la Spec ni refactorices código ajeno a la tarea.
- Si la tarea exige una decisión de arquitectura sin ADR que la respalde, **detente y devuélvela** para decidirla en el chat de arquitectura.
- No cambies versiones de .NET, SDK ni paquetes sin que el handoff lo pida.
- Antes de decir "terminado", corre `dotnet build` y `dotnet test` y reporta el resultado y los archivos cambiados.
- **Spec primero:** las Specs viven en `docs/specs/` (plantilla en `docs/specs/TEMPLATE.md`) y los ADR en `docs/adr/`. Cualquier cambio de comportamiento actualiza primero la Spec o el ADR correspondiente, en el mismo PR que el código.
- Cada PR incluye en su descripción la tabla de validación (requisito → test → resultado) con resultados reales.
- Commits pequeños con Conventional Commits (feat:, fix:, test:, chore:, docs:). El mensaje de commit y el título de los PR van en inglés.
- Las Specs y los ADR se escriben en inglés.

## Stack

- C# y **.NET 10 (LTS)**, ASP.NET Core Web API, EF Core con Npgsql, xUnit.
- Frontend (más adelante): Vue.js con Composition API, TypeScript, Vite y Pinia; pruebas con Vitest.
- Base de datos: PostgreSQL en Supabase. Autenticación: Supabase Auth (JWT).
- Hosting: Render (backend) y Cloudflare Workers con Static Assets (frontend). CI/CD con GitHub Actions.

## Arquitectura: monolito modular, Clean/Hexagonal

Estructura (ADR-03, en `docs/adr/`):

- `GoalFlow.Domain`: entidades, reglas y eventos. **No depende de nada** (ni EF Core ni ASP.NET).
- `GoalFlow.Application`: casos de uso y puertos (interfaces). Depende solo de Domain.
- `GoalFlow.Infrastructure`: adaptadores (EF Core, caché, proveedores externos). Implementa los puertos.
- `GoalFlow.Api`: controladores finos y raíz de composición (DI). Sin lógica de negocio.
- `GoalFlow.Tests`: unitarias, de integración y **pruebas de arquitectura** (`GoalFlow.Tests/Architecture`) que verifican estas dependencias.
- Los módulos (Tenancy, Ligas, Torneos, Estadisticas, Pollas, IA) son namespaces `GoalFlow.<Capa>.<Modulo>` dentro de cada capa, no proyectos. Un módulo depende de otro solo por su sub-namespace `.Contracts`; Tenancy es el núcleo compartido y puede usarse completo.
- Tenancy contiene solo `TenantId`, `ITenantContext` y la excepción de tenant ausente; no crece. El catálogo de ligas y la membresía de Admins viven en `Ligas` (ADR-14).
- Los módulos se comunican por eventos con identificadores y versión, no con datos del partido. Los trabajos en background reciben el `TenantId` en el mensaje.
- Las reglas de arquitectura no se debilitan ni se borran para que pase un test. Una dependencia nueva entre módulos exige actualizar `TablaDeModulos.cs` y el ADR-03 §7 en el mismo commit.

## Reglas de dominio no negociables

- **Multi-tenant en dos capas (ADR-14):** toda entidad core lleva `TenantId`, tiene el filtro global con nombre `Tenant` de EF Core **y** una política RLS en PostgreSQL, ambas en la misma migración. El filtro referencia un miembro del `DbContext`; nunca captura una variable local.
- **El tenant vive en una transacción:** toda lectura o escritura sobre tablas con tenant corre dentro de una transacción que fija `app.tenant_id` con `set_config(…, true)`. Prohibido fijarlo a nivel de sesión. Sin tenant, `ITenantContext` lanza una excepción y la base devuelve cero filas.
- **Transacción explícita siempre:** toda unidad de trabajo sobre tablas con tenant o sobre `admins_liga` abre su transacción por el puerto de Application. Única excepción: el resolutor de slug lee `ligas` sin transacción ni contexto (ADR-14 §2.3). Un `SaveChanges` sin transacción activa lanza una excepción: EF Core no abre transacción para una sola sentencia y el interceptor no correría.
- **Prohibido `IgnoreQueryFilters`.** No hay endpoints de SuperAdmin.
- **SQL crudo solo en la lista permitida (ADR-14 C-10):** migraciones (políticas y `revoke`), el `set_config` del interceptor de transacciones y las pruebas de aislamiento contra la base desechable de pruebas. Cualquier otro SQL crudo se devuelve al chat de arquitectura.
- **Tablas globales:** `ligas` y `admins_liga` son de solo lectura para `goalflow_app`; nadie las modifica desde la API.
- **Pruebas de aislamiento:** contra PostgreSQL 17 real, conectadas como `goalflow_app`. Un superusuario o el dueño de la tabla se saltan RLS, así que una prueba conectada así no demuestra nada.
- **Datos de prueba inventados** (`liga-a`, `liga-b`); nunca nombres reales de ligas, equipos o personas.
- **Derivar, no acumular:** posiciones, goleadores, valla y puntos de pollas se calculan desde los partidos guardados y se recalculan de forma **idempotente**; guardar dos veces o corregir un partido no duplica nada.
- **Fechas:** guardar siempre en UTC; mostrar en `America/Bogota`. El cierre de pronósticos lo decide el reloj del servidor.
- **Resultados:** solo el Admin de la liga los guarda.
- **Jugadores:** solo mayores de 18; el documento de identidad se cifra por columna y nunca se expone en respuestas públicas ni en logs.
- **Magic link del Capitán:** token aleatorio criptográfico de al menos 256 bits, se guarda solo su hash, con caducidad y un solo uso; una edición posterior responde 403.
- **Dinero:** la plataforma no cobra ni paga nada; los premios en dinero están detrás de una bandera apagada.
- **Base de datos:** la API se conecta como `goalflow_app` por el pooler de Supabase en modo sesión, nunca como `postgres`. Las tablas viven en el schema `goalflow`, las crea `postgres` y `goalflow_app` nunca es dueño de una tabla. La Data API de Supabase está desactivada (ADR-05).
- **Migraciones:** EF Core; las políticas RLS y los `revoke` van dentro de la migración. CI genera el script idempotente como artefacto; el propietario lo aplica como `postgres`. CI nunca se conecta a la base de producción para migrar.

## Estándar de código

- `Nullable` activado y advertencias tratadas como errores; centraliza estas opciones en `Directory.Build.props`.
- Inyección de dependencias por constructor; `async/await` con `CancellationToken` en operaciones de E/S.
- Excepciones específicas, validación de entradas en el borde y registro con `ILogger` en formato estructurado.
- Nunca registres tokens, documentos de identidad ni secretos.

## Seguridad y secretos

- **No subas secretos ni cadenas de conexión al repositorio** (es público). Usa variables de entorno y `dotnet user-secrets` en local.
- CORS restringido a los orígenes del frontend; límite de frecuencia en endpoints públicos.

## Si se implementan funciones de IA

- El proveedor de LLM va detrás de una interfaz; una sola llamada por partido guardado y con tope de costo.
- Un verificador determinista debe comprobar que cada número y nombre del texto generado exista en los datos de entrada antes de publicarlo.
- Toda búsqueda vectorial se filtra por `TenantId`. Trata nombres de equipo y textos de usuarios como datos no confiables (riesgo de inyección de prompts).

## Definición de terminado

Los tests del handoff pasan sin haberse modificado, se cumplen los criterios de aceptación, no hay advertencias nuevas, no hay secretos en el diff y, si cambió una decisión de arquitectura, existe o se actualizó su ADR en `docs/adr/`.

## Esfuerzo y modelo

Por defecto, esfuerzo medio. Sube el esfuerzo o el modelo solo para bugs difíciles; las decisiones de diseño se discuten en el chat de arquitectura, no aquí.
