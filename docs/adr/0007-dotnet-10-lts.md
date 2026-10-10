# ADR-07: Runtime .NET 10 (LTS)

- **Estado:** Aceptado
- **Fecha:** 2026-10-08 (S2D1)
- **Relacionados:** ADR-03

## Contexto

Se inicia un proyecto nuevo con salida a producción prevista dentro de pocas semanas. Microsoft anunció que .NET 8 (LTS) y .NET 9 (STS) terminan su soporte el 10 de noviembre de 2026. .NET 10 es LTS y tiene soporte hasta noviembre de 2028 (la página de política de soporte indica el 14 de noviembre de 2028). Datos verificados el 2026-10-08 en la página oficial de política de soporte de .NET y en el blog de .NET; confirmar de nuevo antes de citarlos en un documento externo.

## Decisión

1. El runtime y el SDK son **.NET 10 (LTS)**; todos los proyectos usan `net10.0`.
2. El SDK se fija en `global.json` con la versión `10.0.401`. La política `rollForward` pasa de `latestFeature` a **`latestPatch`**: con `latestFeature`, una máquina con una banda de SDK más nueva compila con un SDK distinto al de CI y los resultados pueden divergir. Actualizar de banda de SDK es un cambio explícito y revisado.
3. **Compatibilidad del stack con .NET 10, verificada hasta hoy:**
   - Npgsql EF Core provider: existen versiones 10.0.x, que dependen de EF Core 10.
   - NetArchTest.Rules 1.3.2 verificado en S2D1 (compila y corre en net10.0). actions/checkout@v7 y actions/setup-dotnet@v6 verificados por la primera ejecución de CI.
   - **Pendiente de verificar en la sesión que las introduce:** Polly (S2D5), JwtBearer (S2D3) e imagen base de Docker (S2D2).
4. Una dependencia no se agrega al proyecto sin comprobar su compatibilidad con .NET 10 y registrarla en el ADR de la sesión correspondiente.

## Alternativas descartadas

- **.NET 8 o .NET 9:** perderían soporte semanas después del lanzamiento y generarían deuda de actualización desde el primer día.
- **Esperar a la siguiente versión:** no hay beneficio; .NET 10 tiene la ventana de soporte más larga disponible.

## Consecuencias

- **(+)** Ventana de soporte hasta 2028 sin migración obligatoria durante la vida útil prevista de la v1.
- **(−)** Algunas bibliotecas de terceros pueden tardar en soportar .NET 10; mitigación: comprobar cada dependencia antes de incorporarla.
- **(−)** El SDK se actualiza solo por decisión explícita (cambio en `global.json`), no automáticamente.

## Verificación

`dotnet --list-sdks` muestra la versión fijada, `dotnet build` y `dotnet test` pasan en local y en CI, y CI usa el SDK declarado en `global.json`. Reevaluar la versión antes de noviembre de 2028.
