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
- **Guardar como valores por defecto** escribe `config.json` (con el nombre exacto de cada
  tipo de barra); **Armar** crea las barras; **Borrar armado del plugin** quita solo los
  conjuntos marcados por el plugin en los elementos seleccionados; **Cancelar** no toca nada.
- **Tipos de barra**: el nombre de `config.json` se busca primero exacto; si es un fragmento
  que coincide con varios tipos (`5/8` con `5/8"` y `Ø 5/8"`), el desplegable se marca en
  **amarillo** con la lista de candidatos y Armar queda desactivado hasta elegir uno. Nunca se
  elige en silencio ni se sustituye por otro tipo.

## Armado en Revit (`RebarGenerator`)

Cada conjunto del plan se crea con `Rebar.CreateFromCurves` (polilínea con sus patas como
tramos; F7 con estilo estribo/horquilla), se reparte como **array** (`SetLayoutAsFixedNumber`)
y recibe la **Partición** del contrato ARBA (`CIMIENTOS - BLQ-{marca}-F#`, ver "Contrato
ARBA-comun") más `ARBA - Origen = BLOQUES`, `ARBA - Código = F#` y `Metrado - Elemento =
CIMIENTOS`, que lo marcan como del plugin (ya no se escribe Comentarios). **O se arma el
bloque entero y bien, o no se arma**:

1. **Plan**: si `ClashCheck` encuentra algún choque previsto, el elemento se rechaza antes de
   crear nada.
2. **Antes de crear cada conjunto**: el eje y cuatro fibras a medio diámetro de cada barra
   prevista tienen que quedar dentro del hormigón (`Solid.IntersectWithCurve`).
3. **Después de crear y regenerar**: se lee la geometría real de cada barra de cada conjunto
   (radios de doblado y todas las posiciones) y se vuelve a comprobar.
4. Cualquier fallo deshace la subtransacción del elemento (incluido el borrado previo de la
   armadura anterior del plugin, que se conserva).
5. **Comparación**: barras y longitudes leídas de Revit frente a la tabla prevista (con la
   deducción de doblado del tipo); las diferencias se marcan en el informe. El informe añade los diámetros de doblado leídos de cada tipo y el redondeo de longitudes del
   proyecto (Configuración de refuerzo) para explicar diferencias de pocos mm por barra.

Si un elemento ya tiene armadura del plugin, al pulsar Armar se pregunta si se borra antes de
rearmar o se conserva (duplicando). El informe final lista, por elemento, la tabla de
cantidades por familia con pesos por diámetro, la comparación con Revit, los avisos y los
rechazos; también queda en `%Temp%\BlockRebar.log`.

## Estado de las entregas

- **Fase 1** (clases puras + tests): hecha. `cd Tests && dotnet run` → 286 comprobaciones,
  0 choques, 0 contactos no previstos, separaciones reales ≤ nominal + 5 mm.
- **Entrega 2a**: lectura del sólido, lámina y "Analizar sin armar". Probada en Revit 2027.2
  (Foundation Slab 3600 × 3300 × 1300 con foso perimetral: 0 choques, niveles en compartidas).
- **Entrega 2b** (esta): `RebarGenerator` con las dos redes de seguridad, subtransacción por
  elemento, arrays, Partición, borrado del armado del plugin, comparación con Revit e informe.
- **Entrega 2c** (esta): vistas de sección A-A y B-B en Revit en las líneas de corte de la
  lámina (escala 1:20, detalle fino, recorte con margen, nombre `{marca} - Sección {letra}`,
  acero sin ocultar, una etiqueta por conjunto y familia si la familia de etiqueta está
  cargada), al armar o con el botón "Crear solo las vistas de sección". La etiqueta se intenta
  con el tipo activado y la referencia al conjunto, en modo por categoría y con la referencia
  geométrica de una barra en la vista; si todo falla, el informe da el motivo exacto de cada
  intento.
- **Fase 3** (esta): rejillas de foso y ángulos de borde (`GridPlan` puro + `GridGenerator`),
  ver abajo.

## Rejillas de foso y ángulos de borde (fase 3)

Botón **Colocar rejillas y ángulos** (independiente del armado). Cada foso rectilíneo se
descompone en **franjas rectangulares** cortándolo en sus esquinas entrantes con líneas
paralelas a su lado corto: en el foso perimetral del plano, las franjas de los lados cortos
llevan las esquinas (**P1**, L = 3500) y las de los lados largos van entre ellas (**P2**,
L = 3300).

- **Rejillas** (Generic Model `Rejilla ARBA`, parámetros de instancia Largo, Ancho y Espesor;
  tipo con Peso por m2 y Peso = Largo × Ancho × Peso por m2; material "Rejilla" con patrón de
  líneas cada 30 mm). Reparto por franja: `n = techo(L / largoMax)`, pieza = `L/n − holgura`,
  ancho = ancho del foso − 10 (825 / 5 / 10 por defecto). Al ras del tope. Modos: modelar,
  solo informe o desactivado. Lista editable de **tipos de rejilla** (nombre, designación, alto,
  kg/m²; 31.0 kg/m² por defecto, del cuadro de parrillas), tipo elegible por bloque. Si la
  familia no está cargada, el botón **Crear familia de rejilla** la genera desde la plantilla
  Generic Model de Revit, la guarda junto a la DLL y la carga.
- **Ángulos** (Structural Framing, familia `L-Angle` tipo `L2-1/2X2-1/2X1/4` por defecto, regla
  de nombres de los tipos de barra): dos por franja, en sus bordes largos, cada uno **centrado
  en su borde**, clasificados por {lado largo | lado corto} × {borde de núcleo | borde de
  murete} con longitud fija (3050 / 3050 / 2070 / 3400) o retiro (125 / 125 / 115 / 50); si la
  longitud fija no cabe pasa a retiro y avisa; si dos ángulos se tocaran en una esquina se
  recortan con 10 mm de holgura y avisa. **Colocación por el talón** (Detalle 1 y Sección C del
  plano): la esquina exterior del perfil en la cara del foso, a "alto de rejilla" (38 mm) bajo el
  tope, ala horizontal hacia el foso (apoyo de la rejilla) y ala vertical hacia abajo con su cara
  exterior contra la pared. La orientación se comprueba sobre la geometría real de la viga
  (esquina vacía de la L) y se corrige invirtiendo la viga o girándola 180°; después se mueve por
  su geometría hasta el talón, y el informe da la desviación medida. Peso lineal del parámetro
  de tipo **W** (masa por longitud convertida con la API, o número tomado como lb/ft:
  4.10 = 6.10 kg/m); si no se lee, 6.10 kg/m. **Pernos de expansión 1/2"**: 5 por ángulo, solo
  contados. **Choques**: ala vertical contra el fondo del foso y ala horizontal contra la cara
  opuesta (hormigón), rejilla sobre el ala sin solape, y alas (chapas de 6.35 mm) contra todas las
  barras del armado; un choque rechaza el elemento. El informe da la cota del talón, la del
  apoyo de la rejilla y la barra más cercana a cada ángulo.
- Caso del plano (en `Tests/`): 8 ángulos (4 × 3050, 2 × 2070, 2 × 3400) = 23.14 m, 141.2 kg,
  40 pernos; 10 P1 de 695 × 590 (127.1 kg) y 8 P2 de 820 × 590 (120.0 kg).
- Lámina: rejillas rayadas con su grupo y ángulos en planta y en las secciones (L con el talón
  en la cara a la cota de apoyo en los cortados, banda en los vistos a lo largo). Informe con
  metrado: m y kg de ángulo, pernos, piezas, m² y kg de rejilla por grupo. Cada rejilla lleva su
  grupo (P1, P2…) en el parámetro de instancia de texto **Pieza** de la familia (Mark queda
  vacío, sin avisos de duplicados) y en `ARBA - Código` (`REJILLA P1`). Marca del plugin con los
  parámetros compartidos del contrato: `ARBA - Origen = BLOQUES`, `ARBA - Código` (`REJILLA P1`
  / `ANGULO longCore`), `ARBA - Anfitrión` (Id del bloque) y el **metrado de misceláneos**:
  `Metrado - Partida` (`ESTRUCTURAS METÁLICAS - REJILLAS` / `… - ÁNGULOS`), `Metrado - Material
  = ACERO ESTRUCTURAL`, `Metrado - Peso (kg)` (rejilla m² × kg/m²; ángulo m × kg/m, los mismos
  kg del informe), `Metrado - Pernos (und)` (`boltsPerAngle`, solo ángulos) y `Metrado -
  Elemento = MISCELANEOS`, con lo que el plugin de metrados los lleva a la tabla "Metrado acero
  estructural - Misceláneos" sin tocar su peso. Subtransacción por elemento, pregunta "borrar y
  recolocar / conservar" si ya los tiene, botón **Borrar rejillas y ángulos del plugin**, Ctrl+Z.
- Familia de rejilla: extrusión gobernada por Largo y Ancho (planos de referencia con cotas
  etiquetadas e igualdad con los planos centrales, caras alineadas) y Espesor (asociado al fin
  de extrusión), comprobada flexionando los parámetros al crearla; material "Rejilla" con patrón
  de modelo de líneas cada 30 mm **a 90°** (las platinas portantes cruzan el foso, paralelas al
  ancho de la pieza, y giran con la instancia) en superficie y corte con un solo color. La
  fórmula Peso = Largo × Ancho × Peso por m2 se intenta en forma directa y, si Revit la rechaza,
  por números adimensionales con tres parámetros unidad (1 m, 1 kg/m², 1 kg); el informe de
  creación dice qué forma entró y el motivo exacto de cada rechazo.

## config.json

Claves exactas (valores del plano): `coverBottomMm` 75, `coverTopMm` 50, `coverEdgeMm` 75,
`coverWallMm` 40, `wallMaxWidthMm` 300, `direction`, `F1_bottomMesh`, `F2_recessMesh`,
`F3_topMesh`, `F4_recessFaceL`, `F5_recessFaceH`, `F6_wallVertical`, `F7_wallHairpin`,
`F8_wallHoriz`, `partitionTemplate` (`{categoria} - {prefijo}-{marca}-{codigo}`), `toleranceMm`,
`minBarLengthMm`, `levelReference` (`shared` / `project` / `internal`), `preview`,
`sectionViews`. Cada capa lleva `barTypeName` (exacto o fragmento, `"5/8\""`), `spacingMm` y
`layoutMode` (`maxSpacing` o `fromTop`). Sin coincidencia de tipo no se arma; nunca se
sustituye por otro tipo.

## Contrato ARBA-comun

El add-in integra el código común de los add-ins ARBA, **ARBA-comun**
(https://github.com/Andy-rba30/ARBA-comun, etiqueta `v1.0.0`), como **submódulo git** en
`external/ARBA-comun`. `BlockRebar.csproj` importa `external/ARBA-comun/Arba.Comun.props`, que
compila `src/**/*.cs` dentro de `BlockRebar.dll` (clases `internal` del namespace `Arba.Comun`;
nunca una DLL compartida). No se modifica nada dentro del submódulo: lo que falte se anota en
`NOTAS-ARBA-COMUN.md`. Lo que aporta (ver `external/ARBA-comun/CONTRATO.md`):

- **Cinta**: `ArbaRibbon` común (pestaña ARBA, paneles IA / Acero / Metrados / Encofrado en ese
  orden, un solo desplegable **Acero**); el botón conserva el nombre interno `ARBA_Acero_Bloques`.
  También `RevitTheme`, `PartitionName` y `NameMatch` comunes (los archivos propios se borraron).
- **Partición** `{categoria} - {prefijo}-{marca}-{codigo}`: la categoría la deduce del anfitrión
  (bloques = `CIMIENTOS`), el prefijo es `BLQ` y `{codigo}` es la familia F1…F8 →
  `CIMIENTOS - BLQ-FT-01-F4`. **Cambio de significado**: `{familia}` es ahora la familia de Revit
  (como en los demás add-ins) y el código F1…F8 es `{codigo}`. Una `config.json` con la plantilla
  antigua `BLQ-{marca}-{familia}` se convierte al cargar y el informe lo avisa; la ventana avisa
  si la plantilla no empieza por `{categoria} - {prefijo}-` y muestra en el pie la versión del
  contrato (`ArbaContract.Version`).
- **Parámetros compartidos** (GUID fijo, de ejemplar, grupo Datos): al armar o colocar rejillas,
  `ArbaSharedParams.EnsureAll` crea o completa los ocho (`ARBA - Origen`, `ARBA - Código`,
  `ARBA - Anfitrión`, `Metrado - Partida`, `Metrado - Material`, `Metrado - Peso (kg)`,
  `Metrado - Pernos (und)`, `Metrado - Elemento`) desde un archivo temporal, restaurando el
  archivo de parámetros compartidos del usuario; los avisos van al informe.
- **Origen en vez de Comentarios**: las barras llevan `ARBA - Origen = BLOQUES`, `ARBA - Código
  = F#`; rejillas y ángulos además `ARBA - Anfitrión` y el metrado de misceláneos. Encontrar,
  borrar y rearmar usan `ArbaOrigin.Find / Delete`; por compatibilidad, los modelos armados con
  versiones anteriores (comentario `BlockRebar F#` / `BlockRebar GRID|ANGLE host <id>`) se siguen
  reconociendo y borrando por el comentario.
- **Migración sin rearmar**: si un bloque tiene armadura del plugin anterior al contrato
  (partición `BLQ-…` sin origen), la pregunta "borrar y rearmar / conservar" ofrece un tercer
  botón **Migrar la armadura antigua al contrato (sin rearmar)** (`ArbaMigration.MigrateHost`):
  partición `CIMIENTOS - BLQ-…-F#`, origen, código y `Metrado - Elemento`, Ctrl+Z lo deshace.

## Compilar e instalar

Requiere el SDK de .NET 10 y Revit 2027.2 (paquetes `Nice3point.Revit.Api.*` 2027.2 y
`Clipper2`). El submódulo tiene que estar inicializado:

```
git clone --recurse-submodules https://github.com/Andy-rba30/Fosa_transformadores
# o, en un clon ya hecho:
git submodule update --init
dotnet build -c Debug
```

Para subir de versión del contrato: `git -C external/ARBA-comun checkout v1.1.0` y commit del
puntero del submódulo.

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

Prueba primero en una **copia del modelo**. Pega (1) el texto de **Analizar sin armar** o del
informe final de Armar (botón "Copiar al portapapeles"), (2) el archivo `%Temp%\BlockRebar.log`
y (3) una captura de la lámina, de las barras en Revit o del error.

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
| `BarTypes.cs` | Tipos de barra del proyecto y diámetros reales; la regla de nombres (exacto, fragmento único, ambiguo) es `NameMatch` del común. |
| `RebarGenerator.cs` | Crea los `Rebar` con las dos redes de seguridad, partición y origen del contrato, borrado y comparación con lo previsto. |
| `SectionViews.cs` | Vistas de sección A-A y B-B en Revit, acero sin ocultar y etiquetas por conjunto. |
| `GridPlan.cs` | Rejillas y ángulos de borde, puro: franjas, piezas, ángulos por categoría, recortes de esquina, metrado. |
| `GridGenerator.cs` | Ángulos (Structural Framing), rejillas (Generic Model), familia de rejilla generada, origen / anfitrión / metrado de misceláneos y borrado. |
| `RebarOptionsWindow.cs`, `PlanPreview.cs`, `SectionPreview.cs`, `PreviewState.cs` | La lámina (WPF en código, sin XAML). |
| `RibbonApp.cs`, `ArmarBloqueCommand.cs`, `Log.cs` | Botón de la cinta (icono propio), comando (parámetros del contrato, migración) y registro. |
| `AppConfig.cs` | Configuración (`config.json`), conversión de la plantilla de partición antigua. |
| `external/ARBA-comun/` | Submódulo con el código común ARBA: contrato, partición, parámetros compartidos, origen, metrado, migración, cinta, tema y nombres. |
| `NOTAS-ARBA-COMUN.md` | Lo que falta o conviene cambiar en ARBA-comun (no se toca desde aquí). |
| `Tests/` | Pruebas de consola de las clases puras. |
| `PLAN.md`, `INSTALADOR.md` | Plan de trabajo e instrucciones para el instalador de ARBA. |
