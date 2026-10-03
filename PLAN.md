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
| 0 | Clonar Acero-Zapatas, leer README/PLAN/código, escribir este PLAN.md | hecho, OK recibido |
| 1 | Clases puras (`Geometry2D`, `Poly2D`, `BlockTopology`, `BlockPlan`, `BlockSection`, `ClashCheck`, `AppConfig`, `PartitionName`) + `Tests/` con el caso del plano y los casos (a) y (b); mostrar la salida de los tests | **hecho** (286 comprobaciones OK, 0 choques, separaciones dentro de nominal + 5; el proyecto principal compila en Linux) |
| 2 | Capa Revit (`BlockOutline`, `HostAnalysis`, `RebarGenerator`, comando, cinta) y ventana tipo lámina (`RebarOptionsWindow`, `PlanPreview`, `SectionPreview`, `PreviewState`); README e INSTALADOR; compilación con `EnableWindowsTargeting` | **siguiente** |
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
| `ClashCheck.cs` | Comprobación pura de choques: cada par de barras de conjuntos distintos, tramo contra tramo en 3D (distancia entre ejes contra suma de radios menos 1 mm), con lista de contactos previstos e informe por par de familias con coordenadas | nuevo (puro) |
| `BlockSection.cs` | Sección pura: `BlockSection.Cut(plan, topología, líneaDeCorte)` devuelve el perfil del hormigón en ese corte, los círculos (barra, familia, posición, diámetro), las polilíneas contenidas en el plano, las cotas (ancho total, tramos murete / foso / núcleo, profundidad de foso, espesor de base), los niveles y los anclajes de las etiquetas por familia y lado | nuevo (puro) |
| `BlockOutline.cs` | Lectura del sólido de Revit: zBase, zTope, fondos de foso, anillos, caras verticales, motivos de rechazo; `BlockFrame` (sistema local u/v por dirección, perfiles reales de las dos secciones muestreados con `Solid.IntersectWithCurve`) | nuevo, patrón de `FootingOutline` |
| `HostAnalysis.cs` | Resultado por elemento (topología o motivo de rechazo) + dirección propia | adaptado |
| `BarTypes.cs` | Tipos de barra del proyecto (`RebarBarType`): nombre exacto o fragmento, diámetro nominal y diámetros de doblado (estándar y de estribo) para `PlanDiameters` | nuevo |
| `Log.cs` | Registro de texto en `%Temp%\BlockRebar.log` (diagnóstico por elemento, informe de "Analizar sin armar", excepciones); se recorta al pasar de 2 MB | nuevo |
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
  contorno inferior, por defecto) o `recess` (solo bajo fosos, prolongando `anchorageMm`,
  600, dentro del hormigón contiguo; los extremos de anclaje van rectos y sin
  recubrimiento). Patas hacia abajo en extremos exteriores (`legDownMm`).
- **Caras de PLATAFORMA hacia el foso** (según el corte del plano), desde la cara hacia
  dentro: **F4 (pegada a la cara) → patas de F3 → F5 (la más interior)**. F5 mantiene ese
  plano en toda la altura, también por debajo de donde terminan las patas de F3.
- F3: barras u a `zTope − ct − d/2`, barras v debajo, recortadas contra cada plataforma
  con inset por arista: `coverEdge + d/2` en aristas exteriores y `coverWall + d4 + d/2`
  en caras de foso (por dentro de F4; si F4 está desactivada no se suma su diámetro); las
  barras v se retranquean un diámetro más. Patas hacia abajo (`legDownMm`) en **todos** los
  extremos de cara (exterior o de foso); rectas en los límites internos con un murete.
- F4: en cada cara de foso de plataforma, a `coverWall + d/2`; vertical desde
  `zTope − ct − d/2` bajando `verticalMm`, pie `footMm` horizontal hacia el foso (bajo su
  fondo). Si el pie queda a la altura de F1 o F2 (±d) se baja/sube a **media altura entre
  ambas mallas** y se avisa; si el vertical no llega bajo el fondo del foso se alarga hasta
  `zFoso − coverWall − d/2` y se avisa; si el pie no cabe hasta la cara opuesta se acorta
  al recubrimiento y se avisa. Posiciones a lo largo de la cara desde `coverWall + 1.5 d`
  de cada esquina de foso (o `coverEdge + d/2` si la esquina es exterior), `n = techo(L/s)`.
  Un conjunto por cara (plano de la L perpendicular a la cara; array a lo largo de ella).
