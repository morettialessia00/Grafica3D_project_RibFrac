# Guida: Espansione del Visualizzatore a Più Pazienti

Questa guida descrive **tutte le modifiche sequenziali** necessarie per scalare il progetto da un singolo paziente hardcodato a un sistema con selezione paziente dinamica, utilizzabile su qualsiasi dispositivo che cloni il repository.

---

## Indice

1. [Riorganizzazione delle cartelle](#1-riorganizzazione-delle-cartelle)
2. [Aggiornamento .gitattributes](#2-aggiornamento-gitattributes)
3. [Nuovo script: PatientConfig.cs](#3-nuovo-script-patientconfigcs)
4. [Nuovo script: PatientRegistry.cs](#4-nuovo-script-patientregistrycs)
5. [Modifica: CTLoader.cs](#5-modifica-ctloadercs)
6. [Nuovo script: PatientSelectorPanel.cs](#6-nuovo-script-patientselectorpanelcs)
7. [Setup Unity Editor: UI del selettore](#7-setup-unity-editor-ui-del-selettore)
8. [Riconfigurare il bottone Load DATA](#8-riconfigurare-il-bottone-load-data)
9. [Procedura per aggiungere nuovi pazienti](#9-procedura-per-aggiungere-nuovi-pazienti)
10. [Condivisione con altri (Git LFS)](#10-condivisione-con-altri-git-lfs)

---

## 1. Riorganizzazione delle cartelle

### Perché è necessario

`CTLoader` usa `File.ReadAllText` e `File.Exists` (accesso diretto al filesystem). Nell'Editor Unity funziona da `Assets/`, ma **in una build compilata (.exe) Unity non copia i file di `Assets/` a meno che non stiano in `Assets/StreamingAssets/`**, l'unica cartella garantita accessibile su disco a runtime.

### Struttura target

Per ogni paziente la struttura è identica. Tutti i file stanno in una singola cartella col nome del paziente:

```
Assets/
└── StreamingAssets/
    └── Patients/
        ├── RibFrac108/
        │   ├── RibFrac108-image.nii.gz
        │   ├── RibFrac108_predictions.json
        │   ├── RibFrac108_ambiguous.json
        │   └── Meshes/
        │       ├── RibFrac108_fracture01_mesh.obj
        │       ├── RibFrac108_fracture01_meta.json
        │       ├── RibFrac108_fracture02_mesh.obj
        │       ├── RibFrac108_fracture02_meta.json
        │       └── ... (tutti gli altri OBJ + meta JSON)
        ├── RibFrac109/
        │   └── (stessa struttura)
        └── RibFracXXX/
            └── (stessa struttura)
```

### Passi da eseguire in Unity (Project window)

> **Importante:** spostare i file **dalla Project window di Unity** (non da Explorer), così Unity aggiorna i file `.meta` senza perdere i GUID.

1. Apri Unity. Nella **Project window** (in basso), naviga in `Assets/`.

2. Crea la cartella `StreamingAssets`:
   - Tasto destro su `Assets` → **Create → Folder** → digita `StreamingAssets` → Invio.

3. Dentro `StreamingAssets`, crea `Patients`:
   - Tasto destro su `StreamingAssets` → **Create → Folder** → `Patients` → Invio.

4. Dentro `Patients`, crea `RibFrac108`:
   - Tasto destro su `Patients` → **Create → Folder** → `RibFrac108` → Invio.

5. Dentro `RibFrac108`, crea `Meshes`:
   - Tasto destro su `RibFrac108` → **Create → Folder** → `Meshes` → Invio.

6. Sposta il file NIfTI:
   - Nella Project window naviga in `Assets/Volumes/RibFrac108/`.
   - Seleziona `RibFrac108-image.nii.gz`.
   - Trascinalo in `Assets/StreamingAssets/Patients/RibFrac108/`.

7. Sposta i JSON predizioni:
   - Naviga in `Assets/Data/Predictions/RibFrac108/`.
   - Seleziona `RibFrac108_predictions.json` e `RibFrac108_ambiguous.json`.
   - Trascinali in `Assets/StreamingAssets/Patients/RibFrac108/`.

8. Sposta le mesh OBJ e i meta JSON:
   - Naviga in `Assets/Models/Fractures/RibFrac108/`.
   - Seleziona **tutti** i file (Ctrl+A).
   - Trascinali in `Assets/StreamingAssets/Patients/RibFrac108/Meshes/`.

9. Elimina le cartelle originali ora vuote:
   - Tasto destro su `Assets/Volumes/RibFrac108` → **Delete**.
   - Tasto destro su `Assets/Data/Predictions/RibFrac108` → **Delete**.
   - Tasto destro su `Assets/Models/Fractures/RibFrac108` → **Delete**.
   - Se `Assets/Volumes`, `Assets/Data`, `Assets/Models` sono ora completamente vuote, eliminale anch'esse.

---

## 2. Aggiornamento .gitattributes

Il `.gitattributes` già esistente copre `.gz` e `.obj` con Git LFS. Va aggiunto solo il tracciamento esplicito per i file `.nii` (NIfTI non compressi, nel caso vengano aggiunti in futuro).

Apri `.gitattributes` nella root del progetto e aggiungi questa riga nella sezione "Compressed Archive":

```
*.nii                   lfs
```

Nessuna altra modifica è necessaria: `.gz`, `.obj`, `.json` sono già gestiti correttamente.

---

## 3. Nuovo script: PatientConfig.cs

Questo file definisce la struttura dati che descrive un paziente. Non contiene logica, è solo un contenitore di path.

**Dove salvarlo:** `Assets/Scripts/PatientConfig.cs`

**Procedura in Unity:**
- Nella Project window, tasto destro su `Assets/Scripts` → **Create → C# Script** → digita `PatientConfig` → Invio.
- Doppio click sul file per aprirlo, **cancella tutto** il contenuto e sostituisci con il codice qui sotto.

```csharp
/// <summary>
/// Struttura dati che descrive un paziente con tutti i percorsi ai file necessari.
/// Costruita da PatientRegistry a runtime scansionando StreamingAssets/Patients/.
/// </summary>
[System.Serializable]
public class PatientConfig
{
    public string id;               // es. "RibFrac108"
    public string niftiPath;        // path assoluto al .nii.gz
    public string predictionsPath;  // path assoluto al _predictions.json
    public string ambiguousPath;    // path assoluto al _ambiguous.json
    public string meshFolder;       // path assoluto alla cartella Meshes/
}
```

---

## 4. Nuovo script: PatientRegistry.cs

Scansiona `StreamingAssets/Patients/` a runtime e costruisce la lista dei pazienti disponibili seguendo la naming convention. Non richiede nessun file indice da aggiornare manualmente.

**Dove salvarlo:** `Assets/Scripts/PatientRegistry.cs`

**Procedura in Unity:**
- Tasto destro su `Assets/Scripts` → **Create → C# Script** → `PatientRegistry` → Invio.
- Apri, cancella tutto, incolla:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Scansiona Application.streamingAssetsPath/Patients/ e restituisce
/// la lista dei pazienti disponibili in base alla naming convention.
///
/// Naming convention attesa per paziente con ID "RibFracXXX":
///   StreamingAssets/Patients/RibFracXXX/RibFracXXX-image.nii.gz
///   StreamingAssets/Patients/RibFracXXX/RibFracXXX_predictions.json
///   StreamingAssets/Patients/RibFracXXX/RibFracXXX_ambiguous.json   (opzionale)
///   StreamingAssets/Patients/RibFracXXX/Meshes/                     (opzionale)
/// </summary>
public static class PatientRegistry
{
    private static List<PatientConfig> _cache = null;

    /// <summary>
    /// Restituisce la lista dei pazienti trovati. Il risultato viene
    /// cachato dopo la prima chiamata (nessuna re-scansione a ogni accesso).
    /// </summary>
    public static List<PatientConfig> GetAll()
    {
        if (_cache != null) return _cache;

        _cache = new List<PatientConfig>();

        string patientsRoot = Path.Combine(Application.streamingAssetsPath, "Patients");

        if (!Directory.Exists(patientsRoot))
        {
            Debug.LogError($"[PatientRegistry] Cartella non trovata: {patientsRoot}\n" +
                           "Assicurati che la struttura StreamingAssets/Patients/ esista.");
            return _cache;
        }

        foreach (string patientDir in Directory.GetDirectories(patientsRoot))
        {
            string id = Path.GetFileName(patientDir);

            string nifti       = Path.Combine(patientDir, $"{id}-image.nii.gz");
            string predictions = Path.Combine(patientDir, $"{id}_predictions.json");
            string ambiguous   = Path.Combine(patientDir, $"{id}_ambiguous.json");
            string meshFolder  = Path.Combine(patientDir, "Meshes");

            // Il file NIfTI è obbligatorio: senza di esso il paziente viene ignorato.
            if (!File.Exists(nifti))
            {
                Debug.LogWarning($"[PatientRegistry] NIfTI non trovato per '{id}', " +
                                 $"paziente ignorato. Path atteso: {nifti}");
                continue;
            }

            _cache.Add(new PatientConfig
            {
                id              = id,
                niftiPath       = nifti,
                predictionsPath = File.Exists(predictions) ? predictions : "",
                ambiguousPath   = File.Exists(ambiguous)   ? ambiguous   : "",
                meshFolder      = Directory.Exists(meshFolder) ? meshFolder : patientDir
            });

            Debug.Log($"[PatientRegistry] Trovato paziente: {id}");
        }

        Debug.Log($"[PatientRegistry] Totale pazienti trovati: {_cache.Count}");
        return _cache;
    }

    /// <summary>
    /// Forza la ri-scansione al prossimo accesso (utile se i file cambiano a runtime).
    /// </summary>
    public static void InvalidateCache() => _cache = null;
}
```

---

## 5. Modifica: CTLoader.cs

### Cosa cambia

- I 4 campi pubblici `niftiPath`, `predictionsPath`, `ambiguousPath`, `objFolder` vengono **rimossi** dall'Inspector.
- Vengono sostituiti da 4 campi **privati** impostati a runtime.
- `LoadData()` (chiamata dal bottone) viene **rimossa**: ora il bottone aprirà il selettore, non carica direttamente.
- Si aggiunge `LoadPatient(PatientConfig config)`: metodo pubblico chiamato dal selettore.
- Si aggiunge `ResetPatient()`: distrugge i dati del paziente precedente prima di caricarne uno nuovo.
- Si aggiunge un riferimento pubblico a `PatientSelectorPanel` (per riaprirlo dopo il caricamento).
- Alla fine del caricamento, il bottone viene **riabilitato** (anziché restare disabilitato).

### Modifiche puntuali

Apri `Assets/Scripts/CTLoader.cs` e applica le seguenti modifiche nell'ordine indicato.

---

#### 5.1 — Sostituire i campi path pubblici con campi privati

**Trova e RIMUOVI questi 4 campi** (sono nella sezione `[Header("Paths")]`):

```csharp
[Header("Paths")]
public string niftiPath       = "";
public string predictionsPath = "";
public string ambiguousPath   = "";
public string objFolder       = "";
```

**Aggiungi al loro posto**, nella stessa posizione, i campi privati e il riferimento al selettore:

```csharp
[Header("Riferimento Selettore")]
[Tooltip("Trascina qui il GameObject con PatientSelectorPanel")]
public PatientSelectorPanel patientSelectorPanel;

// Percorsi impostati a runtime da PatientSelectorPanel tramite LoadPatient()
private string _niftiPath       = "";
private string _predictionsPath = "";
private string _ambiguousPath   = "";
private string _objFolder       = "";
```

---

#### 5.2 — Aggiungere il metodo ResetPatient()

**Aggiungi questo metodo** subito prima di `LoadData()` (se esiste ancora) o prima di `LoadAll()`:

```csharp
// ── Reset: distrugge i dati del paziente precedente ───────────────────────
// Va chiamato prima di LoadPatient() se un paziente è già stato caricato.
public void ResetPatient()
{
    // Distruggi volume CT
    if (_volObj != null)
    {
        Destroy(_volObj.gameObject);
        _volObj  = null;
        _dataset = null;
    }

    // Distruggi mesh fratture
    if (_fracturesParent != null)
    {
        Destroy(_fracturesParent);
        _fracturesParent = null;
    }

    // Reset dizionari e buffer
    FractureData.Clear();
    _baseColors.Clear();
    _depthSortBuffer.Clear();

    // Reset stati
    HasPredictions   = false;
    CurrentPatientId = "";
    CurrentColorMode = 0; // il setter pubblico chiama ApplyColorMode ma _fracturesParent è null, ok

    // Reset UI
    caseInfoPanel?.Clear();
    FindAnyObjectByType<FractureToggle>()?.SetInteractable(false);
    FindAnyObjectByType<CameraControlPanel>()?.SetInteractable(false);
}
```

---

#### 5.3 — Aggiungere il metodo pubblico LoadPatient()

**Aggiungi subito dopo ResetPatient()**:

```csharp
// ── Punto di ingresso pubblico chiamato da PatientSelectorPanel ───────────
public void LoadPatient(PatientConfig config)
{
    ResetPatient();

    _niftiPath       = config.niftiPath;
    _predictionsPath = config.predictionsPath;
    _ambiguousPath   = config.ambiguousPath;
    _objFolder       = config.meshFolder;

    StartCoroutine(LoadAll());
}
```

---

#### 5.4 — Rimuovere o svuotare LoadData()

Se nel file esiste il metodo `public void LoadData()`, **rimuovilo completamente** (non serve più: il bottone ora aprirà il selettore).

---

#### 5.5 — Aggiornare LoadAll()

In `LoadAll()` ci sono due punti da modificare:

**A) Sostituisci i riferimenti ai vecchi campi pubblici con quelli privati.**

Trova:
```csharp
yield return StartCoroutine(LoadVolume(niftiPath));
```
Sostituisci con:
```csharp
yield return StartCoroutine(LoadVolume(_niftiPath));
```

Trova:
```csharp
yield return StartCoroutine(LoadFractures(predictionsPath, objFolder));
```
Sostituisci con:
```csharp
yield return StartCoroutine(LoadFractures(_predictionsPath, _objFolder));
```

**B) Alla fine di LoadAll(), riabilita il bottone selettore.**

Trova la riga che abilita FractureToggle e CameraControlPanel (è verso la fine di `LoadAll()`):
```csharp
FindAnyObjectByType<FractureToggle>()?.SetInteractable(true);
FindAnyObjectByType<CameraControlPanel>()?.SetInteractable(true);
```
**Subito dopo** aggiungi:
```csharp
// Riabilita il bottone "Load DATA" così l'utente può cambiare paziente
if (loadButton != null) loadButton.interactable = true;
```

**C) All'inizio di LoadAll(), il loadButton va disabilitato** durante il caricamento (per evitare click doppi). Già presente nel codice originale:
```csharp
if (loadButton != null) loadButton.interactable = false;
```
Lascia questa riga invariata.

---

#### 5.6 — Aggiornare il percorso ambiguousPath in LoadFractures

In `LoadFractures`, il metodo usa la variabile locale `ambiguousPath` (parametro della funzione). Dopo le modifiche, la firma del metodo deve restare invariata (`string jsonPath, string objDir`). L'unica modifica è che il metodo ora riceve `_ambiguousPath` già impostato come parametro.

Nel corpo di `LoadFractures`, trova il blocco che usa `ambiguousPath`:
```csharp
if (!string.IsNullOrEmpty(ambiguousPath) && File.Exists(ambiguousPath))
```
Questo si riferisce al **campo `ambiguousPath`** originale (ora rimosso). **Sostituisci** `ambiguousPath` in questa riga con `_ambiguousPath`:
```csharp
if (!string.IsNullOrEmpty(_ambiguousPath) && File.Exists(_ambiguousPath))
```
E nella riga interna dove viene letto il file:
```csharp
string ambJson = File.ReadAllText(ambiguousPath);
```
Sostituisci con:
```csharp
string ambJson = File.ReadAllText(_ambiguousPath);
```

---

## 6. Nuovo script: PatientSelectorPanel.cs

Gestisce la UI di selezione paziente. Si apre quando l'utente preme "Load DATA", mostra i pazienti disponibili, e al click su un paziente avvia il caricamento.

**Dove salvarlo:** `Assets/Scripts/PatientSelectorPanel.cs`

**Procedura in Unity:**
- Tasto destro su `Assets/Scripts` → **Create → C# Script** → `PatientSelectorPanel` → Invio.
- Apri, cancella tutto, incolla:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pannello di selezione paziente. Si apre quando l'utente preme "Load DATA"
/// e mostra un bottone per ogni paziente trovato in StreamingAssets/Patients/.
///
/// Setup in Unity (vedi Sezione 7 della guida):
///   1. Crea un Panel figlio del Canvas, rinominalo "PatientSelectorPanel".
///   2. Aggiungi questo script al Panel.
///   3. Nell'Inspector, assegna: ctLoader, buttonContainer, patientButtonPrefab.
///   4. Crea il prefab del bottone (vedi guida).
///   5. Collega il bottone "Load DATA" a PatientSelectorPanel.Open().
/// </summary>
public class PatientSelectorPanel : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Il CTLoader nella scena")]
    public CTLoader ctLoader;

    [Tooltip("Il Transform contenitore in cui vengono creati i bottoni (con VerticalLayoutGroup)")]
    public Transform buttonContainer;

    [Tooltip("Prefab del bottone paziente (un Button con un TextMeshProUGUI figlio)")]
    public GameObject patientButtonPrefab;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        // Il pannello parte nascosto
        gameObject.SetActive(false);
    }

    // ── API pubblica ──────────────────────────────────────────────────────────

    /// <summary>Apre il pannello e popola la lista pazienti.</summary>
    public void Open()
    {
        PopulateList();
        gameObject.SetActive(true);
    }

    /// <summary>Chiude il pannello senza caricare nulla.</summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    // ── Popolamento lista ─────────────────────────────────────────────────────
    void PopulateList()
    {
        // Rimuovi bottoni precedenti (per aggiornamenti a runtime)
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        List<PatientConfig> patients = PatientRegistry.GetAll();

        if (patients.Count == 0)
        {
            // Mostra un messaggio se non ci sono pazienti
            GameObject msgObj = new GameObject("NoPatients");
            msgObj.transform.SetParent(buttonContainer, false);
            var txt = msgObj.AddComponent<TextMeshProUGUI>();
            txt.text = "Nessun paziente trovato.\nVerifica la struttura di StreamingAssets/Patients/.";
            txt.fontSize = 14;
            txt.alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (PatientConfig config in patients)
        {
            // Cattura la variabile nel closure
            PatientConfig captured = config;

            GameObject btnObj = Instantiate(patientButtonPrefab, buttonContainer);

            // Imposta il testo del bottone con l'ID paziente
            TextMeshProUGUI label = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = captured.id;

            // Al click: chiudi il pannello e avvia il caricamento
            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(() =>
                {
                    Close();
                    ctLoader?.LoadPatient(captured);
                });
        }
    }
}
```

---

## 7. Setup Unity Editor: UI del selettore

In questa sezione costruiamo la finestra di selezione paziente nella scena.

### 7.1 — Creare il Panel di sfondo

1. Nella **Hierarchy** (pannello in alto a sinistra), espandi il nodo `Canvas`.
2. Tasto destro su `Canvas` → **UI → Panel**.
3. Rinominalo `PatientSelectorPanel`.
4. Nel suo **Inspector → Rect Transform**: imposta **Anchor** a "stretch-stretch" (allunga su tutto lo schermo):
   - Clicca il quadrato Anchor Presets in alto a sinistra nell'Inspector.
   - Tieni premuto **Alt+Shift** e clicca il preset **in basso a destra** (stretch su tutti i lati).
5. In **Image (Script)** (il componente sotto): imposta **Color** a `(0, 0, 0, 180)` circa (nero semi-trasparente).

### 7.2 — Aggiungere il titolo

1. Tasto destro su `PatientSelectorPanel` → **UI → Text - TextMeshPro**.
2. Rinominalo `TitleText`.
3. Nell'Inspector: **Text** = `Seleziona Paziente`, **Font Size** = `28`, **Alignment** = centro.
4. **Rect Transform**: posizionalo in alto (Anchor top-center, Pos Y ≈ -50).

### 7.3 — Creare il contenitore scroll per i bottoni

1. Tasto destro su `PatientSelectorPanel` → **UI → Scroll View**.
2. Rinominalo `PatientScrollView`.
3. **Rect Transform**: regola le dimensioni in modo che occupi la parte centrale del pannello (es. larghezza 400, altezza 300, centrato).
4. Espandi `PatientScrollView` → `Viewport` → `Content`.
5. Seleziona `Content`. Nell'Inspector → **Add Component** → cerca `Vertical Layout Group` → aggiungilo.
6. Nel `Vertical Layout Group`: spunta **Control Child Size (Width e Height)**, imposta **Spacing** = `10`, **Padding** = `10`.
7. **Add Component** su `Content` → `Content Size Fitter`. Imposta **Vertical Fit** = `Preferred Size`.
8. Rinomina `Content` in `ButtonContainer`.

### 7.4 — Nessun prefab necessario

I bottoni paziente vengono creati interamente via codice a runtime da `PatientSelectorPanel.cs`. Non è necessario creare alcun prefab.

Se hai già creato un `PatientButton` figlio di `ButtonContainer`, eliminalo dalla Hierarchy: tasto destro → **Delete**.

### 7.5 — Aggiungere il bottone Annulla

1. Tasto destro su `PatientSelectorPanel` → **UI → Button - TextMeshPro**.
2. Rinominalo `CloseButton`. Testo del bottone: `Annulla`.
3. **Rect Transform**: posizionalo in basso al pannello.
4. Nel componente **Button → On Click ()**: clicca `+`, trascina `PatientSelectorPanel` nel campo oggetto, scegli `PatientSelectorPanel → Close()`.

> Questo bottone va creato **prima** di aggiungere lo script al pannello (passo 7.6), altrimenti la Hierarchy potrebbe confondersi con il pannello disattivato.

### 7.6 — Aggiungere lo script PatientSelectorPanel al Panel

1. Seleziona `PatientSelectorPanel` nella Hierarchy.
2. Inspector → **Add Component** → cerca `PatientSelectorPanel` → aggiungilo.
3. Assegna i campi nell'Inspector:
   - **Ct Loader**: trascina il GameObject `CTLoader` dalla Hierarchy.
   - **Button Container**: trascina `ButtonContainer` (figlio di `PatientScrollView/Viewport/ButtonContainer`).

> Il campo `Patient Button Prefab` non esiste più: i bottoni vengono creati via codice.

### 7.7 — Aggiornare CTLoader nell'Inspector

1. Seleziona il GameObject `CTLoader` nella Hierarchy.
2. Nel suo Inspector, i campi `Nifti Path`, `Predictions Path`, `Ambiguous Path`, `Obj Folder` **non esistono più** (li hai rimossi nel passo 5.1). Nessuna azione richiesta su di essi.
3. Assegna il nuovo campo **Patient Selector Panel**: trascina `PatientSelectorPanel` dalla Hierarchy.
4. Il campo **Load Button** rimane: assicurati che sia ancora assegnato (il bottone fisico nella UI).

---

## 8. Riconfigurare il bottone Load DATA

Il bottone "Load DATA" ora deve **aprire il selettore** invece di chiamare `CTLoader.LoadData()` direttamente.

1. Nella Hierarchy, trova il GameObject del bottone "Load DATA" (probabilmente figlio del Canvas o di un Panel).
2. Selezionalo → Inspector → componente **Button → On Click ()**.
3. Trova la voce che chiama `CTLoader.LoadData()`. Clicca `-` per rimuoverla.
4. Clicca `+` per aggiungere una nuova voce:
   - Trascina `PatientSelectorPanel` nel campo oggetto.
   - Dal menu a tendina scegli `PatientSelectorPanel → Open()`.
5. Salva la scena: **File → Save** (Ctrl+S).

---

## 9. Procedura per aggiungere nuovi pazienti

Per aggiungere un nuovo paziente al visualizzatore, esegui questi passi nell'ordine. **Nessuna modifica al codice o alla scena Unity è necessaria.**

### File necessari (6 obbligatori + N mesh)

| File | Path nella cartella paziente | Obbligatorio |
|------|------------------------------|:---:|
| Volume CT | `RibFracXXX-image.nii.gz` | ✅ |
| Predizioni | `RibFracXXX_predictions.json` | consigliato |
| Ambigue | `RibFracXXX_ambiguous.json` | opzionale |
| Mesh fratture | `Meshes/RibFracXXX_fractureNN_mesh.obj` | opzionale |
| Meta JSON fratture | `Meshes/RibFracXXX_fractureNN_meta.json` | opzionale |
| Mesh ambigue | `Meshes/RibFracXXX_ambiguousNN_mesh.obj` | opzionale |
| Meta JSON ambigue | `Meshes/RibFracXXX_ambiguousNN_meta.json` | opzionale |

### Passi

1. **Crea la cartella paziente** in `Assets/StreamingAssets/Patients/`:
   - Nella Project window → tasto destro su `Patients` → **Create → Folder** → `RibFracXXX`.
   - Dentro `RibFracXXX` → **Create → Folder** → `Meshes`.

2. **Copia i file** nella cartella creata:
   - Puoi trascinare i file dalla Project window **oppure** copiarli da Explorer in `Assets/StreamingAssets/Patients/RibFracXXX/` e poi premere **Ctrl+R** in Unity per aggiornare.

3. **Verifica la naming convention**:
   - Il NIfTI deve chiamarsi esattamente `{ID}-image.nii.gz` (es. `RibFrac109-image.nii.gz`).
   - I JSON devono chiamarsi `{ID}_predictions.json` e `{ID}_ambiguous.json`.
   - Le mesh: `{ID}_fractureNN_mesh.obj` e `{ID}_fractureNN_meta.json`, dove `NN` è il rib_id a 2 cifre.

4. **Testa in Play mode**: premi Play in Unity, clicca "Load DATA" → il nuovo paziente deve comparire nella lista.

---

## 10. Condivisione con altri (Git LFS)

### Prerequisiti per chi clona il repository

Chi riceve il progetto deve avere **Git LFS installato** prima di clonare, altrimenti i file binari (NIfTI, OBJ) arrivano come puntatori vuoti.

**Installazione Git LFS** (da eseguire una volta sola sul proprio computer):

```bash
# Windows: scarica l'installer da https://git-lfs.com/ oppure:
winget install Git.LFS

# Poi inizializza nel proprio Git:
git lfs install
```

**Clone del repository:**

```bash
git clone <URL_del_repo>
```

Git LFS scarica automaticamente i file binari durante il clone se è installato.

### Aggiungere un nuovo paziente al repository

Dopo aver copiato i file nella struttura `StreamingAssets/Patients/NuovoPaziente/`:

```bash
git add Assets/StreamingAssets/Patients/NuovoPaziente/
git commit -m "Aggiunto paziente NuovoPaziente"
git push
```

I file `.nii.gz` e `.obj` vengono automaticamente gestiti da LFS (già configurato nel `.gitattributes`).

### Verifica che LFS stia tracciando correttamente

Per controllare quali file sono in LFS:

```bash
git lfs ls-files
```

Dovresti vedere tutti i file `.nii.gz` e `.obj` nella lista.

---

## Riepilogo modifiche

| Cosa | Tipo | Note |
|------|------|-------|
| `Assets/StreamingAssets/Patients/` | Nuova struttura cartelle | Sposta tutti i file dei pazienti qui |
| `.gitattributes` | Modifica (1 riga) | Aggiunge `*.nii` a LFS |
| `PatientConfig.cs` | Nuovo script | Struttura dati paziente |
| `PatientRegistry.cs` | Nuovo script | Auto-discovery pazienti |
| `CTLoader.cs` | Modifica | Rimuove path hardcodati, aggiunge Reset/LoadPatient |
| `PatientSelectorPanel.cs` | Nuovo script | UI selezione paziente |
| Scena Unity | Modifica | Nuovo Panel + bottone Load DATA reindirizzato |
