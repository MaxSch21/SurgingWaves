# AGENTS.md – Surging Waves

## Project Overview

**Surging Waves** is a Unity 6.3 LTS PCVR project using HDRP, OpenXR and the XR Interaction Toolkit.

The experience takes place on a boat at sea. Gameplay mechanics and interactions will be defined incrementally throughout development.

The agent primarily assists with **C# programming, Unity systems and project management**.

## Rules

1. **Modular & Clean Code**
   - Write simple, modular, readable and maintainable C# scripts.
   - Expose relevant settings through `[SerializeField]` in the Inspector.
   - Avoid unnecessary complexity, dependencies and overengineering.

2. **Plan Before Implementing**
   - Analyze the existing project and systems before proposing a solution.
   - Consider compatibility with the current architecture.
   - Explain the proposed approach briefly and ask for approval before implementing new systems.
   - Ask questions when requirements are unclear. Do not assume behavior.

3. **Preserve Existing Systems**
   - Never refactor, replace or modify existing systems unless explicitly requested.
   - Keep modifications limited to the requested scope.

4. **Tests & Validation**
   - Do not create or execute tests, builds or validation procedures unless explicitly requested.

5. **No Unapproved Helper Scripts**
   - Do not create additional helper scripts, utilities or components without prior approval.

6. **Scene Restrictions**
   - Only modify the currently active Unity scene.
   - Never edit other scenes unless explicitly instructed.

7. **Prefabs**
   - Reuse existing Prefabs whenever possible.
   - Ask before creating new Prefabs or modifying shared Prefabs.

8. **Project Configuration**
   - Do not modify packages, dependencies, project settings or render pipeline settings without permission.

9. **Git & Files**
   - Never commit, push, merge or switch branches without explicit permission.
   - Do not delete, rename or reorganize existing files without approval.
   - Preserve Unity `.meta` files and asset references.

## Development Workflow

1. Understand the requested mechanic or feature.
2. Inspect relevant existing scripts and components.
3. Propose a minimal, modular solution.
4. Wait for approval before implementing new systems.
5. Implement only the agreed changes.
6. Update the documentation below when mechanics are added or changed.

## Mechaniken

List only mechanics that are actually implemented.

- Radar und Steuerstand: Lenken, Gasgeben, virtuelle Fahrt mit bewegtem Meer sowie Radar mit Fisch- und Monsterkontakten; Details unter „Radar und Steuerstand“.

## Dokumentation

Keep documentation **short, clear and without repetition**.

For each implemented mechanic, document:
- **Purpose:** What does it do? (1–2 sentences)
- **Implementation:** Main scripts, Prefabs and relevant scene objects.
- **Configuration:** Important Inspector settings, if applicable.
- **Sources:** Relevant project file paths and links to external documentation used.

Update existing entries instead of creating duplicate explanations. Never document planned functionality as already implemented.

### Radar und Steuerstand

**Purpose:** Der Steuerstand verbindet die Bootssteuerung mit einer Radaransicht der Umgebung. Die Fahrt wird virtuell berechnet und durch die Bewegung des Meeres um das ortsfeste Boot sichtbar gemacht.

**Implementation:** Das Prefab `RadarHelmStation` enthält die Steuerung, Navigation und Kontaktverwaltung sowie `SteeringWheel`, `ThrottleLever` und `RadarScreen`.

