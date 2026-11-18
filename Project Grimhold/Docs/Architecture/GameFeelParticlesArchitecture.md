# Arquitectura de Partículas de Game Feel

Este documento detalla la arquitectura para el feedback visual basado en partículas al ocurrir eventos de gameplay (impactos, daño, destrucción y consumo).

## Separación de Responsabilidades

El sistema sigue la estricta separación entre **Simulación** y **Presentación** del proyecto.
Las partículas son puramente visuales, no afectan colisiones, no tienen autoridad de red y no se sincronizan como `NetworkObject`.

El mecanismo utilizado se basa en la **Observación** (a través de `LateUpdate()`) para componentes que persisten, y **Suscripción a Eventos** para eventos fugaces o que derivan en un despawn inmediato.

### Mecanismos de Detección

1. **Daño a Characters y Breakables**
   Los componentes de presentación observan las propiedades públicas sincronizadas (`Health`, `IsDestroyed`) desde sus `LateUpdate()`. Cuando se detecta un cambio válido (ej: reducción de vida y estado de vida activo), reproducen el sistema de partículas referenciado dentro de su propio Prefab.
   - **Beneficio**: Reutiliza la vida útil del objeto. No requiere instanciar partículas (cero allocations).

2. **Impactos de Proyectil en Personajes y Escenario**
   Los proyectiles desaparecen (`Runner.Despawn()`) en el mismo tick en el que impactan, por lo que un sistema de partículas hijo no podría sobrevivir.
   `NetworkProjectile` emite `ImpactResolved` durante un despawn normal con estado válido, indicando posición y si fue geometría. `ProjectileImpactParticlePresenter` ya no filtra por geometría: instancia el impacto también en personajes para **todos** los proyectiles, incluidos los que no tienen etiqueta de arma. Su `_tintPalette` opcional toma el `DamageType` sincronizado; sin paleta conserva el color del prefab. Los impactos por defecto conservan looping y stop action del asset: deben estar configurados para terminar y limpiarse. Un perfil visual puede sustituir este impacto con un clon one-shot. No se emite durante shutdown/migración.
   - **Beneficio**: El efecto sobrevive a la entidad que lo originó.

3. **Expiración y estelas de proyectil**
   `NetworkProjectile` marca `[Networked] ExpiryConsumed` antes del despawn por alcance o vida útil. En `Despawned`, si no hubo impacto, emite `Expired(posición, dirección)` una sola vez; los proxies leen la causa autoritativa, no la infieren de su posición interpolada. Sin estado válido, durante shutdown del runner (incluida la migración), o ante un despawn externo sin causa terminal, no se emite expiración. `ProjectileExpiryParticlePresenter` escucha el evento y reproduce un fizzle opcional orientado al vuelo; la instancia desactiva looping y usa `StopAction.Destroy`.
   `ProjectileTrailDetacher` separa únicamente hijos visuales asignados (TrailRenderer/ParticleSystem) en despawns normales, detiene la emisión y programa su destrucción tras la vida restante, incluida la estela de partículas. Durante teardown siguen siendo hijos del proyectil. No prolonga la simulación ni busca componentes en la jerarquía; captura las partículas vivas con una asignación por despawn, no por frame.

4. **Consumo de Ítems**
   Se utiliza el evento `ConsumeConfirmed` ya existente en `PlayerConsumableNetworkController`. El Presenter reacciona instanciando las partículas asociadas desde el scriptable object de la definición del consumible, en la posición local del jugador.

## Impactos de arma confirmados

Solo el **cliente del atacante** emite partículas específicas en el `HitPoint` confirmado (superficie de contacto, no centro del objetivo), ajustado a la cuadrícula de 1/16 unidades. `PlayerCombatNetworkController.Render` consume la confirmación secuenciada del RPC State Authority → Input Authority; dentro del deduplicado llama a `MeleeImpactParticleEmitter` (nombre histórico) por separado de hitstop/reacción. Acepta `ConfirmedImpact`, `IsWeaponImpact`, daño aplicado positivo, arma resoluble por catálogo y objetivo character registrado distinto del atacante: incluye melee y ranged etiquetados. Un golpe mortal también emite si el objetivo sigue registrado; breakables, escenario, daño rechazado/cero y fuentes sin etiqueta no entran en esta ruta. Hitstop y congelación del Animator del objetivo siguen siendo exclusivamente melee (`IsMeleeImpact`); reacción y partículas usan los perfiles opcionales del arma confirmada.

