# TASK-306 — Piloto de iluminación 2D
HacknPlan: proyecto 239834, Sprint 19, ítem 830. Registro de ejecución, no backlog.

## Specs
S1. La herramienta de Editor genera mapas de normales alineados frame a frame tanto en sprites simples como en spritesheets, conectando _NormalMap en Secondary Textures sin romper slices, PPU ni Point (no filter); si su calidad visual no convence, queda evaluada comparativamente la salida directa de spritefusion-sprite-normals sobre los mismos sprites piloto.
S2. Se dispone de prueba comparativa verificable en Catacumbas (escala 1:1, luz normal y atenuada) entre el estado plano actual, Variante A (solo entorno/props) y Variante B (entorno + jugadores/enemigos), comprobando capas modulares, flipX, rotación de armas y HitFlash.
S3. Queda registrado el impacto en FPS con varias DungeonLightProp encendidas y la recomendación aprobada por el equipo (tanto de variante A/B como de generador nativo vs spritefusion-sprite-normals) antes de aplicar a gran escala.

## Tasks
T1 | S1 | Cálculo por frame y pruebas EditMode | verificado (orientación +Y) | 0ec203b0
T2 | S1 | Herramienta Editor, exportación y Secondary Textures | verificada en EditMode | 0ec203b0
T3 | S2 | Comparativa Catacumbas y benchmark externo si aplica | hecha (plano, guiado, SpriteFusion, Raid) | sin commit (evaluación)
T4 | S3 | Mediciones y decisión con Arte/Diseño | cerrada por el usuario sin medir FPS | sin commit (evaluación)
T5 | S1 | API probada para vincular un mapa generado externamente | verificado (6/6 SpriteNormal EditMode) | 0ec203b0
T6 | S2 | Mapas SpriteFusion en todos los props (el cofre conserva la guiada) | verificado (1.043 vínculos) | c062c29c
T7 | S2 | Mapas en personajes y armas; verificar renderers lit, flipX, HitFlash y Visibility | verificado por pruebas; flipX y capas sin revisión visual dedicada | c062c29c
T8 | S2 | Luces piloto leyendo normales (DungeonLightProp, velas, brasero, luz del jugador) | verificado; luz del jugador no localizada | ee6c2f04
T9 | S2 | Validación del usuario en Raid | aprobada para luces y sistema | sin commit (evaluación)
T10 | S2 | Ubicar antorchas, braseros y velas de forma coherente en CatacombsMap | verificado por pruebas; falta validación visual | 489f4d02
T11 | S2 | Luz global de Catacumbas a 0,1 | verificado por pruebas | 489f4d02
T12 | S2 | Las luces aclaran la niebla fuera del cono | verificado por pruebas y verificación independiente | ee6c2f04
T13 | S2 | Suavizar el borde del cono para las entidades | verificado por pruebas y verificación independiente | ee6c2f04
T14 | S2 | Ocultar luces y puertas detrás de muros | verificado (cobertura 42/42, punto 1/1, puertas 74/74 y 10/10) | ee6c2f04
T15 | — | Actualizar VisibilityArchitecture.md y GameFeelParticlesArchitecture.md:114 | hecho | ee6c2f04

## Log
L1. revisa la tarea con el titulo TASK-306 — Piloto de Iluminación 2D con Normal Maps en Unity (Generador Editor y Comparativa A/B) y empeza la implementacion usando ODD
L2. Árbol limpio en New-Testing. Editor 6000.6.8f1 listo. Sin cambios de dependencias, Visibility ni shaders de iluminación.

L3. RED CS0103; GREEN EditMode SpriteNormalFrameTests: 1/1. Sin prueba visual aún.
L4. RED de importación: CS0103 por SpriteNormalImporter ausente. Uso: en `Tools > Grimhold > Sprite Normal Map Generator`, seleccioná una copia PNG importada como Sprite con sus slices definidos, ajustá relieve/detalle/suavizado y activá `Wrap each tile` solo para tiles repetibles. `Generate and link _NormalMap` crea un PNG nuevo junto al original y vincula la textura secundaria. No reemplaza un `_NormalMap` previo. Para comparar parámetros usá otra copia piloto.