- F5: a `coverWall + d4 + d3 + d/2` de la cara (por dentro de F4 y de las patas de F3),
  cotas desde `zFoso + coverWall + d/2` hasta `zTope − ct − d3u − d3v − d/2` (por debajo
  de F3), `n = techo(H/s)`. `shape: "segments"` (por defecto) = tramos rectos por cara
  **prolongados hasta la esquina**: cada barra llega al cruce con la línea de la barra de la
  cara contigua y sigue `lapMm` (400) más allá, recortada al recubrimiento del hormigón;
  `shape: "ring"` = una polilínea cerrada por traslape `lapMm` alrededor de la plataforma
  (si las caras de foso no cierran un anillo, vuelve a tramos y avisa). Un conjunto por
  cara (array vertical).
- **Murete**, desde la cara exterior hacia el foso: **F6 → pata exterior de F7 → F8 → pata
  interior de F7 → cara del foso**. Antes de armar se comprueba que entra: con 150 − 2 × 40
  = 70 mm deben caber las 4 barras de 3/8" y, sobre todo, la **horquilla**: distancia entre
  ejes de patas `w − (cw + d6 + d7/2) − (cw + d7/2)` ≥ `diámetro mínimo de doblado de
  estribo/horquilla + d7` (el de `RebarBarType.StirrupTieBendDiameter`, 4 d en ACI; F7 se
  crea con estilo estribo/horquilla). Si no entra → **rechazo con mensaje claro** que da el
  ancho del murete, el espacio libre y el diámetro de doblado.
- F6: en la cara exterior del murete a `coverWall + d/2`, desde `coverBottom + d/2` (al
  lado de las patas de F1: traslape) hasta `zTope − ct − d7 − d/2` (bajo la horquilla).
  Posiciones a lo largo de cada tramo recto exterior del murete, `n = techo(L/s)` desde
  `coverWall + 1.5 d` de cada esquina exterior.
- F7: horquilla en U invertida: barra horizontal sobre la corona a `zTope − ct − d/2`, pata
  exterior **pegada por dentro de F6** (`coverWall + d6 + d/2`), pata interior a
  `coverWall + d/2` de la cara del foso, `legMm` hacia abajo. El ancho local del murete se
  mide por sonda perpendicular a la cara exterior en cada posición (si la cara opuesta no es
  de foso, esa posición se omite con aviso). `placement: "aligned"` (por defecto) =
  **alineada con F6** (mismas posiciones @125, traslape por contacto); `"staggered"` =
  desplazada media separación. Un conjunto por tramo recto exterior.
- F8: `layers: 1` (por defecto): **una sola capa entre las dos patas de F7**, a
  `coverWall + d6 + d7 + d/2` de la cara exterior, y **por debajo del murete sigue por la
  cara exterior hasta zBase** (mismo plano en toda la altura): cotas desde
  `coverBottom + 2 d1 + d/2` (encima de F1) hasta `zTope − ct − d7 − d/2` (bajo la
  horquilla), `n = techo(H/s)`. `layers: 2` añade una segunda capa en la cara del foso a
  `coverWall + d7 + d/2`, solo en la altura del murete. "Cerradas por tramos": tramos rectos
  por cara prolongados hasta la esquina con `lapMm`, o `shape: "ring"` como en F5. Un
  conjunto por tramo (array vertical).

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
  modelo**: por defecto en **coordenadas compartidas** (punto de reconocimiento, porque los
  planos vienen en cotas absolutas: el tope del plano es 261.906), con las opciones de
  **punto base del proyecto** y **cota interna** (`levelReference` en `config.json` y en la
  ventana, que **muestra qué referencia se está usando** junto a los niveles). La capa Revit
  pasa el desfase; la clase pura solo suma.
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

### Modo diagnóstico: botón "Analizar sin armar"

Abre un informe de texto (copiable) que lista, **por elemento**:

