window.SFX_GUIDE_DATA = {
  stories: [
    {
      id: "US-85",
      hacknplanId: 831,
      week: 1,
      board: "Sprint 18",
      title: "US-85 — Producción de Clips SFX Base de Combate, Espectro y Loop de Raid",
      objective: "Crear los efectos de sonido (SFX) base para el combate principal, el Espectro de Catacumbas y los eventos clave de la Dungeon, reemplazando los sonidos genéricos actuales.",
      tone: [
        "Aventura medieval fantástica oscura en catacumbas húmedas de piedra (tensión táctica, NO horror ni jumpscares).",
        "Sonidos físicos con peso real (acero, cuero, madera, hueso, piedra) y magia contenida/etérea. Evitar sintetizadores futuristas o brillos arcade.",
        "Claridad en combate grupal: evitar agudos estridentes que saturen cuando suenan varias acciones juntas."
      ],
      delivery: "Solo archivos .wav finales (limpios, sin silencio muerto al inicio/fin ni clipping, con volumen parejo entre clips). La integración en el motor la hace Programación."
    },
    {
      id: "US-86",
      hacknplanId: 832,
      week: 2,
      board: "Backlog",
      title: "US-86 — Producción de Clips SFX de Habilidades MVP, Entorno e Interfaz",
      objective: "Completar la librería de efectos de sonido (SFX) para las 9 habilidades activas, trampas, barriles/jarrones rompibles, herramientas de exploración e interfaz medieval del juego.",
      tone: [
        "Mantener la misma identidad medieval fantástica oscura de la US-85.",
        "En menús, comercio e inventario, buscar materialidad orgánica y táctil (pergamino, madera, hierro forjado, monedas metálicas, vidrio), evitando pops digitales o sonidos de app moderna."
      ],
      delivery: "Solo archivos .wav finales (limpios, sin silencio muerto al inicio/fin ni clipping, con volumen parejo respecto a la US-85). La integración en el motor la hace Programación."
    }
  ],
  tasks: [
    {
      id: "SFX-01",
      hacknplanId: 833,
      storyId: "US-85",
      week: 1,
      priority: "High",
      title: "SFX-01 — Combate Base, Armas, Jugador y Enemigos (Slime y Esqueleto)",
      objective: "Reemplazar los sonidos genéricos de combate físico y mágico (todas las armas del catálogo), pasos/daño del jugador y los enemigos base (Slime y Esqueleto).",
      tone: [
        "Seco, físico y contundente; aventura oscura subterránea en piedra húmeda (sin gore exagerado ni tono caricaturesco).",
        "Para armas mágicas, magia contenida/oscura o rúnica (sin láser sci-fi).",
        "Evitar voces muy marcadas en el jugador (el personaje no tiene clase fija ni voz única)."
      ],
      delivery: "Archivos .wav individuales y normalizados, con nombres claros (ej. SFX_Sword_Swing_01.wav).",
      items: [
        {
          id: "sfx01-melee-light",
          gif: "sfx01-melee-light.gif",
          title: "Corte ligero / ágil (Daga y Rapier)",
          status: "gameplay",
          badges: ["Melee 1M", "x3–4 variaciones"],
          clips: [
            "Swing rápido y fino al cortar el aire (x3–4).",
            "Estocada o tajo ligero en cuerpo y armadura (x3–4).",
            "Choque seco al golpear muro/piedra (Block, x2–3)."
          ],
          note: "Incluye Rondel Dagger, Training Dagger y Rapier."
        },
        {
          id: "sfx01-melee-sword",
          gif: "sfx01-melee-sword.gif",
          title: "Corte medio / pesado (Espada corta y Espadón)",
          status: "gameplay",
          badges: ["Melee 1M / 2M", "x3–4 variaciones"],
          clips: [
            "Swing con peso de acero (diferenciar 1H ágil vs Zweihander 2H amplio, x3–4).",
            "Impacto en cuerpo o armadura (x3–4).",
            "Choque metálico contra muro/piedra (Block, x2–3)."
          ],
          note: "Armas base de corte y barrido."
        },
        {
          id: "sfx01-melee-hammer",
          gif: "sfx01-melee-hammer.gif",
          title: "Impacto contundente pesado (Martillo Grande)",
          status: "gameplay",
          badges: ["Melee 2M", "x2–3 variaciones"],
          clips: [
            "Swing grave y pesado al revolear (x2–3).",
            "Impacto aplastante en cuerpo/armadura y golpe en piedra/muro (Block, x2–3)."
          ],
          note: "GreatHammerWeaponDefinition: ataque lento de mayor peso."
        },
        {
          id: "sfx01-melee-hybrid-magic",
          gif: "sfx01-melee-hybrid-magic.gif",
          title: "Espadas híbridas mágicas (MagicSword y MagicCinquedea)",
          status: "gameplay",
          badges: ["Melee Híbrida", "x2–3 variaciones"],
          clips: [
            "Tajo de acero con matiz arcano sutil en el aire (x2–3).",
            "Impacto mixto filo + energía mágica contenida (x2–3)."
          ],
          note: "Ayuda a distinguir sus impactos híbridos en combate (y contra el Espectro)."
        },
        {
          id: "sfx01-shield-block",
          gif: "sfx01-shield-block.gif",
          title: "Bloqueo defensivo con Escudo",
          status: "gameplay",
          badges: ["Defensa Off-Hand", "x2–3 variaciones"],
          clips: [
            "Golpe seco mitigado contra escudo de madera/metal con resonancia corta (x2–3)."
          ],
          note: "Implementado en PlayerShieldDefenseNetworkController (distinto al choque en muro)."
        },
        {
          id: "sfx01-bow",
          gif: "sfx01-bow.gif",
          title: "Arcos (Compuesto y Largo — sin recarga)",
          status: "gameplay",
          badges: ["Distancia 2M", "Sin recarga"],
          clips: [
            "Tensión de cuerda y madera al apuntar (Charge, x2).",
            "Disparo: liberación de cuerda + silbido corto de flecha (x3–4).",
            "Impacto de flecha en cuerpo (x3) y clavado en piedra/pared (x2–3)."
          ],
          note: "En código CompoundBow y LongBow tienen _usesReload = 0."
        },
        {
          id: "sfx01-crossbow",
          gif: "sfx01-crossbow.gif",
          title: "Ballesta Ligera (única arma con recarga)",
          status: "gameplay",
          badges: ["Distancia 2M", "Recarga ~1,3 s"],
          clips: [
            "Disparo mecánico seco y potente de virote (Shoot, x3).",
            "Recarga mecánica de cuerda y trinquete (~1,3 s, x2–3)."
          ],
          note: "Comparte impactos o usa golpe firme de virote."
        },
        {
          id: "sfx01-wand-staff",
          gif: "sfx01-wand-staff.gif",
          title: "Varita Mágica y Cetro / Bastón Mágico",
          status: "gameplay",
          badges: ["Mágica Distancia", "x3–4 variaciones"],
          clips: [
            "Carga/canalización arcana breve al apuntar (Charge/Cast, x2).",
            "Disparo de proyectil mágico ágil o potente (x3–4).",
            "Impacto mágico apagado en cuerpo y en pared (x2–3)."
          ],
          note: "MagicWand, TrainingWand y MagicStaff comparten arquitectura ranged."
        },
        {
          id: "sfx01-spellbook",
          gif: "sfx01-spellbook.gif",
          title: "Libro de Hechizos",
          status: "gameplay",
          badges: ["Mágica Proximidad", "x2–3 variaciones"],
          clips: [
            "Activación / descarga mágica de corto alcance al abrir o conjurar (Cast, x2–3).",
            "Impacto arcano en área/proximidad (x2–3)."
          ],
          note: "SpellbookWeaponDefinition usa alcance corto (2,5 tiles) con flash mágico."
        },
        {
          id: "sfx01-weapon-swap",
          gif: "sfx01-weapon-swap.gif",
          title: "Cambio de arma (Weapon Set)",
          status: "gameplay",
          badges: ["Acción", "x1–2 variaciones"],
          clips: [
            "Desenfunde rápido de cuero y acero al alternar entre Set A y Set B (x1–2)."
          ],
          note: "Corto y limpio para no estorbar el ritmo de pelea."
        },
        {
          id: "sfx01-player-footsteps",
          gif: "sfx01-player-footsteps.gif",
          title: "Pasos del Jugador según material de botas",
          status: "gameplay",
          badges: ["Locomoción", "x4 por material"],
          clips: [
            "Suela blanda / Tela (sin botas / Pueblerino y sets de Mago): pisada suave y discreta (x4).",
            "Botas de Cuero (sets de Ranger): pisada firme y seca de cuero curtido (x4).",
            "Botas de Metal / Placas y Malla (sets de Caballero): pisada pesada con choque/tintineo corto y contenido (x4)."
          ],
          note: "El suelo del mapa es siempre piedra, por eso el material del calzado diferencia las pisadas."
        },
        {
          id: "sfx01-player-damage-death",
          gif: "sfx01-player-damage-death.gif",
          title: "Jugador: Recibir daño y Muerte",
          status: "gameplay",
          badges: ["PlayerAudioConfig", "x3 daño + muerte"],
          clips: [
            "Recibir daño (TakeDamage, x3): golpe físico seco + exhalación/quejido breve neutro.",
            "Muerte (Death): caída física breve y exhalación final contenida."
          ],
          note: "El GIF muestra Knockback (daño recibido). La animación propia de Muerte queda en el debe (Defeated.anim hoy usa Idle); sin clip separado de agotamiento de estamina."
        },
        {
          id: "sfx01-slime",
          gif: "sfx01-slime.gif",
          title: "Slime (Green / Red / Blue)",
          status: "gameplay",
          badges: ["Enemigo PvE", "x3 cada evento"],
          clips: [
            "Desplazamiento viscoso denso y húmedo (serio, nunca caricaturesco, x3).",
            "Ataque cuerpo a cuerpo gelatinoso: estiramiento/golpe húmedo rápido (x3).",
            "Daño recibido: chapoteo espeso (x3).",
            "Muerte: estallido húmedo que se disipa (x3)."
          ],
          note: "SlimeAttackConfig es cuerpo a cuerpo estándar (sin efecto ácido)."
        },
        {
          id: "sfx01-skeleton",
          gif: "sfx01-skeleton.gif",
          title: "Esqueleto (lanzador de huesos)",
          status: "gameplay",
          badges: ["Enemigo PvE", "x3 cada evento"],
          clips: [
            "Pasos: traqueteo seco y liviano de huesos al caminar (x3).",
            "Lanzamiento de hueso con la mano: revoleo/impulso corto + silbido breve de hueso girando (x3).",
            "Tomar/preparar siguiente hueso: chasquido o roce breve al agarrar otro hueso (x2–3).",
            "Daño: crujido de hueso astillado (x3).",
            "Muerte: desarme de huesos cayendo y golpeando el suelo (x3)."
          ],
          note: "No usa arco ni ballesta: arroja huesos manualmente."
        }
      ]
    },
    {
      id: "SFX-02",
      hacknplanId: 834,
      storyId: "US-85",
      week: 1,
      priority: "Normal",
      title: "SFX-02 — Paquete Sonoro del Espectro de Catacumbas",
      objective: "Darle identidad sonora completa al Espectro de Catacumbas para que el jugador lea sus ataques y reapariciones en combate sin confundirlos con otros peligros del nivel.",
      tone: [
        "Criatura funeraria fría y etérea (aire helado, susurro espectral, tela flotante, timbre vítreo/arcano apagado). Tono de amenaza oscura, NO jumpscare de terror.",
        "Regla clave de mezcla: trabajar en registro medio-alto etéreo. Nunca usar subgraves sísmicos o truenos, reservados para el Colapso."
      ],
      delivery: "Archivos .wav individuales y normalizados, con nombres claros por evento.",
      items: [
        {
          id: "sfx02-specter-float",
          gif: "sfx02-specter-float.gif",
          title: "Flotación y presencia sutil",
          status: "gameplay",
          badges: ["Ambiente / Loop opcional"],
          clips: [
            "Murmullo frío de aire y tela flotante muy discreto (loop corto opcional)."
          ],
          note: "No debe tapar pasos ni alertas del mapa."
        },
        {
          id: "sfx02-specter-channel-detonate",
          gif: "sfx02-specter-channel-detonate.gif",
          title: "Canalización AoE (~1,25 s), Detonación (~0,3 s) y Cancelación",
          status: "gameplay",
          badges: ["Ataque AoE 1,5 m", "Registro medio-alto"],
          clips: [
            "Canalización (~1,25 s): subida etérea creciente y tensa que avise salir del círculo.",
            "Detonación (~0,3 s): estallido mágico frío, concentrado y de cola corta.",
            "Canalización cancelada: corte seco y disipación rápida del aura si lo interrumpen."
          ],
          note: "Timings verificados en SpecterRules.cs y SpecterVisualPresenter.cs."
        },
        {
          id: "sfx02-specter-damage",
          gif: "sfx02-specter-damage.gif",
          title: "Daño físico inmune vs Daño efectivo",
          status: "gameplay",
          badges: ["Feedback táctico", "x2–3 cada uno"],
          clips: [
            "Daño físico inmune (incorpóreo en reposo, persecución o reaparición, x2–3): roce hueco al atravesar niebla/ceniza.",
            "Daño efectivo (daño mágico visible, o físico al canalizar, detonar o recuperarse 1,5 s, x2–3): impacto sólido arcano/espectral."
          ],
          note: "Debe distinguir de inmediato cuándo el golpe físico no hace daño y cuándo sí."
        },
        {
          id: "sfx02-specter-teleport-death",
          gif: "sfx02-specter-teleport-death.gif",
          title: "Desvanecimiento (~0,4 s), Reaparición (~0,4 s) y Muerte",
          status: "gameplay",
          badges: ["Teletransporte y cierre"],
          clips: [
            "Desvanecimiento (~0,4 s): succión rápida hacia el vacío espectral.",
            "Reaparición (~0,4 s): materialización breve aguda/etérea (con ataque inicial claro).",
            "Muerte: disolución espectral descendente con liberación de energía fría."
          ],
          note: "Tras 0,5 s invisible reaparece cerca del objetivo elegido."
        }
      ]
    },
    {
      id: "SFX-03",
      hacknplanId: 835,
      storyId: "US-85",
      week: 1,
      priority: "High",
      title: "SFX-03 — Dungeon Core (Colapso, Extracción, Abatido), Mundo y Consumibles",
      objective: "Crear los efectos para los eventos de tensión global de la Dungeon (Colapso, Extracción, Abatido), interactuables del escenario y consumibles de combate.",
      tone: [
        "Peso arquitectónico antiguo (piedra que cruje, retumbo profundo subterráneo), mística rúnica medieval, vidrio de alquimia y pólvora/fuego seco.",
        "Las alertas del Colapso llevan el peso grave/sísmico del juego (para no cruzarse con el Espectro).",
        "Las señales del Santuario y las Bombas permiten a rivales cercanos escucharlas y ubicarse."
      ],
      delivery: "Archivos .wav individuales y normalizados, sin silencios muertos al inicio o al final.",
      items: [
        {
          id: "sfx03-collapse-pressure",
          gif: "sfx03-collapse-pressure.gif",
          title: "Presión y Colapso de la Dungeon",
          status: "mixed",
          badges: ["DungeonPressure", "Subgrave / Sísmico"],
          clips: [
            "Alerta de refuerzos / inestabilidad: resonancia grave en piedra + pulso arcano oscuro.",
            "Presión crítica: bramido subterráneo más intenso con caída de polvo/roca.",
            "Inicio de Colapso (fase de 2 min): estruendo estructural profundo + ruptura sobrenatural.",
            "Cierre absoluto (fin de los ~17 min): golpe final cataclísmico de colapso definitivo.",
            "Temblor periódico lejano (x2–3): sacudida corta de mampostería.",
            "Grieta del suelo (diseño Doc 20, cableado pendiente): vibración local de advertencia + erupción de piedra y energía."
          ],
          note: "El GIF exportado muestra las etapas reales de Grietas (Stage 0–4); el temblor global y alertas requieren captura en Play Mode. Refuerzos, Crítico, Colapso y PeriodicShake ya existen en DungeonPressureAudioPresenter / AmbientPresenter."
        },
        {
          id: "sfx03-extraction-sanctuary",
          gif: "sfx03-extraction-sanctuary.gif",
          title: "Santuario de Extracción (4 cues exactas)",
          status: "gameplay",
          badges: ["ExtractionSanctuaryPresenter", "4 señales"],
          clips: [
            "Santuario asignado: campanada o acorde rúnico solemne informativo.",
            "Inicio de ritual: activación energética profunda audible a distancia (alerta PvP).",
            "Ritual completado: sello rúnico ascendente y resolutivo.",
            "Ritual cancelado: caída de tono breve y apagado de energía al interrumpirse."
          ],
          note: "Corresponde exactamente a los 4 slots de ExtractionSanctuaryPresenter.cs."
        },
        {
          id: "sfx03-downed-revive",
          gif: "sfx03-downed-revive.gif",
          title: "Abatido y Reanimación",
          status: "gameplay",
          badges: ["RaidDownedHudPresenter", "Doc 13"],
          clips: [
            "Caer abatido: impacto amortiguado + bajón breve de aire/latido.",
            "Reanimación / Auto-reanimación completada: liberación vital luminosa y alivio breve.",
            "Reanimación interrumpida: corte seco descendente al cancelar o recibir daño.",
            "(Opcional GDD Doc 13) Pulso sutil de canalización al reanimar/auto-reanimar y aviso breve de Resolución Acelerada (~5 s finales)."
          ],
          note: "Los 3 primeros tienen slot directo en RaidDownedHudPresenter.cs."
        },
        {
          id: "sfx03-doors-chests",
          gif: "sfx03-doors-chests.gif",
          title: "Puertas y Cofres del mapa",
          status: "gameplay",
          badges: ["Escenario", "Madera y hierro"],
          clips: [
            "Puertas de madera pesada con herrajes: apertura y cierre (x2–3).",
            "Cofres: destrabe de cerrojo de hierro + tapa pesada de madera al abrir (x2)."
          ],
          note: "NetworkDoor y NetworkLootContainerInteractable."
        },
        {
          id: "sfx03-potions-mana",
          gif: "sfx03-potions-mana.gif",
          title: "Pociones y Fragmento de Maná",
          status: "gameplay",
          badges: ["CON_HEALTH / MANA", "2,5 s y 1 s"],
          clips: [
            "Poción de Vida y Poción de Maná (~2,5 s): descorche y trago líquido en vidrio con matiz vital o arcano.",
            "Fragmento de Maná (~1 s): quiebre/consumo rápido y cristalino."
          ],
          note: "Activación interrumpible por daño en las pociones de 2,5 s."
        },
        {
          id: "sfx03-throwables",
          gif: "sfx03-throwables.gif",
          title: "Bomba Explosiva, Bomba de Humo y Daga Arrojadiza",
          status: "gameplay",
          badges: ["Consumibles rápidos", "Quick Slots"],
          clips: [
            "Bomba Explosiva: explosión seca con gravilla y pólvora (slot en código) + lanzamiento y mecha chisporroteando (~1,5 s) opcional.",
            "Bomba de Humo: lanzamiento breve (~0,5 s) + estallido sordo y silbido denso de humo alquímico (nube 8 s).",
            "Daga Arrojadiza: corte aéreo fino + clavado seco en carne o piedra (x2–3)."
          ],
          note: "NetworkExplosiveBomb, NetworkSmokeZone y ThrowingDaggerProjectile."
        }
      ]
    },
    {
      id: "SFX-04",
      hacknplanId: 836,
      storyId: "US-86",
      week: 2,
      priority: "Normal",
      title: "SFX-04 — Paquete Sonoro de las 9 Habilidades Activas MVP",
      objective: "Darle una firma sonora propia a cada una de las 9 habilidades activas del juego para que se distingan al instante de los ataques básicos con arma.",
      tone: [
        "Cada habilidad se lee por su naturaleza: física contundente, trampa metálica, arcana oscura (sin láser sci-fi) o vital/rúnica.",
        "Las 5 habilidades con preparación/casteo (Golpe Sísmico, Proyectil Arcano, Restauración, Purificación y Drenaje Vital) pueden interrumpirse por Stun o Knockback: separar preparación de resolución."
      ],
      delivery: "Archivos .wav individuales y normalizados, coherentes con el volumen de la US-85.",
      items: [
        {
          id: "sfx04-charge",
          gif: "sfx04-charge.gif",
          title: "1. Embestida",
          status: "gameplay",
          badges: ["FUE 10 · Stamina", "Instantánea"],
          clips: [
            "Arranque explosivo en carrera.",
            "Choque contundente contra enemigo/armadura (y Stun si aplica daño) o golpe seco contra pared."
          ],
          note: "ChargeAbilityBehaviour: sin casteo previo; frena al primer obstáculo o enemigo."
        },
        {
          id: "sfx04-seismic-strike",
          gif: "sfx04-seismic-strike.gif",
          title: "2. Golpe Sísmico",
          status: "gameplay",
          badges: ["FUE 15 · Stamina", "0,75 s prep."],
          clips: [
            "Carga pesada descendente (0,75 s preparación).",
            "Golpe expansivo radial en la piedra (distinto a las grietas del Colapso)."
          ],
          note: "SeismicStrikeAbilityBehaviour: área AoE centrada en el caster con knockback."
        },
        {
          id: "sfx04-trap",
          gif: "sfx04-trap.gif",
          title: "3. Trampa (habilidad táctica)",
          status: "gameplay",
          badges: ["DES 10 · Stamina", "Instantánea"],
          clips: [
            "Click metálico discreto al apoyarla en el suelo.",
            "Clack hueco corto de rechazo si la posición está bloqueada.",
            "Cierre violento de trampa al pisarla un enemigo (inmoviliza) + pulso de daño periódico cada 1 s."
          ],
          note: "TrapAbilityBehaviour + NetworkTrap (hasta 3 simultáneas por caster)."
        },
        {
          id: "sfx04-arcane-projectile",
          gif: "sfx04-arcane-projectile.gif",
          title: "4. Proyectil Arcano",
          status: "gameplay",
          badges: ["INT 10 · Maná", "0,5 s prep."],
          clips: [
            "Carga oscura luminosa (0,5 s).",
            "Vuelo silbante esquivable e impacto energético en enemigo o pared."
          ],
          note: "ArcaneProjectileAbilityBehaviour: dirección fija al iniciar."
        },
        {
          id: "sfx04-protective-orbs",
          gif: "sfx04-protective-orbs.gif",
          title: "5. Orbes Protectores",
          status: "gameplay",
          badges: ["RES 10 · Maná", "Instantánea · 8 orbes"],
          clips: [
            "Invocación rítmica breve al desplegar los 8 orbes (15 s duración).",
            "Desvío cristalino-arcano al bloquear proyectil o ataque enemigo (x2–3).",
            "Choque al dañar enemigo por contacto (zumbido orbital muy tenue opcional)."
          ],
          note: "ProtectiveOrbsAbilityBehaviour / NetworkEffect."
        },
        {
          id: "sfx04-empower",
          gif: "sfx04-empower.gif",
          title: "6. Potenciar",
          status: "gameplay",
          badges: ["SUE 10 · Maná", "Instantánea · 10 s buff"],
          clips: [
            "Pulso ascendente de acero energizado al activar buff en área sobre el equipo."
          ],
          note: "EmpowerAbilityBehaviour: +30% daño en ataques básicos con arma."
        },
        {
          id: "sfx04-restoration",
          gif: "sfx04-restoration.gif",
          title: "7. Restauración",
          status: "gameplay",
          badges: ["VIT 10 · Maná", "1 s prep."],
          clips: [
            "Canalización cálida y serena (orgánica, sin arpa exagerada, 1 s).",
            "Liberación luminosa de curación en área sobre aliados."
          ],
          note: "RestorationAbilityBehaviour: curación plana (no cura Abatidos)."
        },
        {
          id: "sfx04-purification",
          gif: "sfx04-purification.gif",
          title: "8. Purificación",
          status: "gameplay",
          badges: ["VIT 7 + RES 7 · Maná", "0,5 s casteo · 5 s prot."],
          clips: [
            "Vibración limpia y cortante tipo sello/campana que barre estados negativos.",
            "Halo sutil al otorgar 5 s de protección contra nuevos estados purificables."
          ],
          note: "PurificationAbilityBehaviour: también limpia la Inmovilización de Trampa."
        },
        {
          id: "sfx04-vital-drain",
          gif: "sfx04-vital-drain.gif",
          title: "9. Drenaje Vital + Interrupción común",
          status: "gameplay",
          badges: ["VIT 7 + INT 7 · Maná", "1 s prep."],
          clips: [
            "Carga sombría de absorción sobre enemigos alrededor (1 s) + retorno vital sordo hacia el lanzador.",
            "Interrupción común (x1–2): corte breve mágico/físico cuando Stun o Knockback cancelan cualquiera de las 5 preparaciones."
          ],
          note: "Opuesto en tono al pulso cálido de Restauración."
        }
      ]
    },
    {
      id: "SFX-05",
      hacknplanId: 837,
      storyId: "US-86",
      week: 2,
      priority: "Normal",
      title: "SFX-05 — Trampas de Escenario, Rompibles, Utilidades e Inventario",
      objective: "Producir los sonidos mecánicos de trampas, objetos destructibles reales del mapa, herramientas de exploración e interacciones físicas de inventario y loot.",
      tone: [
        "Mecánico y antiguo: hierro oxidado, mecanismos de piedra, cerámica y madera quebradiza, cuero y monedas en mochila.",
        "Las herramientas rúnicas (Brújula y Detector) deben sonar antiguas y místicas, nunca como beeps electrónicos o radares sci-fi."
      ],
      delivery: "Archivos .wav individuales y normalizados.",
      items: [
        {
          id: "sfx05-spikes-trap",
          gif: "sfx05-spikes-trap.gif",
          title: "Trampa de Pinchos (suelo)",
          status: "gameplay",
          badges: ["BaseTrap + SpikesTrap", "Con Telegraphing"],
          clips: [
            "Advertencia previa (Telegraphing): resorte/piedra tensándose bajo el suelo.",
            "Salida rápida de pinchos metálicos (x2–3).",
            "Retracción lenta raspando hierro al volver a cooldown."
          ],
          note: "Se activa por proximidad (OnTriggerEnter2D -> Telegraphing -> Active)."
        },
        {
          id: "sfx05-dart-trap",
          gif: "sfx05-dart-trap.gif",
          title: "Trampa de Dardos (pared)",
          status: "gameplay",
          badges: ["BaseTrap + DartTrap", "Ráfaga x3"],
          clips: [
            "Señal mecánica breve de advertencia (Telegraphing) en el muro.",
            "Disparo mecánico oculto en ráfaga de 3 dardos (x3).",
            "Impacto de dardo fino en carne o pared (x2–3)."
          ],
          note: "Activación por proximidad; NO usa placa de presión."
        },
        {
          id: "sfx05-breakables",
          gif: "sfx05-breakables.gif",
          title: "Objetos rompibles: Barriles y Jarrones",
          status: "gameplay",
          badges: ["BreakableObject", "Madera y cerámica"],
          clips: [
            "Barriles (madera): golpe parcial con crujido (x2) + estallido completo de tablas y astillas (x2–3).",
            "Jarrones (cerámica): golpe seco parcial en barro (x2) + rotura completa de cerámica en trozos (x2–3)."
          ],
          note: "Solo existen barriles y jarrones en Catacumbas (no hay cajas ni urnas)."
        },
        {
          id: "sfx05-lockpick",
          gif: "sfx05-lockpick.gif",
          title: "Ganzúa (CON_LOCKPICK)",
          status: "gameplay",
          badges: ["Interacción ~3 s", "Cofres cerrados"],
          clips: [
            "Roce metálico fino mientras forcejea la cerradura (~3 s).",
            "Clic satisfactorio de apertura lograda y corte corto de cancelación si se mueve."
          ],
          note: "LockpickChannelRules + LockedChest."
        },
        {
          id: "sfx05-rune-compass",
          gif: "sfx05-rune-compass.gif",
          title: "Brújula Rúnica (UTL_RUNE_COMPASS)",
          status: "gameplay",
          badges: ["Utilidad · 5 s", "Quick Slot"],
          clips: [
            "Repique/activación rúnica cristalina antigua + resonancia suave de orientación hacia el Santuario."
          ],
          note: "Reemplaza el s_provisionalChimeClip actual en RuneCompassPointerEffect.cs."
        },
        {
          id: "sfx05-arcane-detector",
          gif: "sfx05-arcane-detector.gif",
          title: "Detector Arcano (UTL_ARCANE_DETECTOR)",
          status: "gameplay",
          badges: ["2 s carga + 5 s escaneo", "Radio 10 m"],
          clips: [
            "Zumbido armónico creciente durante los 2 s de carga.",
            "Pulso rúnico breve al revelar elementos arcanos y corte apagado si se interrumpe."
          ],
          note: "Si el jugador se mueve o lo interrumpen se cancela sin cooldown."
        },
        {
          id: "sfx05-void-bell",
          gif: "sfx05-void-bell.gif",
          title: "Campana del Vacío (TRE_VOID_BELL)",
          status: "gameplay",
          badges: ["Tesoro Caos", "Cada 2 s en Sprint"],
          clips: [
            "Campanada metálica hueca, apagada e inquietante (audible a 15 m mientras corre en Sprint)."
          ],
          note: "Reemplaza el timbre provisional en PlayerInventoryPassiveEffectsController.cs."
        },
        {
          id: "sfx05-inventory-loot",
          gif: "sfx05-inventory-loot.gif",
          title: "Inventario, Equipamiento y Loot",
          status: "gameplay",
          badges: ["Interacciones UI/Mundo", "x2–3 recogida"],
          clips: [
            "Recoger del suelo (x2–3) y soltar objeto.",
            "Mover entre inventario/baúl y dividir pila (split).",
            "Equipar arma o armadura (cuero/acero) y golpe suave de acción rechazada o sin espacio."
          ],
          note: "Aplica en Raid y Pueblo (Stash)."
        }
      ]
    },
    {
      id: "SFX-06",
      hacknplanId: 838,
      storyId: "US-86",
      week: 2,
      priority: "Normal",
      title: "SFX-06 — Interfaz Medieval (UI), Comercio, Misiones y Flujo de Partida",
      objective: "Darle a todos los menús y pantallas del juego una identidad sonora medieval orgánica y cálida, reemplazando los clicks genéricos actuales.",
      tone: [
        "Materialidad física: pergamino seco, sello de cera, madera tallada, hierro de forja, monedas reales y cristal rúnico suave.",
        "Cero beeps digitales, pops plásticos de celular o fanfarrias arcade. Sonidos cortos, ágiles y descansados."
      ],
      delivery: "Archivos .wav individuales y parejos de volumen entre sí.",
      items: [
        {
          id: "sfx06-ui-navigation",
          gif: "sfx06-ui-navigation.gif",
          title: "Navegación UI Base",
          status: "gameplay",
          badges: ["UI Global", "x2–3 hover/click"],
          clips: [
            "Hover discreto (roce mínimo de papel/madera, x2–3) y Click estándar (x2–3).",
            "Volver/Cancelar, Cambiar pestaña y Error/Bloqueo (golpe sordo suave de madera, nunca bocina).",
            "Abrir y Cerrar ventana (pergamino o tapa de madera)."
          ],
          note: "Se usa en menús de Pueblo, Opciones y pantallas en Raid."
        },
        {
          id: "sfx06-merchant",
          gif: "sfx06-merchant.gif",
          title: "Mercader General (Pueblo)",
          status: "gameplay",
          badges: ["TownMerchantNPC", "Comercio"],
          clips: [
            "Compra, venta y recompra: tintineo realista de monedas de oro y bolsa (diferenciar compra de venta)."
          ],
          note: "MerchantShopUI.prefab en Lobby-Town."
        },
        {
          id: "sfx06-trainer-abilities-gems",
          gif: "sfx06-trainer-abilities-gems.gif",
          title: "Entrenador, Vendedor de Habilidades e Instalación de Gemas",
          status: "gameplay",
          badges: ["Implementado en Lobby-Town", "3 interacciones"],
          clips: [
            "Entrenador: entrega seca de arma gratuita de entrenamiento (madera/cuero/acero ligero).",
            "Vendedor de Habilidades: pulso rúnico claro al desbloquear o adquirir habilidad.",
            "Instalación de Gemas: encastre fino de gema sobre ranura con destello cristalino breve."
          ],
          note: "TownTrainerNpc, TownAbilityVendorNPC y TownGemInstallUIBinding."
        },
        {
          id: "sfx06-gdd-future-npcs",
          gif: "sfx06-gdd-future-npcs.gif",
          title: "Servicios GDD futuros (Herrero, Arcanista y Respec)",
          status: "gdd",
          badges: ["GDD Docs 04 y 23", "Futuro en Unity"],
          clips: [
            "Herrero (mejora de armas físicas y armaduras +1/+2/+3; NO existe reparación): martillazo breve de yunque + ajuste metálico.",
            "Arcanista (comercio/mejora de armas mágicas): resonancia rúnica contenida sobre cristal antiguo.",
            "Respec de atributos (costo fijo en Oro): reinicio arcano limpio + sonido breve de oro."
          ],
          note: "Aún sin UI implementada en Lobby-Town.unity, pero definidos en GDD actual."
        },
        {
          id: "sfx06-missions",
          gif: "sfx06-missions.gif",
          title: "Tablón de Misiones y Tracker en Dungeon",
          status: "gameplay",
          badges: ["TownMissionGuild", "HUD Dungeon"],
          clips: [
            "Aceptar contrato: papel fijado o sello firme sobre madera.",
            "Avance de objetivo en Dungeon: pulso discreto de tinta/pergamino que no distraiga.",
            "Completar / Reclamar recompensa: acorde medieval sobrio de logro (sin trompetas infantiles)."
          ],
          note: "MissionCanvas.prefab y RaidMissionTrackerPresenter."
        },
        {
          id: "sfx06-lobby-results",
          gif: "sfx06-lobby-results.gif",
          title: "Lobby, Grupo y Pantalla de Resultados",
          status: "gameplay",
          badges: ["TownRaidNpc", "Post-Partida"],
          clips: [
            "Confirmar 'Listo' / Entrar a la Dungeon: cerrojo pesado o cuerno/campana grave distante.",
            "Jugador entra o sale del grupo: aviso breve sutil.",
            "Resultados: Extracción Exitosa (alivio solemne contenido) vs Muerte en Dungeon (eco grave seco y contenido)."
          ],
          note: "Prepara la entrada a expedición y los cierres de partida."
        }
      ]
    }
  ]
};
