# RibFrac Visualizer — Handoff per sviluppatori

Questo documento descrive lo stato attuale del progetto Unity e cosa serve per farlo girare su una nuova macchina.

---

## Cosa è stato fatto

### Punto di partenza
Progetto Unity 6 creato da zero con template **3D (URP)** — Universal Render Pipeline.

### Pacchetti installati

Tutti gestiti tramite `Packages/manifest.json`, si scaricano automaticamente all'apertura del progetto.

| Pacchetto | Versione | Scopo |
|---|---|---|
| `com.mlavik1.easyvolumerenderer` | git (HEAD) | Rendering volumetrico CT (NIfTI) |
| `com.unity.inputsystem` | 1.19.0 | Input camera (new Input System) |
| `com.unity.render-pipelines.universal` | 17.4.0 | URP (richiesto dal volume renderer e dai materiali) |
| `com.unity.ugui` | 2.0.0 | Canvas/UI + TextMeshPro |

Il pacchetto `UnityVolumeRendering` viene clonato da GitHub al primo avvio: serve connessione internet e qualche minuto di import.

### Script custom

Tutti in `Assets/Scripts/` (e un editor utility in `Assets/Editor/`).

#### `CTLoader.cs`
Il cuore del visualizzatore. Va attaccato come componente a un GameObject vuoto in scena (es. `CTLoader`).

Fa tre cose in sequenza quando l'utente preme il pulsante **Load DATA**:

1. **Carica la CT NIfTI** (`.nii.gz`) via `UnityVolumeRendering`, la posiziona a origine, applica una transfer function clinica per le ossa (bone window) con scala di grigi. L'asse Y è flippato (`scale = (1, -1, 1)`) per correggere il verso NIfTI→Unity.

2. **Legge il JSON predizioni** (`*_predictions.json`). Il JSON deve avere la struttura:
   ```json
   {
     "public_id": "RibFracXXX",
     "n_fractures": 3,
     "fractures": [
       { "rib_id": 1, "predicted_class": "Displaced", "confidence": 0.92,
         "prob_displaced": 0.92, "prob_nondisplaced": 0.05, "prob_buckle": 0.03 }
     ]
   }
   ```
   Se il JSON non esiste, mostra "Paziente non nel test set" nel pannello.

3. **Carica le mesh OBJ** delle fratture da `objFolder`. Per ogni frattura cerca:
   - `RibFracXXX_fractureYY_mesh.obj`
   - `RibFracXXX_fractureYY_meta.json` (contiene la matrice `affine` NIfTI 4×4 row-major)
   
   Applica la trasformazione `T_vox * affine⁻¹` per allineare le mesh al volume CT. Se il meta JSON manca, le mesh vengono caricate ma potrebbero non essere allineate (warning in Console).

**Campi Inspector di CTLoader:**
- `niftiPath` — percorso assoluto del file `.nii.gz`
- `predictionsPath` — percorso assoluto del file `*_predictions.json`
- `objFolder` — cartella contenente i file OBJ e meta JSON
- `ctScale` — scala uniforme del volume (default 1)
- `caseInfoPanel` — trascina qui il GameObject con `CaseInfoPanel`
- `loadButton` — trascina qui il Button "Load DATA" (viene disabilitato dopo il primo click)

#### `CameraController.cs`
Va attaccato alla **Main Camera**. Controlla:
- **Click destro + drag** → orbita attorno al volume
- **Scroll** → zoom
- **Click centrale + drag** → pan
- **R** → reset alla vista iniziale
- `AutoFrame()` viene chiamato automaticamente da CTLoader dopo il caricamento, centra la camera sul volume.

#### `CaseInfoPanel.cs`
Va attaccato a un GameObject figlio del Canvas. Richiede tre campi `TextMeshProUGUI` (PatientId, FractureCount, ClassBreakdown) da assegnare nell'Inspector. Mostra patient ID e conteggi per classe (Displaced / Non-displaced / Buckle).

