# Plan de trabajo — add-in ARBA Bloques con foso (Revit 2027.2)

Este archivo es el plan vivo del proyecto y su estado. Se actualiza en cada commit
para que, si se corta la sesión, se pueda retomar exactamente donde se quedó.

## Objetivo

Add-in de Revit 2027 en C# (.NET 10, WPF en código, sin XAML) que arme **bloques macizos
de cimentación con fosos o canaletas abiertas por arriba** (fundaciones de transformador
con foso perimetral de contención de aceite, bloques de equipo con canaletas, fosos de
bombas) leyendo la **geometría real del elemento** (el sólido ya lleva los fosos recortados
por vacíos).

Ocho familias de armado, cada una activable y editable (tipo de barra, separación, patas):

| Código | Familia | Dónde |
|--------|---------|-------|
| F1 | Malla inferior, 2 direcciones, patas hacia arriba en bordes exteriores | contorno inferior |
| F2 | Malla bajo el fondo de foso, 2 direcciones, patas hacia abajo | todo el contorno ("full") o solo bajo fosos + anclaje ("recess") |
| F3 | Malla superior de PLATAFORMAS, 2 direcciones, patas hacia abajo en todos los bordes | cada plataforma |
| F4 | Barra en L en caras de foso del lado PLATAFORMA (vertical + pie bajo el foso) | caras de foso de plataforma |
| F5 | Horizontales en caras de PLATAFORMA hacia el foso, por dentro de F4 | caras de foso de plataforma |
| F6 | Vertical en la cara exterior del MURETE, altura completa, traslapada con F1 | caras exteriores de murete |
| F7 | Horquilla en U invertida sobre la corona del MURETE | eje de cada murete |
| F8 | Horizontales del MURETE, de zBase al tope, cerradas por tramos | caras del murete |