L5. EditMode SpriteNormal: timeout (600 s, exit 18, EDITOR_BLOCKED_BY_DIALOG: Scene(s) Have Been Modified). No hay resultado de importación; no reintentar automáticamente. Tras resolución manual consultar estado del Test Runner antes de otra acción. Prueba de bordes con maxTextureSize reducido agregada, no verificada.

L6. Usuario: «ya esta». Test Runner sin ejecución activa; el último resultado era de UtilityExpeditionPlayModeTests, no del piloto. Se inició SpriteNormalImporterTests, incluyendo el caso de reducción.

L7. RED: 2/3 casos de importación, falla en borde de hoja reducida. Corrección con rectángulos fuente. GREEN: compilación y 4/4 SpriteNormal EditMode. Empieza la preparación de la comparativa.

L8. Referencia plana 1:1 en Edit Mode, 256×192 a 16 PPU, luz normal: Artifacts/TASK-306/baseline-normal.png y baseline-normal.md. Cofre LockedChest en Loot_11. MainMenu restaurada sin cambios. No es evidencia de Play Mode ni de A/B. Editor libre para la prueba manual del usuario.

L9. Usuario sobre Detail3: «es lo mismo». Pedido: «https://github.com/Hugo-Dz/spritefusion-sprite-normals probemos esto». Generado localmente con worker oficial worker-Cllu-FMn.js y WASM spritefusion_sprite_normals_bg-DtB4nPko.wasm (SpriteFusion web), cuatro celdas 64×64 procesadas individualmente en OpenGL, strength 1.5, detail 6, volume 1.15. Atlas verificado 256×64: Artifacts/TASK-306/SpriteFusion-chest.png. Sin subir arte ni instalar dependencias.

L10. Variante importada: Assets/Art/Props/Test/Chest-Open-SpriteFusion.png, cuatro frames y _NormalMap confirmado. Captura aislada frame 0: Flat→Detail3 MAE RGB 3,13/255; Flat→SpriteFusion 13,90/255; control direccional 47,09/255. Con normales desactivadas: renders idénticos. Método: Artifacts/TASK-306/chest-frame-0/README.md. No prueba iluminación de Raid ni rendimiento.

L11. Usuario: «Se sienten igual de planos». Comparativa corregida en CatacombsMap, frame 0. Control Flat/Flat antes: brillo 37,65 vs 31,01; después: 70,92 vs 71,21 (0,41 %); repetición idéntica. Al invertir la luz en la misma posición el brillo cambia de la tapa al borde inferior con SpriteFusion, no demuestra volumen convincente. Evidencia: Artifacts/TASK-306/equal-light/comparison.png. Escena sin guardar, edición con Undo.

L12. Validación del usuario: «ok ahora hay una sensacion distinta, no de volumen pero la luz ya no ilumina todo el sprite a misma distancia el derecho esta completamente iluminado y el izquierdo solo la tapa y un poco de la base del lado de la luz.» Para este cofre se percibe iluminación direccional localizada, no volumen; no implica aprobación de adopción.

L13. «ok hacelo»: prueba de normales guiadas por forma. Chest-Open-Guided conserva cuatro slices; mapa 256×64 con curvatura por fila de tapa, bisel lateral en sus extremos y frente plano. En SceneView: Chest-guided a izquierda, Chest-flat a derecha, sin cambiar luz ni guardar escena. Pendiente valoración a 1:1.

L14. Validación del usuario sobre la normal guiada: «si la luz viene de abajo si se siente distinto, no se si la palabra es redondeado pero se ve diferente porque la luz es bloqueada por el cofre y te da una minima sensacion de tridimensionalidad. Desde arriba lo que pasa es que la luz se corta en lo que seria el borde inferior de la tapa, pero sigue llegando a la base, es decir se oscurece la division de tapa-base». Lectura parcial de profundidad dependiente de dirección; no confirma oclusión física ni aprobación para escalar.

L15. «habria que ver como actua con una luz con flickering». Captura en Catacumbas de frame 0, guiado/plano en la misma posición, luz fija desde arriba o abajo. Curva TorchFlicker del proyecto con temporización simulada; 75 muestras a 25 Hz, intensidad 1,887–2,105. Referencias repetibles, sin guardar escena. Artifacts/TASK-306/chest-flicker/README.md. Pendiente valoración perceptiva.