#### `FractureToggle.cs`
Va attaccato **direttamente al GameObject del Dropdown** (TMP_Dropdown). Collega il dropdown a `CTLoader.ApplyColorMode()`. Le tre voci del dropdown devono essere (nell'ordine):
- `Nessuna lesione` → nasconde tutte le mesh
- `Lesione binaria` → mostra tutte le mesh in rosso
- `Lesione per tipo` → arancio (Displaced), blu (Non-displaced), viola (Buckle)

#### `SimpleOBJLoader.cs`
Loader OBJ statico, usato internamente da CTLoader. Supporta triangoli, quad (fan), normali e UV. Quando `CTLoader` passa una `vertexTransform`, la applica a ogni vertice invece del flip Z di default.

#### `Assets/Editor/HierarchyReorder.cs`
Utility Editor-only. Aggiunge il menu `Tools → Reorder` per spostare GameObject nella Hierarchy senza drag&drop (workaround per un bug di Unity 6).

### Scena
`Assets/Scenes/SampleScene.unity` — la scena principale. Contiene:
- **Main Camera** con `CameraController`
- **Canvas** con pannello CaseInfoPanel, Dropdown FractureToggle, Button LoadDATA
- **CTLoader** (GameObject vuoto) con lo script `CTLoader`

---

## Setup su una nuova macchina

### Prerequisiti
- **Unity 6** (6000.x) con modulo **Windows Build Support** (o Mac Build Support su Mac)
- Connessione internet al primo avvio (scarica il pacchetto `UnityVolumeRendering` da GitHub)
- I file dati: la CT in NIfTI (`.nii.gz`), il JSON predizioni, la cartella con OBJ e meta JSON

### Passi

1. **Clona/copia il progetto** — la cartella `Grafica3D_project_RibFrac` intera.

2. **Apri in Unity Hub** — seleziona Unity 6 come editor version. Il primo import dura qualche minuto.

3. **Risolvi warning Input System** — se Unity chiede di abilitare il New Input System, accetta e riavvia.

4. **Assegna i percorsi nel CTLoader** — in Hierarchy seleziona il GameObject `CTLoader`, nell'Inspector imposta:
   - `Nifti Path`
   - `Predictions Path`
   - `Obj Folder`
   
   **Windows:** usa `/` o `\\` come separatore, es. `C:/Dati/RibFrac108-image.nii.gz`
   
   **Mac:** usa percorsi Unix, es. `/Users/nome/Dati/RibFrac108-image.nii.gz`

5. **Premi Play** e poi **Load DATA** nel pannello in-game.

### Note specifiche per Mac

- I percorsi sono Unix-style (`/Users/...`), non usare `C:\`.
- Il package `UnityVolumeRendering` viene installato da GitHub e funziona su Mac senza modifiche.
- `SimpleOBJLoader` usa `Shader.Find("Universal Render Pipeline/Lit")` — questo shader esiste su Mac se URP è configurato correttamente (lo è, essendo il template di partenza URP).
- Se la Console mostra errori di compilazione su Mac relativi a `System.IO.Path`, verificare che i percorsi non contengano spazi non escapati.
- **Input System su Mac:** il click destro e il click centrale del mouse funzionano identicamente a Windows. Su trackpad, il right-click è il tap a due dita; per il click centrale serve un mouse fisico o una soluzione alternativa (da implementare se necessario).

### Cosa NON è salvato nella repo

- I file dati (NIfTI, JSON, OBJ) — devono essere procurati separatamente.
- La cartella `Library/` — viene rigenerata automaticamente da Unity al primo avvio.

---

## Struttura file rilevante

```
Assets/
├── Editor/
│   └── HierarchyReorder.cs       # Utility editor per riordinare la Hierarchy
├── Scenes/
│   └── SampleScene.unity         # Scena principale
└── Scripts/
    ├── CTLoader.cs               # Loader CT + fratture (entry point logica)
    ├── CameraController.cs       # Controlli camera orbitale
    ├── CaseInfoPanel.cs          # UI pannello info paziente
    ├── FractureToggle.cs         # Dropdown colorazione fratture
    └── SimpleOBJLoader.cs        # Parser OBJ runtime

Packages/
└── manifest.json                 # Dipendenze (UnityVolumeRendering, InputSystem, URP...)
```

---