Hermano de [Acero-Zapatas](https://github.com/Andy-rba30/Acero-Zapatas)
(rama `claude/pensive-rubin-nsa1u1`): misma base (.NET 10, `Nice3point.Revit.Api.*` 2027.2,
`EnableWindowsTargeting`), mismo tema oscuro (`RevitTheme`), misma ventana previa con
esquemas, mismo `config.json`, misma red de seguridad que deshace el elemento entero si una
barra queda fuera del hormigón, y el mismo botón en la pestaña **ARBA** > panel **Acero** >
desplegable **Acero** > **Bloques con foso** (compartido con Zapatas, Losas, Columnas y
Muros). Los add-ins existentes no se tocan: `ArbaRibbon` ya está preparado para que cada
add-in añada su botón al desplegable común.

## Fases y forma de trabajo

| Fase | Contenido | Estado |
|------|-----------|--------|
| 0 | Clonar Acero-Zapatas, leer README/PLAN/código, escribir este PLAN.md | **hecho — esperando OK** |
| 1 | Clases puras (`Geometry2D`, `Poly2D`, `BlockTopology`, `BlockPlan`, `BlockSection`, `AppConfig`, `PartitionName`) + `Tests/` con el caso del plano; mostrar la salida de los tests | pendiente |
| 2 | Capa Revit (`BlockOutline`, `HostAnalysis`, `RebarGenerator`, comando, cinta) y ventana tipo lámina (`RebarOptionsWindow`, `PlanPreview`, `SectionPreview`, `PreviewState`); README e INSTALADOR; compilación con `EnableWindowsTargeting` | pendiente |
| 2b (opcional) | Botón **Crear vistas de sección en Revit** tras Armar (`SectionViews.cs`): dos `ViewSection` A y B, acero sin ocultar, etiquetas opcionales. No bloquea la fase 1 | pendiente |
| 3 | Pruebas del usuario en Revit 2027.2 y correcciones | pendiente |
| 4 (opcional) | Botón **Rejillas de foso**: ángulos de borde, pernos y rejillas | solo si se pide |

## Arquitectura (archivos)

| Archivo | Qué hace | Origen |
|---------|----------|--------|
| `BlockRebar.csproj`, `BlockRebar.addin`, `config.json`, `.gitignore` | Proyecto .NET 10 (`net10.0-windows`, x64, WPF), manifiesto (`ARBA Bloques` / `Armar bloque con foso`, ClientId propios), configuración inicial = plano IG-01-260275-104-0004-CV-DWG-0003 | nuevo (copiando el patrón de `FootingRebar.csproj`) |
| `RevitTheme.cs` | Tema oscuro de Revit 2027 | copiado tal cual, namespace `BlockRebar` |
| `RibbonApp.cs` | `ArbaRibbon` compartido (idéntico) + botón **Bloques con foso** con icono propio (bloque en sección con foso y murete) | copiado + icono nuevo |
| `AppConfig.cs` | `config.json`: recubrimientos (inferior, superior, borde, muro), `wallMaxWidthMm`, dirección, F1…F8, partición, tolerancias | nuevo, mismo patrón (Load / Save / Clone / Normalize) |
| `PartitionName.cs` | Plantilla de Partición: `{marca}`, `{id}`, `{tipo}`, `{familia}` (F1…F8), `{conjunto}` | copiado + comodín `{familia}` |
| `Geometry2D.cs` | `Pt`, `Span`, `Outline2D` (contorno con huecos, scan-line con franja), `Positions` (n = techo(L/s)), `Merge` | reutilizado tal cual |
| `Poly2D.cs` | Booleanas y offsets 2D sobre **Clipper2** (NuGet `Clipper2`): unión, diferencia, intersección, offset, apertura morfológica, componentes conexas, **inset por arista con recubrimiento distinto por tipo de borde** | nuevo (puro) |
| `BlockTopology.cs` | Clasificación pura del bloque en 2D: a partir de los anillos del contorno inferior, de las regiones del tope y de los fondos de foso (anillos + cota) obtiene fosos, plataformas, muretes, y para cada arista de cada región su **tipo** (exterior / cara de foso / límite interno) y a qué región pertenece cada cara de foso | nuevo (puro) |
| `BlockPlan.cs` | Armado puro F1…F8 en coordenadas locales: cada barra es una **polilínea** (tramo recto + patas como tramos), agrupación en conjuntos iguales equiespaciados, avisos, conteo y longitudes por familia, peso por diámetro | nuevo (puro) |
| `BlockSection.cs` | Sección pura: `BlockSection.Cut(plan, topología, líneaDeCorte)` devuelve el perfil del hormigón en ese corte, los círculos (barra, familia, posición, diámetro), las polilíneas contenidas en el plano, las cotas (ancho total, tramos murete / foso / núcleo, profundidad de foso, espesor de base), los niveles y los anclajes de las etiquetas por familia y lado | nuevo (puro) |
| `BlockOutline.cs` | Lectura del sólido de Revit: zBase, zTope, fondos de foso, anillos, caras verticales, motivos de rechazo; `BlockFrame` (sistema local u/v por dirección, perfiles reales de las dos secciones muestreados con `Solid.IntersectWithCurve`) | nuevo, patrón de `FootingOutline` |
| `HostAnalysis.cs` | Resultado por elemento (topología o motivo de rechazo) + dirección propia | adaptado |
| `RebarGenerator.cs` | Crea los `Rebar` con `CreateFromCurves` (polilíneas con patas), arrays `SetLayoutAsFixedNumber`, Partición, y las **dos redes de seguridad** | adaptado |
| `RebarOptionsWindow.cs` | Ventana WPF en código: lista de bloques, panel por familia, recubrimientos y partición, lámina (planta + sección A-A + sección B-B), leyenda, interruptores, botones | adaptado |
| `PreviewState.cs` | Estado compartido de las tres vistas: posición de los cortes A y B, barra resaltada, familia aislada, capa de planta mostrada, interruptores (cotas, etiquetas, recubrimientos); avisa a las vistas para redibujar | nuevo |
| `PlanPreview.cs` | Planta: contorno, fosos sombreados, plataformas y muretes con colores distintos, barras de la capa elegida, ejes u/v, **líneas de corte A–A y B–B arrastrables** con flechas y rótulos, leyenda y tooltip | adaptado |
| `SectionPreview.cs` | Dibuja un `SectionCut` (A-A o B-B): título y escala, perfil real, círculos y polilíneas con patas, etiquetas con la notación del plano, cotas, niveles, recubrimientos; zoom / arrastrar / doble clic propios | adaptado |
| `SectionViews.cs` (opcional, fase 2b) | Crea en Revit las dos `ViewSection` en las líneas de corte, acero sin ocultar y etiquetas de barra | nuevo |
| `ArmarBloqueCommand.cs` | Comando externo: selección de cimentaciones estructurales, análisis, ventana, transacción con subtransacción por elemento, informe | adaptado |
| `Tests/BlockRebar.Tests.csproj`, `Tests/Program.cs` | Programa de consola: `Poly2D`, `BlockTopology`, `BlockPlan` con el caso del plano, config y partición | nuevo |
| `README.md`, `INSTALADOR.md` | Documentación con el mismo estilo que Zapatas | nuevo |

Las clases puras (`Geometry2D`, `Poly2D`, `BlockTopology`, `BlockPlan`, `BlockSection`,
`AppConfig`, `PartitionName`) no dependen de Revit; solo de Clipper2 (biblioteca .NET pura, compila en
Linux). `Clipper2Lib.dll` se copia junto a `BlockRebar.dll` (el csproj deja de usar
`CopyLocalLockFileAssemblies=false` y mantiene `ExcludeAssets="runtime"` en los paquetes de
Revit, así solo se copia Clipper2).

## 1. Lectura de la geometría (`BlockOutline` + `BlockTopology`)

Entrada: cimentaciones estructurales (instancia de familia o losa de cimentación) cuyo
sólido ya lleva los fosos recortados. Se unen todos los sólidos y se desprecian los de
volumen menor del 1 % del mayor.

1. **Caras**: planas horizontales hacia abajo (`nz < -0.999`), hacia arriba (`nz > 0.999`),
   verticales (`|nz| < 0.001`, planas o cilíndricas de eje vertical, teseladas) y el resto
   (inclinadas).
2. **zBase** = cara inferior horizontal más baja. **zTope** = cara superior horizontal más
   alta. Sin cara inferior horizontal → rechazo.
3. **Fondos de foso** = caras hacia arriba con `zBase < z < zTope`. Las caras a la misma
   cota se unen en 2D (Clipper2); cada componente conexa es un **foso** con su contorno,
   su cota `zFoso` y su profundidad `zTope − zFoso`. Un foso cuyo contorno toca el borde
   exterior es una **canaleta abierta por un lado** (se admite; esas aristas cuentan como
   exteriores).
4. **Contorno inferior** = caras a zBase (anillos exteriores y huecos pasantes).
5. **Regiones del tope** = caras a zTope. Cada componente conexa se clasifica por su
   **ancho mínimo** con una **apertura morfológica** de radio `wallMaxWidthMm / 2`
   (offset hacia dentro y luego hacia fuera): lo que sobrevive es **PLATAFORMA**; el resto
   (componentes de ancho ≤ `wallMaxWidthMm`) son **MURETES**. Así una región con una
   plataforma y un murete unidos queda partida en sus dos partes.
6. **Aristas**: cada arista de cada región y de cada foso recibe un tipo: *exterior* (está en
   el contorno exterior inferior), *foso* (limita con un foso: la cara vertical de ese foso
   pertenece a la región, plataforma o murete, de ese lado) o *interna* (frontera entre
   plataforma y murete creada por la apertura; no es una cara). Para cada cara de foso se
   sabe a qué región pertenece y el foso al que da.
7. **Sistema local u/v** como en Zapatas (`BlockFrame`): `u` por el lado largo del contorno
   inferior por defecto; lado corto, X, Y o ángulo, general y por elemento.

**Rechazos con motivo** (no se crea ninguna barra):

- cara inferior no plana horizontal; bloque más delgado que 100 mm;
- cualquier cara inclinada: se dice si es *pared de foso no vertical*, *fondo inclinado*
  o *cara exterior inclinada*;
- el contorno del fondo de un foso no coincide con el hueco que deja en el tope (paredes no
  verticales aunque las caras sean planas);
- **cavidad cerrada**: fondo de foso con hormigón encima (sonda vertical desde el fondo), o
  cara horizontal hacia abajo a cota intermedia (techo de cavidad o voladizo);
- **sin fosos**: todos los "fosos" atraviesan toda la altura (eso son huecos pasantes, no
  fosos) o no hay ninguna cara intermedia → el mensaje remite al add-in de Zapatas. Los
  huecos pasantes **acompañados** de fosos reales se admiten como huecos del contorno
  inferior (F1/F2 los rodean).

## 2. Armado (`BlockPlan`, puro)

Coordenadas locales u, v (pies), z desde zBase. Cada barra es una polilínea en un plano
vertical (tramo recto + patas como tramos; `CreateFromCurves` añade los radios de doblado).
Las barras iguales y equiespaciadas se agrupan en un **conjunto** (array). Recubrimientos:

| Recubrimiento | Se aplica a |
|---------------|-------------|
| `coverBottomMm` 75 | desde zBase (F1, arranque de F6) |
| `coverTopMm` 50 | desde zTope (F3, F4, F5, F6, F7) |
| `coverEdgeMm` 75 | caras exteriores del cuerpo macizo (F1, F2, F3 en bordes exteriores de plataforma) |
| `coverWallMm` 40 | caras de foso (F3, F4, F5) y **las dos caras del murete** (F6, F7, F8) |

**Apilado de capas** (desde la cara del hormigón hacia dentro, en mm con los diámetros
reales de los tipos de barra):

- F1: barras u a `cb + d/2`; barras v encima (`cb + d + d/2`); las posiciones de las
  barras v se retranquean un diámetro más en las caras perpendiculares a u para no chocar
  con las patas de las barras u. Patas hacia arriba (`legUpMm`) solo en extremos exteriores;
  rectas en los huecos.
- F2: el tope de la malla a `belowRecessFloorMm` bajo el fondo del foso **más profundo**
  (barras u a `zFoso − b − d/2`, barras v colgadas debajo). Extensión `full` (todo el
  contorno inferior) o `recess` (solo bajo fosos, prolongando `anchorMm` dentro del
  hormigón contiguo). Patas hacia abajo en extremos exteriores (`legDownMm`).
- F3: barras u a `zTope − ct − d/2`, barras v debajo, recortadas contra cada plataforma
  con inset por arista: `coverEdge + d/2` en aristas exteriores y
  `coverWall + d4 + d5 + d/2` en caras de foso (por dentro de F4 y F5; si F4 o F5 están
  desactivadas no se suma su diámetro). Patas hacia abajo (`legDownMm`) en **todos** los
  extremos.
- F4: en cada cara de foso de plataforma, a `coverWall + d/2`; vertical desde
  `zTope − ct − d/2` bajando `verticalMm`, pie `footMm` horizontal hacia el foso (bajo su
  fondo). Si el pie queda a la altura de F1 o F2 (±d) se baja/sube a **media altura entre
  ambas mallas** y se avisa; si el vertical no llega bajo el fondo del foso se alarga hasta
  `zFoso − coverWall − d/2` y se avisa. Posiciones a lo largo de la cara desde
  `coverWall + 1.5 d` de cada esquina, `n = techo(L/s)`. Un conjunto por cara (plano de la
  L perpendicular a la cara; array a lo largo de ella).
- F5: a `coverWall + d4 + d/2` de la cara (por dentro de F4), cotas desde
  `zFoso + coverWall + d/2` hasta `zTope − ct − d3u − d3v − d/2` (por debajo de F3),
  `n = techo(H/s)`. Por tramos rectos por cara **prolongados hasta la esquina** de la línea
  de la cara contigua (se cruzan en la esquina); opción `shape: "ring"` = una polilínea
  cerrada por traslape (`lapMm`) alrededor de la plataforma si Revit la crea limpia.
- F6: en la cara exterior del murete a `coverWall + d/2`, desde `coverBottom + d/2` (al
  lado de las patas de F1: traslape) hasta `zTope − ct − d7 − d/2` (bajo la horquilla).
  Posiciones a lo largo de cada tramo recto exterior del murete.
- F7: horquilla en U invertida: barra horizontal sobre la corona a `zTope − ct − d/2`
  **de cara a cara** del murete (patas a `coverWall + d/2` de cada cara, `legMm` hacia
  abajo). El ancho local del murete se mide por sonda perpendicular a la cara exterior.
  Posiciones **intercaladas** con F6 (desplazadas media separación) para no coincidir en el
  mismo plano. Si el ancho entre patas no admite el diámetro de doblado del tipo se avisa
  antes de crear.
- F8: horizontales a `coverWall + max(d6, d7) + d/2` de **cada** cara del murete
  (exterior y de foso; opción `faces: "exterior"`), cotas desde `coverBottom + 2 d1 + d/2`
  (encima de F1) hasta la corona bajo F7, `n = techo(H/s)`; por tramos rectos prolongados
  hasta la esquina ("cerradas por tramos"), con la misma opción `ring`.

Reglas comunes: separación = máximo (`n = techo(L/s)`), tramos menores que
`minBarLengthMm` se omiten y se cuentan; comprobación de cotas dentro del canto; chequeo de
solape vertical F1 / F2 / F3 (como el de las dos parrillas en Zapatas). No se comprueban
choques barra-barra más allá del apilado descrito (igual que en Zapatas).

## 3. Capa Revit y red de seguridad (`RebarGenerator`)

Igual que en Zapatas: **o se arma el bloque entero y bien, o no se arma**.

1. Antes de crear cada conjunto: eje + 4 fibras a medio diámetro
   (`Solid.IntersectWithCurve`) de **cada tramo de la polilínea** en todas las posiciones del
   array.
2. Tras `Regenerate`: geometría real de cada barra de cada conjunto
   (`GetCenterlineCurves`, patas y radios de doblado incluidos) comprobada entera contra
   los sólidos.
3. Cualquier fallo deshace la subtransacción del elemento. El informe final dice, bloque a
   bloque, barras y conjuntos por familia y **peso por diámetro** (π d²/4 · 7850 kg/m³ ·
   longitud real de la polilínea).

Barras: `Rebar.CreateFromCurves(doc, RebarStyle.Standard, tipo, host, normal, curvas,
BarTerminationsData sin ganchos, true, true)`; normal = dirección del array (perpendicular al
plano de la polilínea). Partición = `partitionTemplate` con `{familia}` = F1…F8. Los tipos
de barra se buscan por nombre exacto o fragmento; sin coincidencia no se arma, nunca se
sustituye.

## 4. Ventana previa (tema oscuro, WPF en código): la lámina del plano

El esquema se ve como la lámina del plano: **una planta y dos secciones, una por cada
dirección**, todo calculado por las mismas clases puras que usa el generador.

### Disposición

- **Izquierda, la PLANTA** del bloque seleccionado:
  - contorno exterior (con huecos), fosos sombreados, plataformas y muretes en colores
    distintos;
  - barras de **F1 / F2 / F3 según la capa que se elija mostrar** (selector de capa en la
    cabecera de la planta; las familias de cara F4…F8 se marcan con su traza en planta);
  - **ejes locales u/v** con su flecha y rótulo;
  - **DOS líneas de corte** con flechas y rótulos "A" y "B" como en el plano: **A–A a lo
    largo de u** (plano u-z, a `v = vCorte`) y **B–B a lo largo de v** (plano v-z, a
    `u = uCorte`), ambas por el centro del bloque por defecto.
- **Derecha, apiladas**: **SECCIÓN A-A** arriba y **SECCIÓN B-B** abajo, cada una con su
  título y su escala (1:20, 1:25, 1:50… la que resulte de encajar, mostrada en el título).
- Debajo o al lado, la **leyenda** de familias (F1…F8, un color cada una) y los
  **interruptores** "Cotas", "Etiquetas", "Recubrimientos".
- Lista de **bloques seleccionados** (como en Zapatas): `u × v`, canto, nº de fosos con su
  profundidad, plataformas y muretes detectados, resumen del armado o motivo de rechazo en
  rojo, **dirección por elemento**. Panel por **familia F1…F8** (activar, tipo, separación,
  patas / vertical / pie / extensión), recubrimientos (4), `wallMaxWidthMm`, dirección
  general, referencia de niveles y plantilla de Partición. Botones **Guardar como valores
  por defecto**, **Armar**, **Cancelar**.

### Contenido de cada sección (`SectionPreview` dibuja un `SectionCut`)

- **Perfil real del hormigón** en ese corte (muestreado del sólido con
  `Solid.IntersectWithCurve`, como en `SectionPreview` de Zapatas): murete, foso, núcleo y
  base, más el **solado** si existe (sólido o cara horizontal por debajo de zBase dentro
  del elemento o de un suelo adyacente; se dibuja como franja aparte). En `Tests/` el perfil
  sale de la topología (exacto); en Revit manda el muestreado y, si difiere del topológico
  más de la tolerancia, se avisa.
- **Barras cortadas** por el plano: círculos a su **diámetro real** en el punto de cruce de
  cada tramo de su polilínea con el plano.
- **Barras contenidas en el plano** (dentro de la tolerancia: la más cercana al corte de
  cada conjunto, a menos de media separación, o cualquier barra suelta a menos de la
  tolerancia): **líneas con sus patas y doblados** (radio de doblado del tipo).
- **Color por familia** F1…F8 con leyenda compartida con la planta.
- **Etiquetas** con la notación del plano (`ø5/8"@125`, `3/8"@125`, `ø5/8" L=1370`
  para barras sueltas) y línea de referencia: **una etiqueta por familia y por lado**
  (izquierda / derecha del centro del corte, o arriba / abajo para las mallas), sin repetir
  en cada barra. El texto sale del nombre del tipo de barra de Revit y de la separación real
  del conjunto.
- **Cotas**: ancho total, **tramos** a lo largo del corte (murete / foso / núcleo / foso /
  murete, obtenidos de los cruces de la línea de corte con las regiones), **profundidad del
  foso** y **espesor de la base** (fondo de foso a cara inferior), y el canto total.
- **Niveles a la derecha**: tope, fondo de cada foso y cara inferior, con la **cota real del
  modelo**: elevación respecto al **punto base del proyecto** (por defecto), a las
  **coordenadas compartidas** o interna, configurable (`levelReference` en `config.json` y
  en la ventana). La capa Revit pasa el desfase; la clase pura solo suma.
- **Recubrimientos**: con el interruptor activo se dibujan a trazos las líneas de
  recubrimiento (inferior, superior, borde, muro) dentro del perfil.
- Terreno bajo la cara inferior como en Zapatas.

### Interacción

- **Arrastrar las líneas de corte** A–A y B–B en la planta mueve el corte (dentro del
  bloque, con imán al centro) y **las dos secciones se actualizan en vivo**; el rótulo del
  título muestra la posición del corte (`A-A a v = 1.90 m`).
- **Pasar el ratón por una barra** en cualquiera de las tres vistas la **resalta en las
  otras dos** (misma barra, o su conjunto) y muestra familia, diámetro, separación y
  longitud en un tooltip y en la barra de estado.
- **Clic en una familia de la leyenda la aísla** (segundo clic: todas); se aplica a las
  tres vistas.
- **Zoom** con rueda, **arrastrar** para mover y **doble clic** para encajar, como en
  Zapatas, **independiente en cada vista** (arrastrar sobre una línea de corte mueve el corte,
  arrastrar sobre el fondo mueve la vista).
- **Al cambiar de bloque** en la lista, las tres vistas se recalculan (cortes al centro del
  nuevo bloque, zoom encajado).

Todo el estado compartido vive en `PreviewState` (cortes, barra resaltada, familia aislada,
capa de planta, interruptores); cada vista se suscribe y redibuja. La ventana recalcula
`BlockPlan` al cambiar cualquier ajuste y `BlockSection.Cut` al mover un corte.

### Lógica pura (`BlockSection`, probada en `Tests/`)

`BlockSection.Cut(BlockPlan plan, BlockTopology topology, SectionLine line, double tol,
Func<double, double?> perfilMuestreado = null)` devuelve un `SectionCut` con:

- `Profile`: perfil del hormigón a lo largo de `s` (u en A-A, v en B-B): lista de tramos
  `(s0, s1, zTop)` exactos a partir de la topología (regiones del tope, fosos, huecos), o
  el muestreado si se pasa;
- `Circles`: (barra, familia, s, z, diámetro) por cada tramo de polilínea que cruza el plano;
- `Polylines`: (barra, familia, puntos (s, z), diámetro) de las barras contenidas en el plano;
- `Segments`: tramos del corte con su tipo (murete / foso / núcleo / hueco) para las cotas;
- `Levels`: tope, fondos de foso y cara inferior (z local y elevación);
- `Labels`: un anclaje por familia y lado con el texto `ø{tipo}@{s}`.

### Opcional (botón "Crear vistas de sección en Revit", después de Armar; fase 2b)

- Crear dos `ViewSection` en Revit en las mismas líneas de corte A y B (`ViewFamilyType`
  de sección, `BoundingBoxXYZ` con origen en el corte, `BasisX` a lo largo del corte,
  `BasisY` vertical y `BasisZ` mirando como en la lámina), **escala 1:20**, **nivel de
  detalle fino**, **crop ajustado al bloque más un margen configurable**
  (`sectionViews.marginMm`, 500 por defecto). Nombres `"{marca} - Sección A"` y
  `"{marca} - Sección B"` (plantilla `sectionViews.nameTemplate`; si ya existen se añade un
  sufijo numérico).
- Acero **visible sin ocultar** en esas vistas (`SetUnobscuredInView`) y, con la opción
  `sectionViews.showSolid`, como sólido en las **vistas 3D** (`Rebar.SetSolidInView` solo
  admite `View3D`: en las secciones el acero se muestra con su presentación de conjunto
  completa, `SetPresentationMode(All)`).
- Opcional: **una etiqueta de barra por conjunto** (`IndependentTag.Create`) con la familia
  de etiqueta `sectionViews.tagFamilyName` de `config.json`. Si no está cargada en el
  proyecto, no se etiqueta y se avisa en el informe.
- Va en la fase de Revit; no bloquea la fase 1.

## 5. Valores por defecto (`config.json` inicial)

Los del plano IG-01-260275-104-0004-CV-DWG-0003 (fundaciones de transformadores, sala
eléctrica N°1): recubrimientos 75 / 50 / 75 / 40, `wallMaxWidthMm` 300, F1 5/8"@125 patas
300 arriba, F2 5/8"@125 `full` a 75 bajo el fondo, patas 220 abajo, F3 5/8"@125 patas 340
abajo, F4 5/8"@125 vertical 1000 pie 370, F5 3/8"@200, F6 3/8"@125, F7 3/8"@125 patas 350,
F8 3/8"@200, partición `BLQ-{marca}-{familia}`, tolerancia 2, barra mínima 300. Se añaden
`anchorMm` (F2 `recess`, 600 por defecto), `lapMm` (anillos, 400) y `shape` / `faces`
descritos arriba, más los de la lámina: `levelReference` (`"project"` por defecto,
`"shared"` o `"internal"`), `preview` (`showDims`, `showLabels`, `showCovers`, capa de
planta por defecto `"F1"`) y `sectionViews` (`enabled` false, `scale` 20, `marginMm` 500,
`nameTemplate` `"{marca} - Sección {letra}"`, `tagFamilyName` `""`, `showSolid` false). El README recordará que las patas (300, 220, 340, 350) y F4 (1000/370) se
midieron a escala en el plano y que el murete lleva 3/8" por defecto (la sección A) aunque la
sección B dice 5/8".

## 6. Caso de prueba (`Tests/`)

Bloque 4800 × 3800 × 1300, murete 150, foso perimetral de 600 de ancho y 800 de
profundidad (fondo a z = 500), núcleo 3300 × 2300. El test construye los anillos en 2D
(contorno inferior 4800 × 3800; tope = anillo del murete 4800 × 3800 menos 4500 × 3500, y
plataforma 3300 × 2300; fondo de foso = 4500 × 3500 menos 3300 × 2300 a z = 500) y
comprueba:

- `BlockTopology`: 1 foso de 800 de profundidad, **1 plataforma + 1 murete en anillo**,
  4 caras de foso de plataforma y 4 de murete, 4 aristas exteriores de murete.
- `BlockPlan` con los valores por defecto (5/8" = 15.9 mm, 3/8" = 9.5 mm): cotas de cada
  capa según el apilado de la sección 2, nº de barras y de conjuntos por familia
  (p. ej. F4: 4 conjuntos, 27 + 27 + 19 + 19 barras de 1000 + 370; F3: 19 barras u de
  3153 + 2 × 340 y 26 barras v), todas las barras dentro del hormigón en 2D (plataforma /
  murete / cuerpo según su cota), pie de F4 entre F1 y F2, F6 intercalada con F7, y la
  **tabla final** de cantidades, longitudes y peso por familia y diámetro impresa por consola.
- `BlockSection` con el mismo caso: la **sección A-A por el centro** muestra F1, F2 y F3
  como líneas en u y círculos en v; F4 como L en las dos caras del núcleo (vertical + pie
  bajo el foso); F5 y F8 como círculos; F6 y F7 en los dos muretes (F6 vertical, F7 en U
  invertida de cara a cara). El perfil topológico tiene los tramos murete 150 / foso 600 /
  núcleo 3300 / foso 600 / murete 150, profundidad de foso 800 y base 500; los niveles son
  tope, fondo de foso y cara inferior. **Se imprime el conteo de círculos por familia en A-A
  y en B-B**, y se comprueba que una etiqueta por familia y lado no se repite. Mover el corte
  fuera del núcleo (por el foso) cambia el perfil y deja F3/F4/F5 fuera del corte.
- Casos extra: bloque sin fosos (rechazo que remite a Zapatas), canaleta abierta por un
  lado, dos fosos de distinta profundidad (F2 bajo el más profundo, F5 desde cada fondo),
  foso que choca el pie de F4 con F2 (se recoloca a media altura con aviso), región mixta
  plataforma + murete partida por la apertura, `Poly2D` (unión, offset, apertura, inset por
  arista), config (ida y vuelta por JSON) y partición con `{familia}`.

## 7. Fase opcional: Rejillas de foso (otro botón, solo si se pide)

Lee los bordes superiores de cada foso y, por tramo recto, coloca ángulos de borde (familia
configurable, por defecto L2-1/2" × 2-1/2" × 1/4") en los dos bordes con retiro en extremos
(125 mm) y pernos de expansión de 1/2" (5 por ángulo), y rejillas (familia configurable,
alto 38 mm, al ras del tope): `n = techo(L / largoMaxMm)`, pieza = `L/n − holgura`, ancho
= ancho del foso − 10. Con 825 / 5 y el foso del plano: los tramos del lado de 3800 llevan
las esquinas (L = 3500 → 5 piezas de 695 × 590) y los del lado de 4800 van entre ellos
(L = 3300 → 4 piezas de 820 × 590), como el cuadro de parrillas P1/P2. Clase pura
`GridPlan` + `Tests`, generador y ventana propios. No se empieza hasta que el acero funcione.

## Decisiones de diseño

1. **Geometría real**: todo sale del sólido con los vacíos ya recortados; nada depende de
   parámetros de familia. Clipper2 solo para booleanas y offsets 2D; el scan-line de
   `Outline2D` sigue siendo el que corta cada barra.
2. **Plataforma vs murete por apertura morfológica** con radio `wallMaxWidthMm / 2`: es la
   "prueba de offset hacia dentro" generalizada, y además parte las regiones mixtas.
3. **Inset por arista**: cada arista de una región lleva su recubrimiento según su tipo
   (exterior / foso / interna) y la familia; el polígono retranqueado se construye arista a
   arista (intersección de rectas desplazadas) y se limpia con Clipper2; después el scan-line
   corta con franja cero (el recubrimiento ya está en el polígono).
4. **Patas como tramos** de la polilínea (`CreateFromCurves`), nunca como ganchos: la
   longitud de pata es la exterior; los radios los añade Revit y los comprueba la red de
   seguridad 2.
5. **Apilado explícito** de capas por cara (sección 2) para que F3 / F4 / F5 y F6 / F7 / F8
   no se crucen en el mismo plano; F6 y F7 intercaladas.
6. **Un conjunto por familia, cara y tramo**: barras iguales equiespaciadas en un array;
   lo demás, barras sueltas.
7. **Red de seguridad doble** y subtransacción por elemento, igual que Zapatas.
8. **Lámina del plano**: planta a la izquierda con las líneas de corte A–A (u) y B–B (v)
   arrastrables, secciones A-A y B-B apiladas a la derecha. La sección es una clase pura
   (`BlockSection.Cut`) que se prueba en consola; la ventana solo dibuja su resultado. En
   Revit el perfil es el muestreado del sólido; en los tests, el topológico.
9. **Estado compartido** (`PreviewState`) entre las tres vistas: cortes, barra resaltada,
   familia aislada e interruptores; zoom y desplazamiento propios de cada vista.

## Puntos a confirmar con el OK del plan

1. **Bloque sin fosos** (solo huecos pasantes o macizo): se rechaza remitiendo a Zapatas.
   ¿De acuerdo, o se arma con F1 + F2 + F3 igualmente?
2. **F8 en las dos caras del murete** por defecto (`faces: "both"`), por fuera de F6/F7 no
   cabe: van por dentro (`coverWall + d6 + d/2`). ¿O solo en la cara exterior?
3. **"Cerradas por tramos"** (F5 y F8) = tramos rectos por cara prolongados hasta la
   esquina, con la opción `ring` (polilínea con traslape `lapMm`). ¿Correcto?
4. **F2 `recess`**: anclaje `anchorMm` 600 por defecto dentro del hormigón contiguo.
5. **F7 intercalada con F6** (media separación) en el mismo plano de la cara exterior.
6. **Canaleta abierta por un lado**: se admite; sus aristas abiertas son exteriores.

## Progreso

- [x] Clonado y análisis de Acero-Zapatas (arquitectura, convenciones, tests).
- [x] PLAN.md.
- [ ] OK del usuario al plan.
- [ ] Fase 1: clases puras (incluida `BlockSection`) + Tests (salida de los tests en el informe).
- [ ] Fase 2: capa Revit, ventana tipo lámina (planta + A-A + B-B), README, INSTALADOR, compilación.
- [ ] Fase 2b (opcional): botón "Crear vistas de sección en Revit".
- [ ] Fase 3: pruebas en Revit 2027.2 y correcciones.
- [ ] Fase 4 (opcional): rejillas de foso.

## Cómo retomar

1. `git pull` de la rama `claude/ecstatic-ptolemy-cehcro`.
2. `cd Tests && dotnet run` para las clases puras; `dotnet build -c Debug` en Windows con
   Revit 2027 copia la DLL (y `Clipper2Lib.dll`), `config.json` y el `.addin` a
   `%AppData%\Autodesk\Revit\Addins\2027\`.
3. En Revit: pestaña ARBA > Acero > Bloques con foso. Seleccionar cimentaciones
   estructurales de hormigón con fosos.

## Entorno de compilación de la sesión

Ubuntu 24.04 con `dotnet-sdk-10.0` (apt) y `EnableWindowsTargeting=true`: WPF, los paquetes
`Nice3point.Revit.Api.*` 2027.2 y Clipper2 compilan en Linux solo para comprobar; la DLL se
usa en Windows con Revit.