L16. Raid Catacumbas Host/InProgress/Raiding en copia aislada, con cofre piloto, jugador, muros, velas y santuario. Con luces originales Disabled, Flat/Guided idénticos (0 píxeles); captura Artifacts/TASK-306/raid-context/. Iniciada comparación con lectura de normales habilitada solo en luces piloto en ejecución.

L17. «continua» tras fallo por cuota del operador. Retomado inline sobre la Raid aislada que seguía activa (Host, InProgress, Raiding). Antorcha izquierda en Accurate: guiado vs plano 0,62/255 en el cofre (153/323 px, máx. 11). Global atenuada 0,85→0,3: 0,60. Diagnóstico con distancia de normal 1: 1,2 (máx. 16). Controles de repetición y Disabled: 0 px. Accurate también altera muros y suelo (10.267 px, media 0,05). Antorcha casi encima del cofre (1,7 u), distancia de normal autorada 3, luz global sin normales. Visualmente indistinguible. Evidencia: Artifacts/TASK-306/raid-context/accurate/README.md y dimmed/. Copia aislada cerrada y eliminada (3,0 GB). ProjectSettings.asset del proyecto principal aparece modificado (companyName Astro Rift Games, productName Everaris): no fue hecho por el piloto; no se tocó.

L18. Usuario: «probaste bajando la global light? yo la tenia en 0.1 de intensidad para que se note la iluminacion de las antorchas mucho mejor.» Primer intento en escena de previsualización inválido (la antorcha no iluminaba el cofre). Repetido en su escena abierta (global 0,1 sin guardar), encuadre de la Raid y cofre temporal: guiado vs plano 1,33/255 (máx. 17) con Accurate; con 0,85 da 0,73, coherente con 0,62 de la Raid. Imperceptible a 1:1 y a 4×. No es Raid: brillo absoluto menor que en la Raid. Todo restaurado. Evidencia: Artifacts/TASK-306/global-01/README.md.

L19. Usuario ante la recomendación de cerrar: «integralo, yo decido». Interpretación: integrar la variante A con la normal guiada en los cofres reales de la Raid y las luces piloto leyendo normales, para que el usuario lo juzgue en juego. Sin commits. Mapeo de cofres y luces en curso.

L20. Usuario: «no solo los cofres, integralo a todos los props».
L21. Usuario: «personajes y armas».
L22. Decisión técnica: el mapa guiado es manual y solo existe para el cofre; para el resto se usan mapas SpriteFusion por slice, generados fuera de Unity (sin dependencias de runtime/CI) y vinculados con API probada del Editor. Nota: HacknPlan marca fuera de alcance la conversión masiva antes de aprobar el piloto; el usuario la pide explícitamente como responsable de la decisión.

L23. Inventario (explore): props en Assets/Art/Props (63, sin Test ni *-Flame-Sheet); personajes Character/{Body,Head,Legs,LeftHand,RightHand} (150) + Sets (720); enemigos Skeletons/Ranged (19), Slime (72); armas Weapons raíz+Shield+Animations Spritesheets (28). Renderers en juego lit con Sprite-Lit-Default; HitFlash cambia a material unlit durante el golpe. Cofres de Raid: LootContainer usa Chest-Open-Sheet (recibe la normal guiada), LockedChest-Open-Sheet y Coffin-Close/Open. Luces: DungeonLightProp.prefab base m_NormalMapQuality 2 (Disabled), variantes Torch/Candle/Candles/Brazier heredan; sin overrides en la escena. Luz propia del jugador no localizada. Excluidos: rifts de Collapse (material emisivo), NPCs del pueblo, placeholder del Espectro.

L24. Link: RED CS0117 observado; implementación lista, GREEN sin ejecutar. Durante la corrida, el Editor principal (PID 416) pasó a «starting» y después dejó de existir: no hay proceso Unity, no hay Temp/__Backupscenes y Editor.log termina en 16:56. Causa no determinada; los cambios sin guardar de CatacombsMap pueden haberse perdido. SpriteFusion: 6.075 slices → 1.042 mapas, más la guiada del cofre (Chest-Open-Sheet). Muestra revisada y alineada por frame. Copiados 1.043 a Assets (sin sobrescribir; lista en Artifacts/TASK-306/copied-normals.txt) y verificados byte a byte. Reabriendo el Editor tras «continua».