- los **sólidos** leídos (volumen, descartados por < 1 %);
- las **caras inferiores** encontradas (hacia abajo), cada una con su **cota** (interna y en
  la referencia de niveles elegida), su área y si es la que fija zBase; también las caras
  hacia abajo a cota intermedia (techo de cavidad / voladizo);
- las **caras superiores** con su cota y área: las de zTope y los **fondos de foso**
  (cota, profundidad, área, contorno, abierto por un lado o no);
- las **caras verticales** (número) y las **caras inclinadas** (normal y cota) si las hay;
- las **regiones del tope**: plataforma o murete, con su **ancho mínimo** (bisección de la
  erosión con Clipper2), área y aristas por tipo (exterior / foso / interna);
- el **motivo exacto de rechazo** (o "armable" con el resumen del plan).

Pensado para familias de cimentación como "EXTRUCCION" cuyo `Elevation at Bottom` dice
`<varies>`: el informe dice qué caras inferiores ve el plugin y cuál toma como base. La
parte 2D (`BlockTopology.Describe()`) se prueba en consola; la de caras la añade
`BlockOutline`.

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
`F2_recessMesh.anchorageMm` (600), `F5_recessFaceH.shape` / `lapMm` (`"segments"`, 400),
`F7_wallHairpin.placement` (`"aligned"`), `F8_wallHoriz.layers` (1) / `shape` / `lapMm`,
más los de la lámina: `levelReference` (`"shared"` por defecto, `"project"` o
`"internal"`), `preview` (`showDims`, `showLabels`, `showCovers`, capa de planta por
defecto `"F1"`) y `sectionViews` (`enabled` false, `scale` 20, `marginMm` 500,
`nameTemplate` `"{marca} - Sección {letra}"`, `tagFamilyName` `""`, `showSolid` false).
Las claves del JSON se escriben exactamente así (`F1_bottomMesh`…) con
`JsonPropertyName`. El README recordará que las patas (300, 220, 340, 350) y F4 (1000/370) se
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
- **Dos casos de generalidad**, que imprimen la clasificación, las cantidades por familia
  y el conteo de círculos por familia en A-A y en B-B:
  - **(a) bloque con canaleta solo en un lado**: 4800 × 3800 × 1300, murete de 150 en el
    lado v = 0, canaleta de 600 × 500 de profundidad a lo largo de u abierta por los dos
    extremos (aristas abiertas = exteriores), plataforma en el resto. Debe dar 1 foso abierto,
    1 plataforma y 1 murete recto (dos tramos exteriores cortos y uno largo).
  - **(b) bloque con un foso rectangular central, sin murete**: 4000 × 3000 × 1200 con un
    foso de 1500 × 1000 × 700 en el centro, todo plataforma alrededor. Debe dar 1 foso,
    1 plataforma en anillo, 0 muretes, F4/F5 en las 4 caras del foso y nada de F6/F7/F8.
- Casos extra: bloque sin fosos (rechazo que remite a Zapatas), dos fosos de distinta
  profundidad (F2 bajo el más profundo, F5 desde cada fondo), pie de F4 que choca con F2
  (se recoloca a media altura con aviso), murete demasiado estrecho para la horquilla
  (rechazo con el mensaje del ancho), región mixta plataforma + murete partida por la
  apertura, `Poly2D` (unión, offset, apertura, ancho mínimo, inset por arista), config
  (ida y vuelta por JSON con las claves exactas) y partición con `{familia}`.

## 7. Fase opcional: Rejillas de foso (fase 3, pedida por el usuario; se empieza tras probar la 2c)

Botón propio ("Rejillas de foso") que lee los bordes superiores de cada foso y coloca, por
tramo recto, **ángulos de borde** y **rejillas**, con su metrado y las mismas reglas que el acero.

### Familias

- **Ángulos**: Structural Framing de acero; familia y tipo configurables en `config.json`
  (`grids.angleFamilyName`, `grids.angleTypeName`). Por defecto: familia que contenga
  "Angle" y tipo `L2-1/2X2-1/2X1/4` (o el equivalente métrico `L 63.5x63.5x6.4`). Si no
  está cargada: no se coloca, se avisa y la fase se marca en **amarillo** en la ventana,
  igual que los tipos de barra (regla `NameMatch`: exacto, fragmento único, ambiguo).
