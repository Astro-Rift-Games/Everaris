# Audio Architecture

Este documento define las responsabilidades, los límites arquitectónicos y la estructura técnica del sistema de audio del proyecto.

## Responsabilidades del Sistema de Audio

El audio es puramente una **capa de presentación**. Al igual que los VFX y la animación visual:
- Reacciona ante eventos del juego (ataques, daño, recolección) y lee estado replicado (salud, estados activos).
- Jamás es fuente de verdad ni muta estado de red o emite eventos que afecten al gameplay autoritativo.
- No usa RPCs propias. Se reproduce localmente al observar estado simulado (`[Networked]`) o eventos de presentación, desde `Render()`/`LateUpdate()`, nunca desde ticks predichos o resimulados.
- Un observador que entra tarde (late join, entrada al área de interés) o un objeto restaurado por Host Migration no reproduce eventos ya ocurridos: cada presentador siembra su línea base al hacer spawn o al habilitarse.

## Estructura de Configuraciones (Data-Driven)

Los sets de audio son **ScriptableObjects** jerárquicos configurables por diseño:

- **CustomClip**: struct serializable con un arreglo `AudioClip[]` y parámetros de mezcla (`Volume`, `Pitch`, `SpatialBlend`, `Loop`). `GetRandomClip()` elige una variación. Un `Volume` o `Pitch` serializado en 0 se interpreta como 1.
- **EntityAudioConfig**: base de `PlayerAudioConfig`, `EnemyAudioConfig`, `SpecterAudioConfig` (subtipo de `EnemyAudioConfig`), `WeaponAudioConfig` y `MusicAudioConfig`. Serializa una lista `clave -> CustomClip` y expone `TryGetClip(string key, out CustomClip clip)` en O(1) (búsqueda sin distinguir mayúsculas).
- **AudioKeys**: constantes de todas las claves; el código no usa literales. `AudioKeySpec` declara por tipo de config qué claves son obligatorias, cuáles alcanzan con una de un grupo y cuáles son opcionales:

| Config | Obligatorias | Al menos una de | Opcionales |
|---|---|---|---|
| Player | `TakeDamage`, `Step` | — | `Death` |
| Enemy | `TakeDamage`, `Step` | `Attack` / `Shoot` | `Death` |
| Specter | igual que Enemy | igual que Enemy | `Death`, `Channel`, `Detonate`, `Reappear` |
| Weapon | — | `Swing` / `Shoot` / `Cast` | `Charge`, `Reload`, `Attack`, `Hit`, `Block` |
| Music | `Town`, `Raid` | — | — |

- Una clave opcional ausente es silenciosa: agregar el clip al ScriptableObject alcanza para que suene, sin tocar código. Los enemigos no tienen mecánica de recarga, por eso no usan `Reload`.
- Tests EditMode (`AudioConfigKeyValidationTests`, `AudioPresenterPrefabConfigTests`) fallan si falta una clave obligatoria, si hay claves desconocidas o duplicadas, si una escena pide una pista inexistente o si un prefab con presentador de audio no tiene config.

## AudioManager y Ciclo de Vida

- Singleton (`AudioManager.Instance`) con `DontDestroyOnLoad`. El prefab `Prefabs/Audio/AudioManager.prefab` está en **cada escena de entrada** (todas las escenas de build: MainMenu, Lobby-Town, Gameplay, CatacombsMap); la primera instancia sobrevive y destruye las copias posteriores. `AudioManagerSceneCompositionTests` lo exige. No hay bootstrap global.
- Pool preinstanciado de `AudioSource` (por defecto 16) asignados al grupo SFX del mixer. Si el pool se satura se reutiliza la fuente que empezó a sonar primero (`SfxVoiceSelector`).
- Volumen por canal (`MasterVolume`, `MusicVolume`, `SFXVolume`, parámetros expuestos del mixer) persistido por `AudioVolumeSettingsStore` en PlayerPrefs y controlado desde `SettingsPanelView` mediante `IAudioVolumeControl`.
- Crossfade y fade de música: los parámetros existen en la API pero todavía no están implementados.

### Regla del mixer

Todo audio pasa por `GrimholdAudioMixer` para que respondan los sliders de volumen: los `AudioSource` de prefabs llevan grupo de mixer y no se usa `AudioSource.PlayClipAtPoint`. `AudioMixerRoutingTests` lo verifica. Sin `AudioManager` no hay audio (no hay fallbacks fuera del mixer).

## Política espacial (2D / 3D)

