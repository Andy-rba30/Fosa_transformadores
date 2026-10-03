# Prompt para el instalador de ARBA — añadir el plugin Bloques con foso

Añade al instalador de los plugins ARBA para Revit 2027 el nuevo add-in **Bloques con foso**
(armado de bloques macizos de cimentación con fosos o canaletas: fundaciones de transformador,
bloques de equipo, fosos de bombas). Es el hermano de **Zapatas** (Acero-Zapatas), **Losas**,
**Columnas** y **Muros**: se instala exactamente igual y aparece en el mismo sitio de la cinta.
No crees ningún botón ni pestaña desde el instalador: la cinta la crea el propio add-in al
arrancar Revit.

## Origen

- Repositorio: https://github.com/Andy-rba30/Fosa_transformadores, rama
  `claude/ecstatic-ptolemy-cehcro` (o `main` cuando se fusione).
- Proyecto: `BlockRebar.csproj` (.NET 10, `net10.0-windows`, x64). Compilar con
  `dotnet build -c Release`. La salida está en `bin\Release\net10.0-windows\`.
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

Pestaña **ARBA** > panel **Acero** > desplegable **Acero** > botón **Bloques con foso**, junto a
**Zapatas**, **Columnas**, **Muros** y **Losas**. Todos llevan la misma clase `ArbaRibbon`: cada
uno crea la pestaña ARBA y los paneles IA / Acero / Encofrado si no existen (en ese orden) y
añade su botón al desplegable "Acero" del panel "Acero", así que da igual cuál cargue primero.
Si se desinstala Bloques con foso, solo hay que borrar `BlockRebar.addin` y la carpeta
`BlockRebar\`; el desplegable sigue con los demás botones.

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