- **Rejillas**: Generic Model con parámetros de instancia **Largo**, **Ancho** y **Espesor**
  (`grids.gridFamilyName`, nombre configurable). Si no existe en el proyecto, la ventana ofrece
  el botón **"Crear familia de rejilla"**, que la genera desde la plantilla Generic Model
  (extrusión rectangular gobernada por esos tres parámetros, Espesor = 38 mm), la guarda junto
  a la DLL y la carga en el proyecto.
- **Pernos de expansión de 1/2"**: solo se cuentan (`grids.boltsPerAngle`, 5 por ángulo por
  defecto, editable) y se reportan en el informe; no se modelan.

### Colocación

- **Ángulos** en los dos bordes de cada tramo de foso, con el tope al ras de la cara superior y
  **retiro en los extremos** configurable (125 mm por defecto). **Longitudes editables por
  lado** en la ventana, porque el plano es ambiguo (texto: 3050 en el lado largo y 3400 en el
  lado corto; cota suelta: 2070).
- **Rejillas** al ras del tope: `n = techo(L / largoMax)`, pieza = `L/n − holgura`, ancho =
  ancho del foso − 10. Por defecto `largoMax` 825 y `holgura` 5. Con el bloque del plano debe
  dar **5 piezas de 695 × 590 por cada lado de 3800 (P1)** y **4 de 820 × 590 por cada lado de
  4800 (P2)**: los tramos del lado de 3800 llevan las esquinas (L = 3500) y los del lado de
  4800 van entre ellos (L = 3300). Es la comprobación obligatoria de `Tests/`.
- **Lámina e informe**: ángulos y rejillas se dibujan en la planta y en las secciones y salen
  en el informe con su propio metrado: m de ángulo, kg (peso lineal del perfil) y número de
  pernos.
- **Mismas reglas que el acero**: subtransacción por elemento, marca del plugin (comentario
  `BlockRebar GRID` / `BlockRebar ANGLE`), "Borrar y recolocar" sin duplicar y Ctrl+Z que lo
  deshace todo.

### Arquitectura prevista

`GridPlan.cs` (puro: tramos por foso, ángulos con retiro, reparto de piezas, metrado) +
`Tests/` (caso del plano: P1 5 × 695 × 590, P2 4 × 820 × 590), `GridGenerator.cs` (Revit:
Structural Framing por `NewFamilyInstance` con curva, Generic Model con sus tres parámetros,
familia de rejilla generada con `Document.EditFamily` / plantilla, marca y borrado), panel
"Rejillas" en la ventana, dibujo en `PlanPreview` / `SectionPreview`, informe.

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

### Notas de implementación de la fase 1 (lo que se decidió al escribir el código)

- **"Ancho mínimo" de una región** = diámetro del mayor círculo inscrito (bisección sobre la
  erosión con Clipper2). En un murete es su espesor; en una plataforma en anillo lo manda el
  lado más ancho del anillo (caso (b): 1250 aunque la banda estrecha mida 1000). Es
  exactamente el valor que se compara con `wallMaxWidthMm`.
- **Aristas partidas por adyacencia**: antes de clasificar, cada anillo recibe los vértices
  de las demás regiones, fosos y del cuerpo que caen sobre sus aristas, así una arista que
  toca a la vez un foso y un murete queda en dos aristas con su tipo cada una.
- **Inset por arista con escalón**: dos aristas colineales con retranqueo distinto dan rectas
  desplazadas paralelas; el contorno retranqueado hace un escalón perpendicular en el vértice
  (no se inclina).
- **Barras del mismo conjunto** = traslaciones a lo largo de la normal del conjunto (los
  puntos coinciden al quitar la componente según la normal): las dos mitades de una barra
  partida por un foso son conjuntos distintos.
- **Barras tangentes a un hueco retranqueado**: si el corte pasa justo por un vértice se
  desplaza una micra hacia el lado que deja más barra. Las barras que pasan dentro del margen
  de retranqueo de un foso (sin cruzarlo) se parten igualmente y llevan pata (conservador).