- **2D**: UI, HUD, diálogo, música y avisos globales (fases de presión del dungeon).
- **3D**: todo lo que ocurre en el mundo (combate, enemigos, puertas, cofres, santuarios, consumibles). Los sfx afectan el gameplay porque permiten ubicar fuentes.
- La cámara está en z = -10; `PlanarAudioListener` mantiene el único `AudioListener` activo en el plano de juego (z = 0) siguiendo X/Y de la cámara, así la atenuación depende solo de la distancia en X/Y. Está en `Gameplay Camera.prefab` y en la cámara de Lobby-Town; MainMenu es solo UI.
- Alcance central en `AudioManager` (ajustable en el Inspector): atenuación lineal, distancia mínima 2 y máxima 20 unidades, aplicada al pool, a `CreateSfxSource` y a fuentes propias mediante `ApplySpatialSettings` (por ejemplo la puerta).
- `PlaySfx(..., SfxSpatialMode)`: `Default` respeta el `SpatialBlend` del `CustomClip` (un `AudioClip` suelto es 2D), `Flat2D` y `World3D` lo fuerzan. En los assets, las entradas de música tienen `SpatialBlend` 0 y todas las demás 1.

## AudioPresenters (Conectores)

1. **Armas (`WeaponAudioPresenter` + `WeaponAudioCue`)** en el prefab del jugador:
   - Resuelve la config del arma equipada vía `PlayerWeaponEquipmentNetworkController` (o una config de respaldo).
   - Al confirmarse un ataque (`AttackPerformed`) reproduce `Charge` una vez durante el windup confirmado (se corta al cancelar, interrumpir o soltar) y al soltar `Swing` (melee) o `Cast`/`Shoot` (rango, `Cast` si existe).
   - `Reload` sigue el estado de recarga replicado del arma (`WeaponReloadState`), no el cooldown.
   - **Impacto del atacante**: `CombatFeedbackResolved` (RPC de State Authority a Input Authority) reproduce `Attack` (o `Hit` si `Attack` no existe) contra personajes y `Block` contra escenario; repeticiones de la misma secuencia se ignoran.
   - **Impacto para observadores**: el State Authority escribe `[Networked] LastObservedImpact` (secuencia, víctima, punto, arma) en cada impacto confirmado con daño. `PlayerCombatNetworkController` eleva `ObservedImpactPresented` desde `Render()` solo en peers sin Input Authority, una vez por secuencia nueva y sembrado en `Spawned`. El presentador lo reproduce en 3D en el punto de impacto. **Limitación aceptada**: los impactos que un observador no recibió entre dos snapshots (mismo tick, pérdida de paquetes, intervalo de envío) se fusionan en uno; el atacante escucha cada uno. Si la víctima está fuera del área de interés del observador se reproduce `Block`.
2. **Enemigos (`EnemyAudioPresenter` + `EnemyAnimationAudioListener`)**: `AttackPerformed` reproduce `Attack` (melee) o `Shoot` (rango). Daño y muerte vienen de `HealthAudioObserver`. El listener, junto al `Animator`, expone `PlayAudioEvent(string)` y `PlayFootstep()` (`Step`) para AnimationEvents.
3. **Espectro (`SpecterAudioPresenter` + `SpecterAudioCueObserver`)**: observa el estado replicado del Espectro y reproduce `Channel` (canalización), `Detonate` (detonación) y `Reappear` (reaparición) una vez por transición, sin repetir el estado vigente al habilitarse. Hoy usa clips placeholder en `SpecterAudioConfig.asset`.
4. **Jugador (`PlayerAudioPresenter` + `PlayerAnimationAudioListener`)**: daño y muerte vía `HealthAudioObserver`; pasos con la clave `Step` (AnimationEvents o timer opcional).
5. **`HealthAudioObserver`** (C# puro, compartido por jugador y enemigo): la primera muestra válida es solo línea base; una baja de salud reproduce `TakeDamage`; la muerte reproduce `Death` una vez por muerte; revivir (incluido el ciclo de Downed) re-arma la muerte sin reproducir daño; muestras de objetos inválidos o despawneados se ignoran; la observación sigue aunque no haya `AudioManager`, así no hay sonidos "atrasados".
6. **Música (`SceneMusicPresenter`)**: en Lobby-Town (`Town`), Gameplay y CatacombsMap (`Raid`) reproduce en loop la pista de la escena en `Start()`. Al descargarse su escena detiene la pista que inició si sigue sonando, para que no pase a la pantalla de carga ni a la escena siguiente; la música que otro sistema inició después (por ejemplo la del colapso) se conserva.
7. **Consumibles**: la bomba explosiva marca `HasDetonated` (replicado), presenta la explosión desde `Render()` una vez por peer y se despawnea 1 s después, para que el marcador llegue a los clientes antes de que el objeto desaparezca. Un cliente que pierde paquetes por más de ese tiempo o recibe la bomba ya detonada no la escucha. La zona de humo persiste ~8 s y conserva su RPC de detonación.

## Pendiente

- Advertencia local de audio en las grietas del colapso del dungeon (Game Design): sin contenido de audio todavía.
- Reemplazar los placeholders del Espectro cuando existan los audios definitivos.