- **Lenken und Gasgeben:** `BoatControls` speichert die Lenkung von −1 bis +1 und die drei Gasstufen 0 / 1 / 2 für Stopp / halbe / volle Leistung; Rückwärtsfahrt ist nicht implementiert. `FpsHelmInteractor` und `HelmMouseControl` ermöglichen die Bedienung mit gedrücktem E und Mausbewegung. In VR übersetzt `HelmVRControl` die Bewegung des greifenden Controllers um den jeweiligen Drehpunkt in dieselben Steuerwerte. Rad und Hebel bleiben montiert; ihre `Visual`-Kinder drehen um die lokale Z- beziehungsweise X-Achse. Die Stellung bleibt beim Loslassen erhalten. Der Hebel besitzt Raststufen mit Hysterese in VR und Bewegungswiderstand bei Mausbedienung.
- **Fahrt und bewegtes Meer:** `RadarNavigation` berechnet Geschwindigkeit, virtuelle Position und Kurs in `FixedUpdate`. Beschleunigen und Abbremsen erfolgen schrittweise. Lenken ändert den Kurs nur bei eingelegter Gasstufe und vorhandener Geschwindigkeit. Die HDRP-`WaterSurface`-Objekte werden entgegen der Fahrt verschoben und beim Lenken entgegengesetzt um das Boot gedreht; dabei bewegen sich das Wellenfeld und seine Oberflächendetails. Der Bootsrumpf selbst wird durch dieses System weder verschoben noch gedreht. Ohne explizite Zuweisung werden Wasserflächen derselben Szene gesucht.
- **Radaranzeige:** `RadarDisplay` und `RadarGraphic` zeichnen eine World-Space-UI mit drei Entfernungsringen, Raster, umlaufendem Scanstrahl und Bootssymbol im Zentrum. Oben entspricht der aktuellen Fahrtrichtung; Kontakte und Kompassbeschriftungen berücksichtigen den virtuellen Kurs. Der Status zeigt Reichweite, Geschwindigkeit, Kurs und Gas in Prozent. Nur aktive Kontakte innerhalb der gemeinsamen Reichweite werden angezeigt. Der Scanstrahl ist eine Animation; Kontakte werden unabhängig davon laufend aktualisiert.
- **Angezeigte Objekte:** `RadarContactManager` verwaltet virtuelle 2D-Kontakte, keine automatisch erkannten oder erzeugten Fisch-/Monster-GameObjects. Fische erscheinen als grüne Punkte und werden im Bereich vor dem Boot zufällig platziert. Erreichte Fische werden gezählt, lösen `onFishReached` aus und erhalten eine neue Position; weit zurückgelassene Kontakte werden ebenfalls neu platziert. Bei Bedarf wird ein Fischkontakt wieder vor das Boot gesetzt. Monster erscheinen als orange Rauten und werden gezielt über `AddMonsterAhead(distance, bearing)` oder das Komponenten-Kontextmenü `Spawn Monster Ahead` hinzugefügt. Beim Erreichen werden sie gezählt, deaktiviert und lösen `onMonsterReached` aus; weitere Ereignisfolgen müssen separat angebunden werden.

**Configuration:** Gemeinsame Werte stehen in `Assets/Scripts/Settings/Radar/RadarSettings.asset`:

- Navigation: `maxSpeed = 8 m/s`, `acceleration = 3`, `deceleration = 5`, `turnSpeed = 4°/s`; die Gasstufen ergeben Zielgeschwindigkeiten von 0 / 4 / 8 m/s.
- Meer: `waterTravelEnabled` und `rotateWaterWithSteering` aktivieren Verschiebung und Drehung; `waterSpeedMultiplier = 1` bestimmt die zusätzliche Meeresbewegung relativ zur virtuellen Fahrt. Wasserflächen und optionaler Boots-Rigidbody werden an `RadarNavigation` zugewiesen.
- Kontakte und Anzeige: `range = 100 m`, `fishCount = 4`, `spawnNear = 0.5`, `spawnFar = 0.85`, `spawnHalfAngle = 55°`, `reachDistance = 7 m`, `sweepDegreesPerSecond = 45`. Farben sind ebenfalls konfigurierbar. Monsterentfernung und -peilung für das Kontextmenü stehen separat am `RadarContactManager`.
- Bedienung: `wheelMaxAngle = 120°`, `leverMaxAngle = 35°`; Mausreichweite und -empfindlichkeit sowie Rastwiderstand stehen im Settings-Asset. VR verwendet am `HelmVRControl` `leverNotchHysteresis = 0.1` und `minimumGrabRadius = 0.025 m`. Beide Bedienelemente benötigen einen Collider, einen kinematischen Rigidbody ohne Gravitation und ein `XR Grab Interactable` mit `Select Mode = Single`, deaktiviertem Position-/Rotation-/Scale-Tracking und deaktiviertem `Throw On Detach`. Die Interaction-Maske muss mit den Controller-Interactors übereinstimmen. `HelmMouseControl` benötigt `Settings`, `Controls` und ein direkt untergeordnetes `Visual`.

**Sources:** [Radar-Scripts](Assets/Scripts/Radar/), [RadarHelmStation-Prefab](Assets/Prefabs/Radar/RadarHelmStation.prefab), [RadarSettings-Asset](Assets/Scripts/Settings/Radar/RadarSettings.asset), [Unity XR Grab Interactable 3.3](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.3/manual/xr-grab-interactable.html).