`WeaponAttackTag` contiene slot, secuencia aceptada, índice de catálogo + 1 y tipo de ataque. Melee lo aporta mediante un scope síncrono del controlador; ranged lo transporta por `ProjectileSpawnRequest` → cuatro enteros `[Networked]` de `NetworkProjectile` → `DamageRequest` → `RecordResolvedDamage`. `RecordResolvedDamage` toma `Request.DamageType` y el RPC conserva tanto ese tipo como `IsFatal` en `CombatPresentationEvent` (Physical por defecto para compatibilidad). No se deduce el tipo desde el Equipment actual. `CopyStateFrom` preserva la etiqueta en Host Migration; habilidades, trampas, enemigos o proyectiles legacy sin metadatos degradan silenciosamente al feedback genérico, sin inventar el arma actualmente equipada.

### Configuración visual

`WeaponDefinition.Presentation.ImpactParticles` referencia opcionalmente un `MeleeImpactParticleProfile` validado. Un perfil nulo conserva las partículas genéricas. Los nombres `MeleeImpactParticleProfile`, `MeleeImpactParticleEmitter` y los métodos de coalescencia/color se conservan por compatibilidad, no como filtro de ranged. Los tres perfiles de `Assets/Scriptable Objects/Combat/ImpactParticles/` reutilizan la clasificación inicial melee; ranged puede asignar cualquiera opcionalmente, sin implicar que sus assets ya estén configurados:

| Perfil | Armas | Cantidad | Velocidad (u/s) | Cono | Tamaño (px) | Vida (s) |
| --- | --- | --- | --- | --- | --- | --- |
| Light | Rondel Dagger, Magic Cinquedea | 5–7 | 13–15 | 70° | 2 | .32–.40 |
| Medium | Arming Sword, Magic Sword, Rapier, Spellbook | 8–11 | 15–18 | 80° | 2–3 | .36–.46 |
| Heavy | Long Sword, Zweihander, Great Hammer | 14–18 | 17–21 | 90° | 3–4 | .40–.52 |

Los perfiles conservan una chispa cálida clara `(1, .92, .65, 1)` como fallback. Antes de emitir, el controlador consulta `CharacterDamageParticlePresenter.TryGetMeleeImpactColor(out color)` en el objetivo. `_overrideMeleeImpactColor` habilita el color serializado `_meleeImpactColor`: esqueleto base y ranged heredado `( .82, .82, .85, 1)`, slime azul `(.208, .631, .757, 1)`, verde `(.349, .757, .208, 1)` y rojo `(.757, .208, .369, 1)`. Los slimes guardan overrides de variante; NetworkPlayer no habilita el override. Sin color propio del objetivo, `_impactTintPalette` opcional del controlador toma `CombatPresentationEvent.DamageType`: un color no blanco **reemplaza**, no multiplica, el color del perfil (Magical violeta arcano para Magic Sword/Cinquedea). Un override del objetivo siempre gana; paleta nula, entrada ausente o blanca conserva la chispa del perfil. `Emit` recibe un `Color?` opcional y escribe `EmitParams.startColor`, sin modificar el perfil compartido ni las partículas genéricas. Las gotas nacen sobre la superficie cercana de un cuerpo de ~1 u y se comportan como una salpicadura viscosa: la velocidad de la tabla es la de salida y el prefab aplica drag lineal `k = 8` (Limit Velocity over Lifetime, sin límite ni dampen), de modo que `v(t) = v0·e^(−k·t)` y el recorrido es `v0/k·(1 − e^(−k·t))`. La gota más lenta debe salir del cuerpo (1 u) dentro del 40% de su vida y asentarse a ≥ 1.25 u (20 px), pasando la mayor parte de su vida sobre el suelo; lo verifica `MeleeImpactParticleProfileTests`. Se conserva el test de contraste del color fallback (no dominante en rojo), antes camuflado con el flash rojo genérico; los colores de objetivo son deliberados y la reacción de arma local ahora usa una silueta blanca unlit. Cantidad, velocidad, dispersión, tamaño, PPU, vida y color son ajustables sin cambiar gameplay. Cada partícula toma una dirección dentro de ±medio cono alrededor de `HitDirection`; dirección cero/no finita produce un círculo completo. Los tamaños son píxeles enteros / PPU (16). La validación exige rangos ordenados y finitos, cantidad 1–256, tamaño 1–4 px, vida .001–.6 s y color normalizado.