- **Horquillas en las esquinas**: la sonda perpendicular desde la esquina recorre el murete
  contiguo y no encuentra cara de foso; ahí se usa el ancho nominal del tramo (sonda desde su
  punto medio). Si ni el nominal da a un foso (testeros de un murete recto) se omiten con aviso.
- **Extremos rectos** llegan al recubrimiento (medio diámetro más allá de la línea de ejes);
  en un límite interno plataforma / murete la barra se queda justo en el límite.
- **Etiquetas** con la separación nominal de la familia ("@125"), no con el paso real del
  conjunto (que es menor o igual, `n = techo(L/s)`).
- **Comprobación de choques** (`ClashCheck`, pura, en todos los casos de prueba): cada par de
  barras de conjuntos distintos, tramo contra tramo en 3D; choque = distancia entre ejes menor
  que la suma de radios menos 1 mm (inadmisible: los tests exigen 0). Los contactos (distancia
  igual a la suma de radios) solo se admiten donde están previstos: las dos capas de una malla,
  F6 con la pata exterior de F7, F8 con esa pata y con la vertical de esquina de F6 de la cara
  contigua (mismo plano), las patas de F3 contra F4 y F5, F5 bajo F3, y los cruces de esquina
  de F5 / F8 / pies de F4 desfasados un diámetro. Cualquier otro contacto se lista como "no
  previsto" (los tests exigen 0).
- **Regla de esquinas** (F4, F6, F7): la barra de esquina pertenece a una sola cara, la que
  llega a la esquina (a recubrimiento + 1.5 d del vértice); la cara que sale de la esquina
  empieza su reparto a una separación de esa barra o, si es demasiado corta, termina antes
  con una sola barra en su otro extremo. También en los testeros de un murete recto.
- **Patas de F1 y F2**: F2 se retranquea `d1 + d2` más en los bordes exteriores, así su pata
  baja por dentro de la de F1 con un diámetro libre.
- **Zona de patas exteriores**: hasta donde llegan las patas de F1 / F2 / F3 junto a las caras
  exteriores (según el rango de cotas). El primer vertical de F4 junto a una esquina exterior,
  el pie de F4 hacia una cara exterior y las prolongaciones de F5 hacia ella paran antes.
- **F4 entre las barras de F2**: los verticales de F4 atraviesan la malla F2. En cada cara se
  reparte primero de extremo a extremo (barra exacta en la esquina que posee; la primera a una
  separación, en distancia de planta, de la barra de esquina de la cara anterior) comprobando
  que cada posición queda a más de la suma de radios + 1 mm de las barras de F2 que cruzan el
  plano del vertical; si alguna coincide, con más barras; si no, moviendo el inicio (o el
  final cuando no es esquina propia) dentro de lo admisible; y en último término ajustando a
  la retícula de F2 y añadiendo la barra de esquina que falte (aviso). Las barras de F2
  paralelas a una cara de foso esquivan el plano de sus verticales repartiéndose con más
  barras si hace falta (aviso).
- **Separación real máxima** (`BlockPlan.CheckSpacing`, en todos los tests): paso de cada
  conjunto y, en F4 / F6 / F7, separación entre barras consecutivas de cada cara INCLUIDOS los
  extremos (hasta la barra de esquina de la cara perpendicular, en distancia de planta, o
  hasta el límite admisible de la cara). Ninguna puede superar la nominal + 5 mm. Dos barras
  iguales a más de la separación nominal no forman conjunto (quedan sueltas).
- **Cruces de esquina a la misma cota**: los tramos de F5 y F8 de las caras a lo largo de v
  van un diámetro más bajos que los de las caras a lo largo de u; lo mismo los pies de F4
  (que convergen en las esquinas entrantes de un foso). El cruce queda en contacto previsto.
- **F5 por dentro de la pata más interior de F3** (la de las barras v, retranqueadas un
  diámetro más): plano único `cw + d4 + d3u + d3v + d5/2` en todas las caras y toda la altura.
- **Tramos colineales** de F5 / F8 de dos fosos alineados sobre la misma cara que se solapan
  se funden en una sola barra.