L25. Editor reabierto (PID 12912) con CatacombsMap y Gameplay cargadas. La corrida de SpriteNormal (6 pruebas) quedó esperando 10 min y venció (exit 18). Después el Editor pasó a «unreachable», sin entradas en el log: un modal bloquea el hilo principal, probablemente el de guardar escenas modificadas. Los 1.043 PNG copiados todavía no se importaron (sin .meta). No reintentar: cuando el usuario cierre el diálogo, consultar el estado de las pruebas antes de correrlas de nuevo.

L26. Usuario: «listo» (diálogo resuelto; escena limpia). GREEN: 6/6 SpriteNormal, incluidos Link con tamaño igual y distinto. Iniciado el vínculo del lote.

L27. Lote vinculado: 1.043/1.043, 0 fallos, 1.043 verificados (secondary _NormalMap apunta al mapa e importado como NormalMap). Diff de cada .meta: entrada secondaryTextures más dos campos de serialización que Unity agrega con valores por defecto (spriteTessellationMethod 0, spriteGeometrySubdivision -1). DungeonLightProp.prefab m_NormalMapQuality 2→1 (Accurate), distancia 3 sin cambios; Brazier, Candle, Multiple Candles y Torch heredan Accurate. Luz del jugador no localizada. Corriendo regresión EditMode completa.

L28. La regresión EditMode completa (8.564 pruebas) no avanzó y venció a los 30 min (exit 18). El Editor quedó «unreachable». Al recargar, Wwise «Automatically added AkAudioListener to Main Camera»: eso ensucia la escena abierta, y al iniciar las pruebas aparece el modal de guardar escenas. No se ejecutó ninguna prueba de regresión. Plan: cuando el usuario cierre el diálogo, correr el subconjunto relevante por filtro, no la suite completa.

L29. Usuario: «listo» (diálogo cerrado sin guardar). Regresión dirigida: SpriteNormal 6/6, ChestOpeningPresenter 8/8, LootContainerPrefab 20/20, CoffinLootContainer 15/15, CatacombsDoor 8/8. DungeonDoorAnimation venció (exit 18): al cargar CatacombsMap en las pruebas, Wwise vuelve a crear objetos y la escena queda modificada para la corrida siguiente. Escena actual sin título y limpia. Se retoman los 13 grupos restantes; antes de cada uno, si una escena quedó modificada por la automatización, se abre una escena vacía sin guardar.

L30. Regresión dirigida (18 grupos, 196 pruebas): 192 pasan. Fallan 4. ExtractionSanctuaryPrefab: Prefab_ComposesSanctuaryAndInteractionAreaUnderOneRoot (null) y Prefab_V1VisualsPreserveGameplayGeometryAndVisibility (falta BoxCollider2D). CatacombsSceneComposition: MinimalRaid_ContainsNoDeveloperSpawnersPickupsOrExcludedSystems (AmbientDustParticles) y Sanctuaries_AreUnmodifiedPrefabInstancesAtTheEightAuthoredPoints (override m_IsActive). Validan composición de prefab y escena; esos archivos no se tocaron. Su relación con el cambio no está demostrada con una corrida sin él. Escena guardia: todas limpias. Lanzada verificación independiente (riesgo ítems 6 y 3).

L31. Usuario: «faltaria agregar antorchas brazeros y velas de forma coherente en el mapa, y bajar la luz global a 0.1».