Las partículas de impacto del proyectil son la capa visible para **todos los peers**; `WeaponDefinition.Presentation.ImpactParticles` sigue siendo una capa opcional solo del atacante. Para un único burst ranged, dejar ese campo sin asignar; asignarlo significa apilar ambas capas deliberadamente. Las partículas genéricas de Health siguen su contrato de coalescencia local, no se eliminan globalmente.

### Perfiles visuales de proyectil

`WeaponDefinition.ProjectileVisual` referencia opcionalmente un `ProjectileVisualProfile` (sprite, controller, tinte, prefab de estela, fizzle e impacto). En cada peer, `ProjectileVisualProfilePresenter` resuelve la etiqueta replicada con `LootDefinitionCatalog.TryGetByIndex(indexPlusOne - 1)`; Equipment es alternativa explícita solo sin catálogo asignado, nunca el arma actualmente equipada. Identidad ausente/inválida o perfil nulo conserva defaults silenciosamente.

El controller del perfil tiene prioridad y habilita el Animator; un perfil solo de sprite deshabilita el Animator de flecha. `UseDamageTypeTint` con paleta válida gana al tinte explícito; si no aplica ninguno, conserva el color original. Impacto y fizzle no nulos sustituyen independientemente a los presenters por defecto, sin duplicarlos: los clones del perfil son one-shot. La estela visual se registra mediante `ProjectileTrailDetacher.Register`, sin NetworkObject ni componentes de gameplay. Los eventos terminales consumen el perfil antes de restaurar sprite, controller/estado del Animator y color; disable y nuevo vuelo también restauran defaults. El proveedor Fusion actual destruye proyectiles: esto no afirma que exista pooling.

`StickOnImpact` en el perfil deja un `StuckProjectileVisual` local: copia del sprite de vuelo con la punta recortada (solo la parte trasera `StickVisibleFraction` del largo queda visible fuera de la superficie, para que parezca incrustada; el pivote queda en el borde de corte, ubicado `StickPenetration` —por defecto 0— más allá del punto de impacto en la dirección de vuelo, más un hundimiento extra en impactos oblicuos descrito abajo; el `Sprite` recortado es propiedad del visual y se destruye con él), visible `StickHoldSeconds`, con fade de `StickFadeSeconds` y luego destruida. El borde de corte se coloca en el **punto de superficie** del contacto terminal (`ImpactSurfacePoint`, replicado solo para presentación y registrado por la autoridad; no el centro del proyectil, que queda un collider de radio antes), con fallback a la posición del evento. En cuerpos usa el target y el offset terminal replicados (`punto de superficie − raíz del cuerpo`, solo presentación) para quedar sobre el cuerpo en cada peer, lo sigue, respeta su opacidad de visión y usa `StickBodySortingLayer`; desaparece si el cuerpo se destruye. Además se **ancla** localmente (una vez por impacto) al `Collider2D` hallado bajo el punto clavado con la máscara de impacto del proyectil (dentro del cuerpo si lo hay; se ignoran colliders propios y de orbes): sigue al anchor si no hay cuerpo y expira de inmediato si el collider se destruye, se deshabilita o su objeto se desactiva (p. ej. objetos rompibles, que solo deshabilitan colliders y renderers). Sin collider resoluble (p. ej. tilemaps compuestos) queda sin ancla. En impactos **oblicuos** el borde de corte, perpendicular al vuelo, dejaría un extremo separado de la superficie (≈1 px a 45°): el presenter calcula localmente la normal con un `Physics2D.Raycast` corto a lo largo del vuelo sobre el punto de superficie (misma máscara de impacto, triggers incluidos, ignorando colliders propios/orbes y prefiriendo el anchor; funciona con colliders compuestos y tilemaps; sin estado replicado) y hunde el borde `s = (StickEdgeWidth / 2) · |p·n| / max(|f·n|, 0.35)` extra, acotado a `[0, StickEdgeWidth]` (`f` = vuelo, `p` = perpendicular, `n` = normal). `StickEdgeWidth` es el ancho visible del proyectil en el corte, en unidades de mundo (0 desactiva; sin normal hallada se trata como impacto frontal); el recorte conserva `s` más de largo para que el largo visible fuera de la superficie no cambie. Las intercepciones de orbe no dejan flecha; en geometría queda fija en el mundo.

