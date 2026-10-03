# Notas para ARBA-comun (desde Fosa_transformadores / BlockRebar)

Lo que faltó o conviene cambiar en el código común al integrar la etiqueta `v1.0.0`. Nada de
esto se ha tocado dentro de `external/ARBA-comun`; el add-in lo resuelve por su lado donde se
indica. El cambio, si procede, se hace en ARBA-comun con su versión.

## 1. `Arba.Comun.props` y el globbing por defecto del SDK (duplicados CS2002)

Con `Microsoft.NET.Sdk`, el globbing por defecto (`**/*.cs`) ya incluye `external/ARBA-comun/**`
cuando el submódulo está dentro de la carpeta del proyecto: el `Import` del `.props` añade otra vez
`src/**/*.cs` (13 avisos `CS2002: Source file ... specified multiple times`) y además entran
`tests/Program.cs` y `build/CheckUsage.cs` en la DLL del add-in. `INTEGRACION.md` §2 no lo
menciona.

- Solución en el add-in: `<Compile Remove="external\**" /> <None Remove="external\**" />` en un
  `ItemGroup` **antes** del `Import` (después del `Import` quitaría también lo que añade el
  `.props`).
- Propuesta para el común (PATCH): que el `.props` ponga
  `<DefaultItemExcludes>$(DefaultItemExcludes);$(ArbaComunDir)**</DefaultItemExcludes>` o que
  `INTEGRACION.md` documente el `Remove` antes del `Import`.

## 2. `PartitionName.Source` no tiene comodín para la capa (u / v)

La `PartitionName` propia de Bloques tenía `Layer` y el comodín `{capa}`; en el común `{capa}`
es alias de `{codigo}`. La capa u/v de las mallas (F1, F2, F3) ya no se puede poner en la
partición: por defecto solo va F# (`CIMIENTOS - BLQ-FT-01-F1`), que es lo que pide el contrato.
Si algún usuario la quiere, haría falta un comodín nuevo (MINOR) o escribirla en `ARBA - Código`
(`F1-u`), lo que cambiaría el valor `F#` que espera el plugin de metrados.

## 3. `ArbaOrigin.Delete` borra armaduras y misceláneos a la vez

En Bloques "Borrar armado del plugin" y "Borrar rejillas y ángulos del plugin" son botones
distintos, así que no se usa `ArbaOrigin.Delete(doc, prefijo, host)` (borraría también las
rejillas y los ángulos del bloque): el add-in recorre `ArbaOrigin.Find` filtrando con
`ArbaPartition.IsRebar` y borra cada grupo por separado. Sería cómodo un parámetro opcional
`onlyRebar` / `onlyNonRebar` en `Delete` (MINOR).

## 4. `ArbaMigration.HasLegacy` / `RebarOf` recorren todo el modelo por anfitrión

`RebarOf` hace tres `FilteredElementCollector` (Rebar, RebarInSystem, FabricSheet) y compara el
anfitrión de cada armadura; con muchos bloques seleccionados se repite por cada uno. Para
`Rebar` se podría usar `RebarHostData.GetRebarsInHost(host)` (es lo que hace el respaldo por
comentario del add-in). No bloquea: la selección habitual son pocos bloques.

## 5. Avisos de `ArbaSharedParams.Ensure` en documentos de familia

`EnsureAll` devuelve false y avisa en un documento de familia; Bloques solo corre en proyectos
(selección de cimentaciones estructurales), así que no se da. Se anota para que el aviso no se
tome como error en el informe si alguien ejecuta el comando desde el editor de familias.