L32. Verificación independiente del lote: aprueba metas (1.043, solo _NormalMap más 2 campos por defecto), mapas (tipo, filtro, tamaño) y prefab (1 línea). Los 4 fallos de regresión ya existen en HEAD (falta BoxCollider2D en ExtractionSanctuary, AmbientDustParticles y overrides m_IsActive de santuarios). Además Unity regeneró DungeonGrayboxMinimapLayout.asset y LiberationSans SDF - Fallback.asset, que no son parte del trabajo.
L33. Luces: el presenter de presión ajusta la global en runtime con DefaultDungeonAmbientConfig. Fases cambiadas Normal 0,85→0,1, Reinforcements 0,75→0,088, Critical 0,65→0,076, Collapse 0,55→0,065 (misma proporción). Global de CatacombsMap a 0,1. Gameplay.unity a 0,1 editando solo esa línea: guardarla desde Unity re-serializaba ~3.000 líneas ajenas, así que se revirtió a HEAD y se aplicó el cambio puntual. Ubicación automática (plan.cs y apply.cs en Artifacts/TASK-306/lights): 159 antorchas en caras norte de muro de ≥3 filas, a ≥6 u de otras luces y ≥3 u de puertas; 77 braseros en suelo abierto 5×5, a ≥8 u de otras luces y ≥4 u de puertas y santuarios; 14 Multiple Candles flanqueando santuarios. 250 instancias bajo LightProps. Cobertura de suelo a ≤4,5 u: 154→9.711 de 15.944. El diff de CatacombsMap solo agrega líneas, salvo la intensidad global. Vista del plan: lights/plan-overview.png.

L34. Pruebas tras las luces: DungeonAmbientConfig 5/5, DungeonPressureAmbientPresenter 9/9, DungeonLightProp 4/4, CatacombsDoor 8/8, LootContainerPrefab 20/20. CatacombsSceneComposition 7/9: fallan los 2 que ya fallaban en HEAD (AmbientDustParticles y override m_IsActive). La global de Gameplay se lee 0,1 en una escena de previsualización. Siguen: validación del usuario en Raid y medición de FPS (S3).

L35. Usuario: «ok creo que funciona bien, tal vez son demasiadas luces, pero los cofres, ataudes y jarrones al ser visibles todo el tiempo no se si estan siendo afectados realmente por las luces, aparte de que se posicionan en cualquier lado y las luces mayormente estan en las paredes, pero eso es un tema del sistema de spawn que no esta calibrado, habria que arreglar eso para verlo mejor. Pero mi pregunta es porque esta todo visible? No me gusta como se integra la niebla con el cono de vision y las luces hay que hacer algo con eso tambien».
Nota: los cambios en las reglas de Visibility están fuera del alcance de HacknPlan; la calibración del spawn de contenedores queda como seguimiento aparte. Investigando (solo lectura) cómo se combinan visibilidad, niebla y luces.

L36. Explicación (explore). Cofres, ataúdes, jarrones y santuarios tienen EntityVisibilityPresenter con _occlusionOnly=1 (EntityVisibilityPresenter.cs:45): ignoran el cono y solo los ocultan los obstáculos. Luces y puertas no tienen presenter, así que nunca se ocultan. La niebla (VisionFog.shader) es unlit, va en la capa Fog encima de todo y su máscara solo usa obstáculos, sin luces. Las propuestas «Lights thin the fog», «Explored memory», «Lit fog» y «Fog over entity edges» figuran sin implementar en VisibilityArchitecture.md:199-225. Cambiar la regla de visión o el aspecto aprobado de la niebla requiere decisión de Diseño y actualizar la arquitectura; queda fuera del alcance original de HacknPlan.

L37. Decisión del usuario sobre niebla, cono y luces: elige «Las luces aclaran la niebla (recomendado)», «Suavizar el borde del cono» y «Ocultar luces y puertas detrás de muros». No elige ocultar cofres, ataúdes y jarrones fuera del cono. Amplía el alcance de Visibility, explícitamente excluido en HacknPlan; lo decide el usuario. Requiere actualizar la arquitectura.

