# Armado automático de bloques con foso — add-in Revit 2027

Genera la armadura de **bloques macizos de cimentación con fosos o canaletas abiertas por
arriba** (fundaciones de transformador con foso perimetral de aceite, bloques de equipo con
canaletas, fosos de bombas) a partir de la **geometría real del elemento**, sin depender de los
nombres de parámetros de la familia: el sólido de la cimentación estructural ya lleva los fosos
recortados y el plugin deduce la base, el tope, los fondos de foso, las plataformas (núcleo)
y los muretes.

Es el hermano del add-in de zapatas ([Acero-Zapatas](https://github.com/Andy-rba30/Acero-Zapatas)):
misma base (lectura del sólido, ventana previa con tema oscuro de Revit, red de seguridad que
deshace el elemento entero si una barra queda fuera del hormigón, `config.json`) y el mismo
botón en la pestaña **ARBA** > panel **Acero** > desplegable **Acero** > **Bloques con foso**.

## Familias de armado (plano IG-01-260275-104-0004-CV-DWG-0003)

| Código | Familia | Dónde |
|--------|---------|-------|
| **F1** | malla inferior (u + v) con patas hacia arriba en los bordes exteriores | cara inferior, en todo el contorno |
| **F2** | malla bajo el fondo de foso (u + v) con patas hacia abajo | 75 mm bajo el fondo más profundo, en todo el contorno o solo bajo los fosos con anclaje |
| **F3** | malla superior de cada plataforma con patas hacia abajo en todos sus bordes | tope de las plataformas |
| **F4** | barra en L (vertical + pie bajo el foso) | caras de foso del lado plataforma |
| **F5** | horizontales por dentro de F4 y de las patas de F3 | caras de foso del lado plataforma |
| **F6** | verticales de altura completa | cara exterior de los muretes |
| **F7** | horquillas en U invertida sobre la corona | muretes |
| **F8** | horizontales de la corona a la base | muretes |

El apilado de capas (recubrimientos + diámetros reales), la regla de esquinas (cada cara es
dueña de su barra de esquina), los desfases para que nada choque y la comprobación de
choques (`ClashCheck`) y de separaciones reales (`CheckSpacing`) están descritos en `PLAN.md`.

## Cómo lee el bloque

1. Toma los sólidos del elemento (cimentación estructural); los despreciables (< 1 % del mayor)
   se ignoran.
2. **zBase** = cara horizontal hacia abajo más baja; **zTope** = cara horizontal hacia arriba más
   alta; **fondos de foso** = caras hacia arriba a cota intermedia sin hormigón encima.
3. Rechaza con motivo: caras inclinadas o curvas no verticales, caras hacia abajo a cota
   intermedia (cavidad cerrada o voladizo), fondos con hormigón encima, bloques sin fosos
   (es una zapata: usar Acero-Zapatas), cantos menores de 100 mm.
4. Elige los **ejes locales**: `u` es la dirección de las barras principales de las mallas (lado
   largo por defecto; también lado corto, X, Y o ángulo, general o bloque a bloque).
5. Clasifica en 2D (Clipper2) las **regiones del tope**: plataforma o murete (apertura
   morfológica de radio `wallMaxWidthMm / 2`), y cada arista como exterior, de hueco, de foso o
   interna. Esa topología es la que arma `BlockPlan`.

## La lámina (ventana previa)

- **Izquierda**: lista de bloques (lo detectado o el motivo de rechazo en rojo, dirección
  propia), dirección general, un panel por familia F1…F8 (activar, tipo de barra, separación,
  reparto "máx." o "exacta desde el inicio", patas / vertical / pie / extensión / forma),
  recubrimientos (inferior, superior, borde, foso y murete), ancho máximo de murete, referencia
  de niveles y plantilla de Partición.
- **Planta**: contorno con huecos, fosos rayados con su profundidad, plataformas y muretes con
  tintas distintas, las barras de la capa elegida (F1, F2 o F3) a su grosor, la traza de
  F4…F8, ejes u/v y las **líneas de corte A–A (a lo largo de u) y B–B (a lo largo de v)**, que
  se **arrastran** (imán al centro) y actualizan las secciones en vivo.
- **Secciones A-A y B-B** apiladas a la derecha: título con escala y posición del corte,
  terreno, perfil del hormigón (murete / foso / núcleo; muestreado del sólido real, con aviso
  si difiere del topológico), barras cortadas como círculos a su diámetro, barras contenidas en
  el plano como polilíneas con sus patas, **una etiqueta por familia y lado** con la notación
  del plano (`F4 ø5/8"@125`), cotas (tramos, ancho total, canto, profundidad de foso, espesor
  de base), **niveles** con su elevación (coordenadas compartidas por defecto; punto base o
  cota interna) y recubrimientos a trazos.
- **Interacción**: pasar el ratón por una barra la resalta (con su conjunto) en las tres
  vistas; clic en una familia de la leyenda la aísla; zoom con rueda, arrastrar para mover y
  doble clic para encajar, independiente en cada vista; interruptores Cotas / Etiquetas /
  Recubrimientos.
- **Analizar sin armar**: informe de texto copiable con los sólidos y caras leídos (cotas
  internas y en la referencia elegida, áreas), fondos de foso, regiones del tope con su ancho
  mínimo y aristas, motivo exacto de rechazo o armado previsto con tabla de cantidades, choques
  y separaciones. Se guarda también en `%Temp%\BlockRebar.log`. Pensado para familias cuyo
  `Elevation at Bottom` dice `<varies>`: dice qué caras inferiores ve el plugin y cuál toma
  como base.
- **Guardar como valores por defecto** escribe `config.json`; **Armar** crea las barras
  (entrega 2b); **Cancelar** no toca nada.

## Estado de las entregas

- **Fase 1** (clases puras + tests): hecha. `cd Tests && dotnet run` → 286 comprobaciones,
  0 choques, 0 contactos no previstos, separaciones reales ≤ nominal + 5 mm.
- **Entrega 2a** (esta): lectura del sólido, lámina en modo solo lectura y "Analizar sin
  armar". **No crea barras**: el botón Armar está desactivado.
- **Entrega 2b**: `RebarGenerator` (CreateFromCurves con patas, arrays, Partición, dos redes de
  seguridad, subtransacción por elemento, informe con pesos).
- **Entrega 2c** (opcional): vistas de sección A y B en Revit.

## config.json

Claves exactas (valores del plano): `coverBottomMm` 75, `coverTopMm` 50, `coverEdgeMm` 75,
`coverWallMm` 40, `wallMaxWidthMm` 300, `direction`, `F1_bottomMesh`, `F2_recessMesh`,
`F3_topMesh`, `F4_recessFaceL`, `F5_recessFaceH`, `F6_wallVertical`, `F7_wallHairpin`,
`F8_wallHoriz`, `partitionTemplate` (`BLQ-{marca}-{familia}`), `toleranceMm`,
`minBarLengthMm`, `levelReference` (`shared` / `project` / `internal`), `preview`,
`sectionViews`. Cada capa lleva `barTypeName` (exacto o fragmento, `"5/8\""`), `spacingMm` y
`layoutMode` (`maxSpacing` o `fromTop`). Sin coincidencia de tipo no se arma; nunca se
sustituye por otro tipo.

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027.2 (paquetes `Nice3point.Revit.Api.*` 2027.2 y
`Clipper2`).

```
dotnet build -c Debug
```

En Debug la compilación copia `BlockRebar.dll`, `Clipper2Lib.dll`, `config.json` y
`BlockRebar.pdb` a `%AppData%\Autodesk\Revit\Addins\2027\BlockRebar\` y `BlockRebar.addin` a
`%AppData%\Autodesk\Revit\Addins\2027\`. Al abrir Revit aparece **ARBA > Acero > Acero >
Bloques con foso** (comparte pestaña y desplegable con los demás add-ins ARBA) y el comando
también está en Complementos > Herramientas externas.

El proyecto lleva `EnableWindowsTargeting`, así que compila en Linux o macOS para comprobar
el código. Las clases puras se prueban sin Revit:

```
cd Tests && dotnet run
```

## Si algo falla en Revit

Pega (1) el texto de **Analizar sin armar** (botón "Copiar al portapapeles"), (2) el archivo
`%Temp%\BlockRebar.log` y (3) una captura de la lámina o del error.

## Estructura del código

| Archivo | Qué hace |
|---------|----------|
| `Geometry2D.cs`, `Poly2D.cs` | Geometría pura: contornos con huecos, cortes de rectas, booleanas y offsets 2D (Clipper2). |
| `BlockTopology.cs` | Clasificación pura del bloque: fosos, plataformas, muretes, tipo de cada arista. |
| `BlockPlan.cs` | Armado puro F1…F8 (polilíneas con patas, conjuntos, cantidades, pesos, separaciones). |
| `ClashCheck.cs` | Choques tramo contra tramo en 3D con lista de contactos previstos. |
| `BlockSection.cs` | Sección pura: perfil, círculos, polilíneas, cotas, niveles y etiquetas. |
| `BlockOutline.cs` | Lectura del sólido de Revit y sistema local (`BlockFrame`, perfil muestreado). |
| `HostAnalysis.cs` | Resultado por elemento, dirección propia e informe del modo diagnóstico. |
| `BarTypes.cs` | Tipos de barra del proyecto y diámetros reales. |
| `RebarOptionsWindow.cs`, `PlanPreview.cs`, `SectionPreview.cs`, `PreviewState.cs` | La lámina (WPF en código, sin XAML). |
| `RevitTheme.cs`, `RibbonApp.cs`, `ArmarBloqueCommand.cs`, `Log.cs` | Tema oscuro, cinta, comando y registro. |
| `AppConfig.cs`, `PartitionName.cs` | Configuración y plantilla de Partición. |
| `Tests/` | Pruebas de consola de las clases puras. |
| `PLAN.md`, `INSTALADOR.md` | Plan de trabajo e instrucciones para el instalador de ARBA. |