### Carga y ráfaga de release

`Presentation.WindupChargeVfx` referencia un perfil `WindupChargeVfx` opcional; `WindupChargeWindow` usa `[0, ScheduledWindupSeconds)` en el reloj confirmado, sin temporizador de gameplay nuevo. `WindupChargePresentation` mantiene Main/Off Hand separados. Requiere visual Cast Flash y sigue su origen resuelto (`pose.Position + Center`) después de evaluar el grip. Aplica tinte del arma confirmada, sorting de la pose y escala del prefab con rampa lineal .25→1; AimStance completamente tensado multiplica por 1.5 (defaults configurables, incluido Staff).

Release corta y limpia la carga; cancelación, reloj inválido, muerte/Downed, consumo, disable o reemplazo también la cortan. Resume reconstruye solo la fase restante, sin ráfaga; una carga terminada no reaparece al retroceder el reloj. `Presentation.ReleaseBurstParticles` puede acompañar slash/thrust/cast una vez en la posición/rotación/sorting VFX, con el mismo tinte: empieza al abrir la ventana visual relativa al release (`ReleaseLeadSeconds`, incluido el adelanto de .025 s del cast), no cambia el deadline autoritativo. Una notificación fuera de la ventana se consume sin replay; `AttackReleaseBurstLifetime` espera a que terminen también los hijos. Sin assets opcionales no se añade este efecto ni se modifica daño, mana, velocidad, vida o timing.

Los números de daño usan `_tintPalette` opcional en `CombatFeedbackPresenter`: tipo confirmado para no fatales (Magical violeta), blanco/nulo conserva el TMP original y el estilo de kill gana siempre. Cada entrada reutilizada del pool restablece color/escala; no recolorea el flash genérico de Health.

### Reutilización, tiempo y limpieza

Los perfiles comparten `Assets/Prefabs/VFX/MeleeImpactParticles.prefab`: cuadrados billboard sin textura, material **Sprite-Unlit-Default** de URP reutilizado (la iluminación 2D no apaga el color del objetivo en el dungeon oscuro), sorting **VFX/0** y alpha opaca (1) hasta el 80% de la vida, con fade solo al final. No se añaden sprites ni materiales. La cuadrícula y los tamaños mantienen la escala de 16 PPU; la rasterización final depende de la cámara y requiere validación visual.

El emisor es una clase plana propiedad del controlador: crea perezosamente **una instancia por prefab**, sin padre y con simulación mundial; reutiliza `EmitParams` sin allocations por contacto. Posición/velocidad por partícula permiten contactos simultáneos en múltiples objetivos sin mover partículas anteriores. Máximo 256 partículas vivas por emisor: saturar ese límite puede omitir partículas adicionales. No se instancia/destruye por golpe; `Spawned` reinicia y `Despawned`/`OnDestroy` liberan las instancias. El efecto no depende del target ni de su Animator tras emitirlo.

Las partículas avanzan con tiempo normal (`Time.deltaTime`), no con el reloj mapeado del ataque: **continúan durante el hitstop**, sin congelarse ni rebobinar. No se cambia `Time.timeScale`, daño, knockback, detección, cooldown, timings, hitstop ni hit reaction; no hay eventos globales ni estado de red para las partículas de contacto; la etiqueta de vuelo y la causa de expiración sí son estado `[Networked]` del proyectil.

### Coalescencia del daño genérico

Tras emitir, el controlador llama a `CharacterDamageParticlePresenter.AbsorbConfirmedMeleeImpact(appliedDamage)` del objetivo, únicamente en ese cliente. Health replica un agregado sin fuente, por lo que la correlación es **por cantidad** dentro de una ventana serializada (por defecto .15 s):

- Si un burst genérico se reprodujo dentro de la ventana y su reducción de Health es explicable por el daño de arma confirmado (`reducción <= AppliedDamage`), se limpia **ese burst**. Si la reducción fue mayor, incluía otra fuente y el burst genérico se conserva.
- En caso contrario se arma un presupuesto de absorción igual a `AppliedDamage`; varios impactos de arma confirmados sobre el mismo objetivo dentro de la ventana suman su daño, porque pueden llegar en una sola reducción observada.
- Una reducción posterior dentro de la ventana se absorbe solo si el presupuesto restante la explica, y lo consume. Una reducción mayor que el presupuesto (daño independiente incluido), o fuera de la ventana, reproduce las partículas genéricas y descarta el presupuesto.
- `OnDisable` limpia el estado local. Otros peers nunca reciben esta llamada y mantienen las partículas genéricas. Las demás fuentes siguen el camino Health existente.

