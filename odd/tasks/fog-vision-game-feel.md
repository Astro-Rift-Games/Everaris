# Game feel de niebla y visión
Prototipo para comparar con la niebla actual, pedido por el usuario después de cerrar TASK-306. No hay un ítem de HacknPlan asociado.
Arquitectura: Project Grimhold/Docs/Architecture/VisibilityArchitecture.md. Tracker anterior: odd/tasks/task-306-normal-maps.md (L45).

## Specs
S1. «Oscuridad en lugar de niebla (recomendado)»: «Lo no visto se oscurece y pierde color en vez de cubrirse de gris; las luces revelan con su propio brillo.»
S2. «Más visión cercana en 360°»: «Ampliar el radio alrededor del personaje; el cono queda solo para ver lejos.»
S3. «Desvanecimiento gradual en el borde»: «Los enemigos se vuelven transparentes al acercarse al borde del cono en lugar de aparecer de golpe.»
S4. Prototipo comparable con la niebla actual; el usuario solo mira y valida.

## Tasks
T1 | S1, S4 | Estilo de oscuridad | rechazado por el usuario; código y configuración retirados
T2 | S2 | Ampliar el radio de visión cercana | NearRadius 2 → 3,5; pruebas en verde | ee6c2f04
T3 | S3 | Desvanecimiento continuo de entidades dinámicas en el borde del cono | verificado: banda dentro del cono | ee6c2f04
T4 | S4 | Capturas comparativas en una Raid y validación del usuario | oscuridad rechazada; radio y borde pendientes de validación en juego
T5 | — | Actualizar VisibilityArchitecture.md | hecho para lo conservado (radio y borde) | ee6c2f04

## Log
L1. Usuario: «sigue sin gustarme como se ve la niebla en general y el cono de vision se siente raro, las luces y el sistema quedaron perfectos, pero esto es algo aparte, vamos a dar la tarea por cerrada, y seguimos buscando como mejorar el game feel».
L2. Qué le molesta de la niebla: «Parece un velo gris pegado encima», «El movimiento o el ruido se ven raros», «Se ve como el haz de una linterna». Agrega: «Ademas de lo que marque, no se integra con el ambiente, y ahora con las luces se siente mas raro aun».
L3. Qué le molesta del cono: «Los enemigos aparecen y desaparecen de golpe». Agrega: «se siente raro para jugar, pero no se de que otra forma solucionar el tema de la vision en este juego con esta perspectiva».
L4. Para el prototipo elige las tres opciones: «Oscuridad en lugar de niebla (recomendado)», «Más visión cercana en 360°» y «Desvanecimiento gradual en el borde».
L5. Cambia el aspecto aprobado de la niebla y la regla de visión: es decisión del usuario y requiere actualizar la arquitectura si se aprueba. Mapeo técnico en curso.

L6. Mapa (explore): niebla unlit en la capa superior Fog; la textura de capas de cámara del 2D Renderer estaba apagada; VFX es la capa justo debajo de Fog. NearRadius 2 en el asset, sin pruebas que fijen ese valor. IVisibilityTarget tiene un solo implementador.
L7. T1: VisionFogStyle (Mist o Darkness) en VisionFogSettings, con DarknessBrightness 0,35, DarknessDesaturation 0,75 y DarknessTint (0,75; 0,8; 1). En modo Darkness el shader lee _CameraSortingLayerTexture y oscurece, desatura y tiñe el mundo según 1 − clear; clear incluye la visión y las luces, así que las zonas iluminadas muestran su brillo real. Renderer2D: m_UseCameraSortingLayersTexture=1, límite en VFX (-316428783). VisionFogSettings.asset queda en Darkness para el prototipo; Mist conserva el aspecto anterior para comparar. Sin RED: es visual y no tiene prueba automatizable.
L8. T2: VisibilitySettings.asset NearRadius 2 → 3,5. Valor de diseño ajustable; sin RED porque es configuración.
L9. T3: RED (EdgeFactor, constructor de VisionQuery y ConeEdgeFactor ausentes). Implementado: VisionConeRules.EdgeFactor, VisionQuery con edgeSoftDegrees (ve hasta la mitad exterior de la banda), IVisibilityTarget.EdgeFactor, EntityVisibilityPresenter.EdgeFactor (los estáticos devuelven 1) y EntityVisibilitySystem multiplica alfa × borde, guardando el último alfa aplicado. VisibilitySettings.ConeEdgeSoftDegrees 20. Los renderers que no son sprite siguen apareciendo de golpe (forceRenderingOff).

