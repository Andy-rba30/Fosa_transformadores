# Prompt para el instalador de ARBA — añadir el plugin Bloques con foso

Añade al instalador de los plugins ARBA para Revit 2027 el nuevo add-in **Bloques con foso**
(armado de bloques macizos de cimentación con fosos o canaletas: fundaciones de transformador,
bloques de equipo, fosos de bombas). Es el hermano de **Zapatas** (Acero-Zapatas), **Losas**,
**Columnas** y **Muros**: se instala exactamente igual y aparece en el mismo sitio de la cinta.
No crees ningún botón ni pestaña desde el instalador: la cinta la crea el propio add-in al
arrancar Revit.

## Origen

- Repositorio: https://github.com/Andy-rba30/Fosa_transformadores, rama `main`. Lleva el
  **submódulo** `external/ARBA-comun` (código común ARBA, etiqueta `v1.0.0`): clonar con
  `git clone --recurse-submodules …` o, en un clon ya hecho, `git submodule update --init`;
  sin el submódulo no compila.
- Proyecto: `BlockRebar.csproj` (.NET 10, `net10.0-windows`, x64). Compilar con
  `dotnet build -c Release`. La salida está en `bin\Release\net10.0-windows\`. El código común
  se compila dentro de `BlockRebar.dll` (no hay ninguna DLL `Arba.Comun` que instalar).
- Dependencias: además de las DLL de Revit (los paquetes `Nice3point.Revit.Api.*` son solo de
  compilación, no se copian), usa **Clipper2** (`Clipper2Lib.dll`, biblioteca .NET pura), que
  queda en la salida y **hay que instalar junto a `BlockRebar.dll`**.

## Archivos a instalar (por usuario, `%AppData%\Autodesk\Revit\Addins\2027\`)

```
%AppData%\Autodesk\Revit\Addins\2027\BlockRebar.addin                <- del repo (raiz)
%AppData%\Autodesk\Revit\Addins\2027\BlockRebar\BlockRebar.dll       <- de bin\Release\net10.0-windows\
%AppData%\Autodesk\Revit\Addins\2027\BlockRebar\Clipper2Lib.dll      <- de bin\Release\net10.0-windows\
%AppData%\Autodesk\Revit\Addins\2027\BlockRebar\config.json          <- del repo (raiz); NO sobrescribir si ya existe
                                                                        (guarda los valores por defecto del usuario)
```

El manifiesto `BlockRebar.addin` ya trae las rutas relativas `BlockRebar\BlockRebar.dll`, los dos
registros (Application `BlockRebar.RibbonApp` con ClientId `c4e1a7d2-5b86-4f3a-9e0d-7a2c6b1f8d53`
y Command `BlockRebar.ArmarBloqueCommand` con ClientId `e9b3d5f1-2a74-4c68-8d1e-5f0b9c3a7e26`),
`VendorId` LOCAL. No lo modifiques. Si el instalador usa una carpeta común para todos los add-ins
ARBA en lugar de una por plugin, ajusta solo la etiqueta `<Assembly>` del manifiesto para que
apunte a la ruta real de la DLL. `config.json` y `Clipper2Lib.dll` tienen que quedar siempre en
la misma carpeta que `BlockRebar.dll`.

## Dónde aparece en Revit

Pestaña **ARBA** > panel **Acero** > desplegable **Acero** > botón **Bloques con foso** (nombre
interno `ARBA_Acero_Bloques`), junto a **Zapatas**, **Cimientos**, **Vigas**, **Columnas**,
**Losas** y **Muro de contencion**. Todos llevan la misma clase `ArbaRibbon` del código común
ARBA-comun: cada uno crea la pestaña ARBA y los paneles IA / Acero / Metrados / Encofrado si no
existen (en ese orden) y añade su botón al desplegable "Acero" del panel "Acero", así que da
igual cuál cargue primero (y conviven add-ins con y sin el contrato durante la integración).
Si se desinstala Bloques con foso, solo hay que borrar `BlockRebar.addin` y la carpeta
`BlockRebar\`; el desplegable sigue con los demás botones.

## Archivos que genera el plugin en uso

- `%AppData%\Autodesk\Revit\Addins\2027\BlockRebar\Rejilla ARBA.rfa`: la familia de rejilla que crea el botón
  "Crear familia de rejilla" (se guarda junto a la DLL y se carga en el proyecto). No hace falta
  instalarla; si el instalador encuentra una de una versión anterior, puede dejarla.
- `%Temp%\BlockRebar.log`: registro de cada ejecución.
- En el **proyecto** de Revit (no en disco): al armar o colocar rejillas crea los parámetros
  compartidos del contrato ARBA (`ARBA - Origen`, `ARBA - Código`, `ARBA - Anfitrión`,
  `Metrado - Partida`, `Metrado - Material`, `Metrado - Peso (kg)`, `Metrado - Pernos (und)`,
  `Metrado - Elemento`; GUID fijos, grupo Datos) desde un archivo temporal que se borra; el
  archivo de parámetros compartidos del usuario no cambia. No hay que instalar ningún `.txt`.

## Comprobación tras instalar

1. Abrir Revit 2027.2 y aceptar la carga del add-in (si pide confirmación por el VendorId).
2. En la pestaña ARBA, panel Acero, desplegable Acero debe estar **Bloques con foso** con su
   icono (bloque en sección con dos fosos, malla inferior y barras en L). También aparece en
   Complementos > Herramientas externas.
3. Seleccionar una cimentación estructural de hormigón con foso y pulsar el botón: se abre la
   ventana "Armar bloques con foso" con la lámina (planta + secciones A-A y B-B).
4. El registro de cada ejecución queda en `%Temp%\BlockRebar.log`.

## Desinstalación

Borrar `%AppData%\Autodesk\Revit\Addins\2027\BlockRebar.addin` y la carpeta
`%AppData%\Autodesk\Revit\Addins\2027\BlockRebar\`.