Límite residual: un daño independiente que llegue dentro de la ventana con una cantidad menor o igual al presupuesto de arma pendiente no se distingue del golpe confirmado y se coalesce. No se bloquean todas las reducciones durante una ventana ni se modifica el estado autoritativo.

### Pruebas y comprobaciones manuales

EditMode cubre validación, catálogo/pesos, muestreo (punto, dirección, cantidad, tamaño y vida), reutilización/limpieza del emisor y absorción anterior/posterior/caducada/de un uso. PlayMode comprueba el release/RPC equipado y el impacto ranged etiquetado con perfiles opcionales, sin hitstop, además del filtrado/deduplicado en Render, múltiples contactos y limpieza al despawn sin cambiar Health. La cobertura de presentación de proyectiles distingue impacto, expiración/fizzle, estelas y teardown; las fuentes sin etiqueta conservan el feedback genérico. La aprobación estética, lectura Light/Medium/Heavy y verificación Host + clientes/proxies reales siguen siendo manuales; los tests de un runner no prueban esos casos visuales multijugador.

#### M7 wiring — asignación y comprobaciones pendientes

- [ ] Asignar `WeaponDefinition.ProjectileVisual`; añadir `ProjectileVisualProfilePresenter` a la raíz del proyectil con catálogo, SpriteRenderer/Animator hijos explícitos, paleta opcional y `ProjectileTrailDetacher`. Asignar aparte la paleta del presenter de impacto por defecto. Las estelas deben ser solo visuales.
- [ ] Comprobar override de controller/sprite (sin Animator de flecha sobrescribiéndolo), tintes, sustitución sin doble impacto/fizzle, colisión con personaje/pared, expiración y limpieza de estelas en Host/Client.
- [ ] Asignar `Presentation.WindupChargeVfx` / `ReleaseBurstParticles`; comprobar punta, profundidad, rampa .25→1, Staff tensado ×1.5, cancelación/resume y ambas manos. Falta una aserción dedicada de pose de ráfaga thrust; verificarla visualmente junto al slash.
- [ ] Asignar `AttackVfxTintPalette.asset` a `_impactTintPalette` del controlador y `_tintPalette` del feedback; comprobar prioridad del color de objetivo, magia violeta, kill y reutilización del pool.
- [ ] Dejar ranged `Presentation.ImpactParticles` vacío para un burst de proyectil único, o aceptar explícitamente el apilado local. Comprobar capas visibles en atacante, víctima y proxies; la coalescencia genérica sigue siendo local.
- [ ] Asignar Charge/Cast opcionales en audio; comprobar corte por mano, resume silencioso, fallback Shoot y Attack > Hit (Block sin cambios); véase el contrato de audio en Player Combat.
- [ ] Validar migración real con proyectiles en vuelo (Host y al menos dos Clients): etiquetas conservadas, fallback genérico sin errores y ningún fizzle/estela desprendida por teardown.

No se introducen assets nuevos ni predicción cliente del ataque o del proyectil. El cableado y la aprobación estética siguen pendientes; estos campos opcionales no implican assets ya asignados.

Véase también [PlayerCombatArchitecture — Weapon impact particles](PlayerCombatArchitecture.md#weapon-impact-particles).

## Helper Core

### `ParticleEffectPlayer`
Clase estática que estandariza las dos aproximaciones:
- `PlayInPlace(ParticleSystem, Vector2)`: Ubica el sistema y lo reproduce.
- `InstantiateAndPlay(ParticleSystem prefab, Vector2)`: Genera un nuevo objeto autónomo a partir del Prefab.

## Principios Cumplidos

- **Sin Event Bus Global**: Mantiene flujos de comunicación y acoplamientos explícitos.
- **Data-Driven**: Cada GameObject (Character, Proyectil, Item) define sus propios Prefabs de partículas en el inspector.
- **Sin Sincronización Innecesaria**: Solo se propaga la mínima señal necesaria, los clientes resuelven la presentación leyendo el último snapshot garantizado por Fusion.