L38. T14: EntityVisibilityPresenter con _occlusionOnly en DungeonLightProp y sus variantes (Torch con _visibilityOffset (0,-2)), DungeonDoor y CatacombsDoor. Override de -1,5 en Y para las Multiple Candles de escena montadas en muro (-115,5; 38). RED: 14 fallos de cobertura y 1 de punto; GREEN: 42/42 y 1/1. Ocultar solo apaga los sprites; Light2D sigue iluminando.
L39. T12: VisionFogLightRules (máximo 16, las más cercanas al visor), FogLightSource, DungeonLightPropRegistry.Props, DungeonLightProp.VisibilityAlpha, VisionFog.shader (_FogLights) y VisionFogSettings LightClearStrength 0,7 / LightInnerRatio 0,35 / LightReferenceIntensity 1,2. Una luz solo aclara si su fuente es visible. Reglas 4/4. La prueba de VisibilityAlpha agrega un renderer, como el prefab real: sin renderers, ApplyVisibility no registra el alfa.
L40. T13: VisionFacingSmoother (una vez por cuadro) en LocalVisionSource con VisibilitySettings.FacingSharpness 12. La niebla usa query.Facing; se quitó FacingSharpness de VisionFogSettings. Corresponde a la opción de la propuesta 8 de compartir la orientación suavizada; la visibilidad sigue siendo binaria con los fundidos existentes.
L41. Documentación: VisibilityArchitecture.md (regla de visión, Feel, luces que aclaran la niebla, propuestas 2 y 8) y GameFeelParticlesArchitecture.md:114.

L42. Pruebas T12-T13: VisionFacingSmoother 4/4, VisionFogRules 18/18, VisionFogLightRules 4/4, DungeonLightProp 8/8, InventoryPassiveEffects 12/12, VisibilityFadeRules 7/7, VisionConeRules 8/8, VisibilityPrefabCoverage 42/42 y VisionOcclusion en Play Mode 15/15 (asíncrono). CatacombsSceneComposition 7/9, con los 2 fallos que ya existían (corrida desde MainMenu limpia: Wwise ensucia la escena vacía). SessionCompositionConfiguration 22/23: falla SharedInventoryPrefab_PreservesCompleteSerializedUiContract (30 vs 38), un prefab de inventario que no tocamos. Verificación independiente lanzada (riesgo ítems 3 y 6).

L43. Verificación independiente T12-T15: pasan las tres opciones y la documentación, sin bloqueantes. Observaciones: (1) el despeje alrededor de una antorcha visible es un círculo sin oclusión y puede aclarar la niebla detrás de su muro (las entidades de ahí siguen ocultas); queda para validar en juego. (2) Al cambiar de visor, el suavizado arrancaba desde una orientación vieja: corregido con TDD, RED (0,27; 0,96) y luego GREEN 5/5; ahora salta directo si hubo cuadros sin consulta. (3) Mientras una luz no se registró en el sistema de visibilidad, cuenta como visible. (4) Costo: unas 278 nuevas comprobaciones de línea de visión por cuadro, que entran en la medición de FPS de S3. (5) Queda la clave huérfana FacingSharpness en VisionFogSettings.asset, sin efecto.

L44. Después de la corrección del suavizado, la oclusión en Play Mode sigue en 15/15. Quedan pendientes la validación del usuario en la Raid (T9) y la medición de FPS (T4/S3). Sin commits.

L45. Usuario: «sigue sin gustarme como se ve la niebla en general y el cono de vision se siente raro, las luces y el sistema quedaron perfectos, pero esto es algo aparte, vamos a dar la tarea por cerrada, y seguimos buscando como mejorar el game feel».
Cierre: el usuario aprueba las luces y el sistema. El aspecto general de la niebla y la sensación del cono quedan como tema de game feel aparte. Sin medir: FPS (S3). Sin commits; el árbol tiene cambios ajenos que no conviene incluir (layouts del minimapa, fuente de TMP, sets de animación, TimeManager.asset y ProjectSettings.asset). No se actualizó HacknPlan.
L46. Usuario: «ok commitea esto por ahora sirve». Commits en New-Testing: 0ec203b0 herramienta del Editor; c062c29c 1.043 normal maps; ee6c2f04 visibilidad, luces que aclaran la niebla, orientación compartida, borde suave y radio cercano 3,5, con documentación; 489f4d02 250 luces en Catacumbas y luz global 0,1. Excluidos: ProjectSettings.asset, TimeManager.asset, AttackAnimationSets (x3), layouts del minimapa, fuente de TMP, .anim sin cambios de contenido, CatacombsDoor.prefab sin diff, Assets/Art/Props/Test y Artifacts/. Sin push.