- **`layoutMode`** por familia: `maxSpacing` (reparto con barra en los dos extremos,
  `n = techo(L/s)`) o `fromTop` (separación exacta desde el nivel superior, el resto queda
  abajo, como el plano: 4 F5 por cara de núcleo y 6 F8 en el murete). Por defecto `fromTop`
  en F5 y F8 y `maxSpacing` en el resto.
- **Clasificación murete / plataforma**: es LOCAL (apertura morfológica de radio
  `wallMaxWidthMm / 2`); el "ancho mínimo por diámetro inscrito" es solo informativo (y las
  astillas numéricas se descartan por área).
- **`Clipper2`**: la unión de anillos sueltos usa la regla par-impar (las caras de Revit no se
  solapan); la de regiones se hace por pares (subject / clip) para que los solapes no se
  anulen. Precisión de 6 decimales en pies. Las versiones 1.5.x no tienen `Union(subject,
  fillRule, precision)` para `PathsD`: se usa `BooleanOp`.

## Decisiones confirmadas por el usuario (OK al plan)

1. **Bloque sin fosos** (solo huecos pasantes o macizo): se rechaza remitiendo a Zapatas.
2. **F8 de una sola capa** entre las patas de F7 (por dentro de F6 y de la pata exterior de
   F7), que por debajo del murete sigue por la cara exterior hasta zBase;
   `F8_wallHoriz.layers` 1 | 2, por defecto 1.
3. **Tramos rectos por cara prolongados hasta la esquina**, con opción de anillo por
   traslape; la prolongación / traslape es el parámetro `lapMm`.
4. **F2 `recess`** con anclaje `anchorageMm` (600); el modo por defecto sigue siendo `full`.
5. **F7 alineada con F6** (`placement: "aligned"`, opción `"staggered"`), pata exterior
   pegada por dentro de F6; orden F6 → F7 ext → F8 → F7 int → cara del foso; comprobación
   de que entra con el diámetro mínimo de doblado de la horquilla, rechazo claro si no.
6. **Canaleta abierta por un lado** admitida; sus aristas abiertas cuentan como exteriores.
7. **Caras de plataforma hacia el foso**: F4 → patas de F3 → F5; F5 mantiene su plano en
   toda la altura.
8. **Modo diagnóstico** "Analizar sin armar" (caras inferiores con cota, fondos, regiones
   con ancho mínimo, motivo exacto).
9. **Lámina**: barra en plano = la más cercana dentro de media separación; perfil topológico
   en tests y muestreado en Revit con aviso si difieren; **niveles en coordenadas compartidas
   por defecto** mostrando la referencia; acero sólido solo en 3D; vistas de sección como
   fase 2b.

## Progreso