L10. Pruebas: VisionConeEdgeFactor 6/6, VisionConeRules 8/8, VisibilityFadeRules 7/7, VisionFacingSmoother 5/5, VisionFogRules 18/18, VisionFogLightRules 4/4, DungeonLightProp 8/8, ProjectileStuckVisual 29/29, InventoryPassiveEffects 12/12, VisibilityPrefabCoverage 42/42. CatacombsSceneComposition 7/9, con los 2 fallos que ya existían. En Play Mode: VisionOcclusion 15/15, DungeonDoor 19/19, ImmobilizeEffect 16/16. Verificación independiente lanzada (riesgo ítems 3 y 6). Siguiente: que el usuario lo pruebe en una Raid y compare Darkness con Mist.

L11. Usuario: «no, se ve horrible».

L12. Decisión del usuario: «Volver a la niebla anterior (Mist)», «Conservar el radio cercano de 3,5» y «Conservar el desvanecimiento en el borde». Sobre lo que se ve horrible: «La oscuridad: demasiado negra o sucia», «El corte entre lo visible y lo oscuro», «Cómo quedan las luces» y «Todo el concepto».
L13. Verificación independiente del prototipo. La banda centrada en el borde mostraba enemigos hasta 10° fuera del hueco de la niebla, en contra de VisibilityArchitecture:194. Con cono de 360° o más angosto que la banda, el factor no llegaba a 1. Al salir del cono por un costado, la entidad se iba sin el periodo de gracia. El ajuste global del renderer encarecía todas las escenas.
L14. Cambios. Se quitó la oscuridad: se borraron VisionFogStyle.cs y su .meta, se sacaron los campos de VisionFogSettings, del presenter y del shader, y Renderer2D.asset volvió a HEAD (textura de capas desactivada). Se volvió a guardar VisionFogSettings.asset, que perdió las claves huérfanas. El asset muestra valores de niebla que no cambió el agente (InsideDensity 0,25 → 0,5, NoiseScale 0,35 → 0,25, NoiseStrength 0,9 → 0,95, EdgeSoftness 1,2 → 1,57, EdgeBreakup 0,45 → 0,1, FarFogDensity 0,35 → 0,5); probablemente son ajustes del usuario en Play Mode y se conservan. EdgeFactor: la banda queda dentro del cono, de 1 en half−soft a 0 en el borde. Un cono de 360° no se desvanece y uno más angosto que la banda la reduce a su mitad. VisionQuery ya no ensancha el cono. RED: 5 de 8 fallaban; GREEN en curso.

L15. GREEN: VisionConeEdgeFactor 8/8, VisionConeRules 8/8, VisibilityFadeRules 7/7, VisionFacingSmoother 5/5, VisionFogRules 18/18, VisionFogLightRules 4/4, DungeonLightProp 8/8, ProjectileStuckVisual 29/29, VisibilityPrefabCoverage 42/42. CatacombsSceneComposition 7/9, con los 2 fallos que ya existían. En Play Mode: VisionOcclusion 15/15, DungeonDoor 19/19, ImmobilizeEffect 16/16. Renderer2D.asset sin cambios frente a HEAD. Shader de niebla sin errores. Documentación actualizada: regla de visión, opacidad, mejora 8 y validación.
L16. Usuario: «ok commitea esto por ahora sirve». Radio cercano, borde suave y documentación quedan en ee6c2f04. La oscuridad se descartó antes del commit. Pendiente: validar en juego. El aspecto de la niebla sigue abierto.