- [x] Clonado y análisis de Acero-Zapatas (arquitectura, convenciones, tests).
- [x] PLAN.md.
- [x] OK del usuario al plan (decisiones de arriba).
- [x] Fase 1: clases puras (incluida `BlockSection`) + Tests: `cd Tests && dotnet run` → 278 comprobaciones correctas, 0 fallos, 0 choques y 0 contactos no previstos en todos los casos. `dotnet build BlockRebar.csproj -c Release` compila (0 errores) y deja `Clipper2Lib.dll` junto a `BlockRebar.dll`.
- [x] Revisión de choques (`ClashCheck`), regla de esquinas, retranqueo de F2, F5 por dentro de la pata más interior, cruces desfasados, `layoutMode` fromTop.
- [x] Separación real máxima (`CheckSpacing`): 286 comprobaciones OK, F4 vuelve a 88 con barra exacta en cada esquina.
- [x] Entrega 2a (lectura y diagnóstico, sin crear barras): `BlockOutline` (caras, zBase, zTope, fondos, rechazos con motivo, `BlockFrame` con perfil muestreado), `HostAnalysis` (informe), `BarTypes`, `Log`, `PreviewState`, `PlanPreview` (planta con cortes A–A / B–B arrastrables), `SectionPreview` (secciones con círculos, polilíneas, etiquetas, cotas, niveles, recubrimientos), `RebarOptionsWindow` (lista, paneles F1…F8, lámina, leyenda que aísla, "Analizar sin armar" con `ReportWindow`, Armar desactivado), `ArmarBloqueCommand` (sin transacción), README e INSTALADOR. `dotnet build BlockRebar.csproj -c Release` compila en Linux (0 errores). **Pendiente de probar en Revit 2027.2.**
- [x] 2a probada en Revit 2027.2 por el usuario ([653044 LOSA_TRANSF], Foundation Slab 3600 × 3300 × 1300: plataforma 2100 × 1800, murete en anillo, foso 600 × 800, 0 choques, tope 264.099 en compartidas). Correcciones: encuadre de las secciones con etiquetas y niveles (márgenes según el ancho de los textos), rótulos y cotas de la planta en bandas sin solapes.
- [x] Regla de tipos de barra ambiguos (`NameMatch`): exacto primero; un fragmento con varios candidatos se marca en amarillo con la lista y Armar queda desactivado hasta elegir; "Guardar como valores por defecto" guarda el nombre exacto. 7 comprobaciones nuevas en `Tests/` (293 en total).
- [x] Entrega 2b: `RebarGenerator` (CreateFromCurves con patas como tramos, F7 estilo estribo con reintento estándar, arrays `SetLayoutAsFixedNumber`, Partición + comentario `BlockRebar F#`, red de seguridad del plan por choques, red 1 antes de crear, red 2 con la geometría real, comparación de barras y longitudes con deducción de doblado), comando con subtransacción por elemento, pregunta "borrar y rearmar / conservar" si ya hay armadura del plugin, botón "Borrar armado del plugin" e informe final (`ReportWindow`) con tabla por familia y pesos. Compila en Linux (0 errores). **Pendiente de probar en Revit 2027.2.**
- [x] Entrega 2c: `SectionViews` (ViewSection A-A y B-B en las líneas de corte de la lámina, escala 1:20, detalle fino, recorte = bloque + margen, profundidad configurable, nombre por plantilla con sufijo si existe, acero del plugin sin ocultar y con el conjunto completo, una etiqueta por conjunto y familia visible si la familia de etiqueta está cargada, acero sólido en la 3D activa opcional); casilla "Crear las vistas al armar" y botón "Crear solo las vistas de sección". Compila en Linux. **Pendiente de probar en Revit 2027.2.**
- [ ] Fase 3: pruebas en Revit 2027.2 y correcciones.
- [x] Fase 3: rejillas de foso y ángulos (`GridPlan` puro con 47 comprobaciones nuevas (340 en total): caso del plano 8 ángulos = 23.14 m, 40 pernos, 10 P1 695 × 590 y 8 P2 820 × 590; foso rectangular con retiro de respaldo; canaleta en L con recorte de esquina; rejillas y ángulos en las secciones), `GridGenerator` (vigas por línea con justificación centrada y giro configurable, Generic Model por punto con Largo/Ancho/Espesor, familia generada desde la plantilla, W leído con la API, marca y borrado), panel en la ventana (modo, reparto, lista de tipos, familia, ángulo con regla de nombres, categorías), tipo por bloque, botones "Colocar rejillas y ángulos", "Crear familia de rejilla" y "Borrar rejillas y ángulos del plugin", dibujo en la lámina e informe. Compila en Linux. **Pendiente de probar en Revit 2027.2.**
- [ ] Pendientes de la 2c para el final: etiquetas ("The reference can not be tagged": probar la referencia de una barra del conjunto, `Rebar.GetBarReferences`/posición, en lugar de `new Reference(rebar)`); comprobar que las barras cortadas se ven como círculos en las vistas (presentación y profundidad).

### Notas de implementación de la fase 3

- Descomposición: cortes solo en los vértices entrantes del foso (giros a la derecha del anillo exterior y esquinas convexas de los huecos), con líneas paralelas al lado corto del foso; los trozos no rectangulares se descartan con aviso; los rectángulos contiguos con el mismo rango transversal se unen. Número de piezas con tolerancia: `n = techo((L − tol) / largoMax)` (3300 / 825 son 4, no 5).
- Clasificación del borde: sondeo a 5 mm fuera de la franja; plataforma = núcleo, murete = murete, el mismo foso = borde interno (sin ángulo), exterior = murete con aviso. El borde se parte en los vértices del foso para que, p. ej., el borde interior de una franja P1 solo lleve ángulo en el tramo que da al núcleo.
- Ángulos como Structural Framing: línea a media ala del borde (hacia el foso) y media ala bajo el tope, `Y/Z Justification` = centro, `Cross-Section Rotation` = `rotationDeg`; la viga siempre recorre el borde con el foso a su izquierda, así un solo giro orienta las alas de todos.
- Rejillas por punto con el nivel más alto bajo el tope, giradas según u (o v) del bloque, y comprobación del z real tras crear (`MoveElement` si la familia lo cambió).
- Familia generada: parámetros con `FamilyManager.AddParameter(nombre, GroupTypeId, SpecTypeId, instancia)`, planos de referencia con cotas etiquetadas (y de igualdad con los planos centrales de la plantilla), extrusión alineada a los planos por sus caras, `EXTRUSION_END_PARAM` asociado a Espesor, material con `FillPattern` de modelo cada 30 mm, un `FamilyType` por tipo de la lista; se guarda junto a la DLL y se carga con `Document.LoadFamily`.

### Notas de implementación de la entrega 2c

- Caja de la sección: `BasisX` = dirección del corte (u en A-A, v en B-B), `BasisY` = Z, `BasisZ` = `BasisX × Z` (hacia el observador): en A-A se mira hacia +v y en B-B hacia −u, igual que las flechas de la planta. `Max.Z = 0` deja el plano de corte en el origen; `Min.Z = −profundidad`.
- Etiquetas: `IndependentTag.Create` con referencia al conjunto; solo conjuntos cuya caja corta el volumen de la sección, uno por familia (comentario `BlockRebar F#`); cabezas alternadas sobre el tope y bajo la cara inferior, escalonadas.
- La posición de los cortes de cada bloque se guarda en la ventana al moverlos (`CutsOf`); los bloques no vistos usan el centro.
- Las vistas van en su propia subtransacción después de la del armado: un fallo en las vistas no deshace las barras.

### Notas de implementación de la entrega 2b

- La marca del plugin es el comentario de instancia `BlockRebar F#` (parámetro Comentarios); "Borrar armado del plugin" y la pregunta de rearmado solo tocan conjuntos con ese comentario, nunca otra armadura del elemento. La Partición sigue la plantilla (`BLQ-{marca}-{familia}`) y admite `{capa}`.
- Red de seguridad del plan: `ClashCheck` con 1 mm de tolerancia; un choque previsto rechaza el elemento antes de crear nada. Las separaciones por encima de la nominal solo avisan.
- Las fibras de comprobación se calculan por tramo (dos direcciones perpendiculares al tramo), así valen para barras horizontales, verticales (F6) y horquillas.
- Longitud esperada = polilínea − Σ(2 R tan(θ/2) − R θ) por doblez, con R = medio diámetro interior de doblado del tipo (estándar, o de estribo en F7) + medio diámetro de barra; tolerancia de comparación 1 % o 5 mm por barra. Las barras se cuentan posición a posición (`DoesBarExistAtPosition`).
- El borrado previo va dentro de la misma subtransacción que el armado: si el nuevo armado se rechaza, la armadura anterior se conserva.

### Notas de implementación de la entrega 2a

- `BlockOutline.Frame(mode, angle, wallMaxFt)`: la caché de sistemas locales incluye el ancho máximo de murete, así el campo "Murete hasta" de la ventana reclasifica plataformas y muretes en vivo sin volver a leer el sólido.
- La ventana es el único suscriptor de `PreviewState.Changed`: al mover un corte recalcula solo la sección afectada (`BlockSection.Cut` con `BlockFrame.SampledTop`) y redibuja las tres vistas; pasar el ratón o aislar una familia solo redibuja.
- Escala del título de cada sección: `N = 1152 / k` (k = píxeles por pie; 96 ppp) redondeada a la escala normalizada más cercana (1:20, 1:25…), con "≈" si difiere más del 8 %.
- Etiquetas y niveles en dos columnas (izquierda: "izq" y "centro"; derecha: "der" y los niveles) sin solapes, con línea de referencia al anclaje.
- "Analizar sin armar" vuelca el mismo informe en `%Temp%\BlockRebar.log` (`Log.Block`), además de los diagnósticos de cada elemento al abrir el comando.

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
