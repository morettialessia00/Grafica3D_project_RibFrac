using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityVolumeRendering;

public class CTLoader : MonoBehaviour
{
    [Header("Riferimento Selettore")]
    [Tooltip("Trascina qui il GameObject con PatientSelectorPanel")]
    public PatientSelectorPanel patientSelectorPanel;

    // Percorsi impostati a runtime da PatientSelectorPanel tramite LoadPatient()
    private string _niftiPath     = "";
    private string _patientDir    = "";
    private string _ambiguousPath = "";
    private string _objFolder     = "";

    [Header("Visualizzazione")]
    [Tooltip("Fattore di scala uniforme del volume CT. Aumenta se la CT appare troppo piccola.")]
    public float ctScale = 1f;

    [Header("UI")]
    public CaseInfoPanel caseInfoPanel; // Trascina qui il GameObject con CaseInfoPanel
    [Tooltip("Il pulsante Load DATA — viene disabilitato dopo il primo caricamento")]
    public UnityEngine.UI.Button loadButton;

    [Header("Sezione Assiale CT")]
    [Tooltip("Trascina qui il GameObject con AxialSliceController")]
    public AxialSliceController axialSliceController;

    [Header("Crop Cassa Toracica")]
    [Tooltip("Trascina qui il GameObject con VolumeCropBox (opzionale)")]
    public VolumeCropBox volumeCropBox;

    private VolumeDataset        _dataset;
    private VolumeRenderedObject _volObj;
    private GameObject           _fracturesParent;

    // Buffer riusabile per il sort depth-based (nessuna allocazione per frame)
    private readonly List<(float sqDist, Renderer rend)> _depthSortBuffer
        = new List<(float, Renderer)>();

    // Colori base delle mesh fratture (RGB + alpha canonico senza modulazione depth)
    private readonly Dictionary<Renderer, Color> _baseColors
        = new Dictionary<Renderer, Color>();

    // GameObject frattura attualmente sotto il cursore (impostato da FractureSelector)
    private GameObject _hoveredObject;

    /// <summary>
    /// Chiamato da FractureSelector ogni frame per aggiornare quale frattura
    /// è sotto il cursore. Passa null per rimuovere l'highlight.
    /// </summary>
    public void SetHoveredObject(GameObject go) { _hoveredObject = go; }

    // ── Dati predizioni per classificatore ───────────────────────────────────
    // Un dizionario rib_id → FractureEntry per ciascuno dei 4 classificatori.
    private readonly Dictionary<int, FractureEntry>[] _classifierData =
        new Dictionary<int, FractureEntry>[FractureClasses.Classifiers.Length];

    // Dizionario del classificatore attualmente selezionato (vuoto se non in
    // modalita classificatore). Usato da FractureSelector per il pannello dettaglio.
    private static readonly Dictionary<int, FractureEntry> _emptyData
        = new Dictionary<int, FractureEntry>();
    public Dictionary<int, FractureEntry> FractureData
    {
        get
        {
            int k = FractureClasses.ClassifierIndex(CurrentColorMode);
            return (k >= 0 && _classifierData[k] != null) ? _classifierData[k] : _emptyData;
        }
    }

    // Numero totale di mesh-frattura caricate (classificate + ambigue) per il paziente.
    private int _totalFractureMeshes = 0;
    // Numero di mesh ambigue caricate.
    private int _nAmbiguousMeshes = 0;

    public string  CurrentPatientId  { get; private set; } = "";
    public bool    HasPredictions    { get; private set; } = false;

    // ── API pubblica per il pulsante UI ───────────────────────────────────────
    public bool FracturesVisible => _fracturesParent != null && _fracturesParent.activeSelf;

    public void ToggleFractures()
    {
        if (_fracturesParent == null) return;
        _fracturesParent.SetActive(!_fracturesParent.activeSelf);
    }

    public void SetFracturesVisible(bool visible)
    {
        if (_fracturesParent == null) return;
        _fracturesParent.SetActive(visible);
    }

    // ── Logica di colorazione a 6 stati ───────────────────────────────────────
    // 0 = No lesions      → mesh fratture nascoste
    // 1 = All lesions     → tutte le mesh rosse (vista binaria)
    // 2 = Classificatore 1 → colori per classe dal JSON 4classes
    // 3 = Classificatore 2 → colori per classe dal JSON 3classes D_ND_B
    // 4 = Classificatore 3 → colori per classe dal JSON 3classes SD_ND_B
    // 5 = Classificatore 4 → colori per classe dal JSON 2classes SD_NDB
    public int CurrentColorMode { get; private set; } = 0;

    /// <summary>Indice 0..3 del classificatore attivo, o -1 se non in modalita classificatore.</summary>
    public int CurrentClassifierIndex => FractureClasses.ClassifierIndex(CurrentColorMode);

    public void ApplyColorMode(int mode)
    {
        CurrentColorMode = mode;

        if (_fracturesParent == null) return;

        // No lesions: nascondi tutto e mostra il riepilogo nel Case INFO.
        if (mode == FractureClasses.ModeNone)
        {
            _fracturesParent.SetActive(false);
            caseInfoPanel?.ShowSummary(CurrentPatientId, _totalFractureMeshes, _nAmbiguousMeshes);
            return;
        }

        _fracturesParent.SetActive(true);

        // All lesions: tutte rosse.
        if (mode == FractureClasses.ModeAll)
        {
            foreach (Transform child in _fracturesParent.transform)
                foreach (var rend in child.GetComponentsInChildren<Renderer>())
                    SetFractureColor(rend, FractureClasses.BinaryColor);

            caseInfoPanel?.ShowSummary(CurrentPatientId, _totalFractureMeshes, _nAmbiguousMeshes);
            return;
        }

        // Modalita classificatore: colora per classe predetta e conta per classe.
        int k = FractureClasses.ClassifierIndex(mode);
        Dictionary<int, FractureEntry> data =
            (k >= 0 && _classifierData[k] != null) ? _classifierData[k] : _emptyData;
        FractureClasses.ClassifierDef def = FractureClasses.Classifiers[k];

        var counts = new Dictionary<string, int>();
        foreach (string cls in def.classes) counts[cls] = 0;
        int nUnclassified = 0;

        foreach (Transform child in _fracturesParent.transform)
        {
            var info = child.GetComponent<FractureMeshInfo>();
            int ribId = info != null ? info.ribId : -1;
            bool ambiguous = info != null && info.isAmbiguous;

            string cls = null;
            if (!ambiguous && data.TryGetValue(ribId, out FractureEntry e))
                cls = e.predicted_class;

            Color col;
            if (cls != null && counts.ContainsKey(cls))
            {
                col = FractureClasses.MeshColor(cls);
                counts[cls]++;
            }
            else
            {
                col = FractureClasses.UnclassifiedMeshColor;
                nUnclassified++;
            }

            foreach (var rend in child.GetComponentsInChildren<Renderer>())
                SetFractureColor(rend, col);
        }

        caseInfoPanel?.ShowClassifier(CurrentPatientId, def, _totalFractureMeshes,
                                      counts, nUnclassified);
    }

    // Helper: imposta colore e registra il colore base per la modulazione depth
    void SetFractureColor(Renderer rend, Color color)
    {
        rend.material.color = color;
        _baseColors[rend] = color;
    }

    // ── Depth sort per-frame ──────────────────────────────────────────────────
    // Tutte le mesh hanno pivot in (0,0,0): Unity non può sortarle per distanza.
    // Usiamo rendererPriority come tiebreaker: più lontano = priorità bassa = renderizza prima.
    // Questo garantisce il corretto alpha-compositing back-to-front (painter's algorithm).
    void LateUpdate()
    {
        if (_fracturesParent == null || !_fracturesParent.activeSelf) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        _depthSortBuffer.Clear();
        Vector3 camPos = cam.transform.position;

        foreach (Transform child in _fracturesParent.transform)
        {
            Renderer r = child.GetComponentInChildren<Renderer>();
            if (r == null) continue;
            _depthSortBuffer.Add(((r.bounds.center - camPos).sqrMagnitude, r));
        }

        // Discendente: il più lontano va a indice 0 → rendererPriority 0 → renderizza prima
        _depthSortBuffer.Sort((a, b) => b.sqDist.CompareTo(a.sqDist));

        int n = _depthSortBuffer.Count;
        for (int i = 0; i < n; i++)
        {
            _depthSortBuffer[i].rend.rendererPriority = i;

            // Depth cue: le fratture più lontane diventano più trasparenti.
            // t=0 → farthest (alpha×0.45), t=1 → closest (alpha×1.0)
            float t = n > 1 ? (float)i / (n - 1) : 1f;
            float alphaScale = Mathf.Lerp(0.45f, 1.0f, t);

            Renderer rend = _depthSortBuffer[i].rend;
            if (_baseColors.TryGetValue(rend, out Color baseCol))
            {
                Color c = baseCol;
                c.a = baseCol.a * alphaScale;

                // Hover boost: schiarisce e aumenta l'opacità della frattura sotto il cursore.
                // IsChildOf restituisce true anche se rend.transform == _hoveredObject.transform.
                if (_hoveredObject != null && rend.transform.IsChildOf(_hoveredObject.transform))
                {
                    c   = Color.Lerp(c, Color.white, 0.30f);   // schiarisce verso bianco
                    c.a = Mathf.Min(0.88f, baseCol.a * 2.5f);  // più opaco, ma mai pieno
                }

                rend.material.color = c;
            }
        }
    }

    // ── Reset: distrugge i dati del paziente precedente ───────────────────────
    // Va chiamato prima di LoadPatient() se un paziente è già stato caricato.
    public void ResetPatient()
    {
        // Notifica il controller della sezione assiale
        axialSliceController?.OnPatientReset();

        // Distruggi volume CT (il CropBox è figlio del volume, viene distrutto con esso)
        volumeCropBox?.Clear();
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
        for (int i = 0; i < _classifierData.Length; i++) _classifierData[i] = null;
        _baseColors.Clear();
        _depthSortBuffer.Clear();
        _hoveredObject = null;
        _totalFractureMeshes = 0;
        _nAmbiguousMeshes    = 0;

        // Reset stati
        HasPredictions   = false;
        CurrentPatientId = "";
        CurrentColorMode = 0;

        // Reset UI
        caseInfoPanel?.Clear();
        FindAnyObjectByType<FractureToggle>()?.SetInteractable(false);
        FindAnyObjectByType<CameraControlPanel>()?.SetInteractable(false);
    }

    // ── Punto di ingresso pubblico chiamato da PatientSelectorPanel ───────────
    public void LoadPatient(PatientConfig config)
    {
        ResetPatient();

        _niftiPath     = config.niftiPath;
        _patientDir    = config.patientDir;
        _ambiguousPath = config.ambiguousPath;
        _objFolder     = config.meshFolder;
        CurrentPatientId = config.id;

        StartCoroutine(LoadAll());
    }

    IEnumerator LoadAll()
    {
        // Disabilita il pulsante subito: evita caricamenti multipli
        if (loadButton != null) loadButton.interactable = false;

        yield return StartCoroutine(LoadVolume(_niftiPath));

        // Passa il volume al controller slice: DataTex e TFTex sono già pronti
        if (_volObj != null)
            axialSliceController?.OnPatientLoaded(_volObj);

        // Posiziona la camera PRIMA di mostrare il volume → nessun glitch visivo
        CameraController cam = FindAnyObjectByType<CameraController>();
        if (cam != null)
            cam.AutoFrame();
        else
            Debug.LogWarning("[CTLoader] CameraController non trovato in scena.");

        // Mostra il volume solo dopo che la camera è già nella posizione corretta
        if (_volObj != null)
            _volObj.gameObject.SetActive(true);

        yield return StartCoroutine(LoadFractures(_objFolder));

        // Abilita dropdown e controlli camera ora che i dati sono pronti
        FindAnyObjectByType<FractureToggle>()?.SetInteractable(true);
        FindAnyObjectByType<CameraControlPanel>()?.SetInteractable(true);

        // Riabilita il bottone "Load DATA" così l'utente può cambiare paziente
        if (loadButton != null) loadButton.interactable = true;
    }

    // ─── CT VOLUME ────────────────────────────────────────────────────────────
    IEnumerator LoadVolume(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[CTLoader] NIfTI non trovato: {path}");
            yield break;
        }

        IImageFileImporter importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NIFTI);
        _dataset = importer.Import(path);

        if (_dataset == null)
        {
            Debug.LogError("[CTLoader] Import NIfTI fallito: dataset null.");
            yield break;
        }

        _volObj = VolumeObjectFactory.CreateObject(_dataset);
        _volObj.gameObject.SetActive(false); // nascosto finché la camera non è posizionata
        _volObj.transform.position = Vector3.zero;
        // UnityVolumeRendering mappa k → -Y (testa in basso).
        // Il flip (1,-1,1) inverte l'asse verticale senza alterare X/Z.
        // La stessa correzione è applicata alla matrice T_vox delle mesh (vedi sotto).
        _volObj.transform.localScale = new Vector3(ctScale, -ctScale, ctScale);

        // ── Transfer Function: bone window clinico ────────────────────────────
        // Rileva se i valori sono in HU (CT standard) o in altra scala normalizzata.
        // In HU: aria ≈ -1024, grasso ≈ -100, muscolo ≈ +50, osso spongioso ≈ +300, corticale ≈ +700..1800
        float minVal = _dataset.GetMinDataValue();
        float maxVal = _dataset.GetMaxDataValue();
        float range  = maxVal - minVal;
        bool  isHU   = minVal < -200f && maxVal > 400f;

        Debug.Log($"[CTLoader] Data range: [{minVal:F1}, {maxVal:F1}], isHU={isHU}");

        // N(hu, fallback): converte un valore HU in posizione [0,1] nella TF.
        // Se i dati non sono in HU, usa il fallback (stima percentile del range).
        System.Func<float, float, float> N = (hu, fb) =>
            isHU ? Mathf.Clamp01((hu - minVal) / range) : fb;

        float nAir    = N(-900f, 0.03f); // aria / sfondo
        float nLung   = N(-500f, 0.22f); // polmone
        float nFat    = N(-100f, 0.42f); // grasso / tessuto molle
        float nMuscle = N( 100f, 0.52f); // muscolo
        float nBoneCut = N( 150f, 0.57f); // taglio secco appena sotto l'osso
        float nBone   = N( 280f, 0.60f); // osso spongioso / coste sottili (inizio osso)
        float nCortex = N( 600f, 0.72f); // osso corticale denso

        Debug.Log($"[CTLoader] TF positions — lung:{nLung:F3} fat:{nFat:F3} bone:{nBone:F3} cortex:{nCortex:F3}");

        UnityVolumeRendering.TransferFunction tf =
            ScriptableObject.CreateInstance<UnityVolumeRendering.TransferFunction>();

        // ── Colori: scala di grigi pura ───────────────────────────────────────
        // Aria → nero; osso spongioso → grigio chiaro; osso corticale → bianco.
        // Il tessuto molle (fat/muscle) è comunque trasparente per l'alpha → non visibile.
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(0f,       new Color(0.00f, 0.00f, 0.00f))); // nero (aria)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nLung,    new Color(0.05f, 0.05f, 0.05f))); // quasi nero (polmone)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nFat,     new Color(0.30f, 0.30f, 0.30f))); // grigio scuro (grasso)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nMuscle,  new Color(0.50f, 0.50f, 0.50f))); // grigio medio (muscolo)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nBone,    new Color(0.75f, 0.75f, 0.75f))); // grigio chiaro (osso spongioso)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nCortex,  new Color(0.95f, 0.95f, 0.95f))); // quasi bianco (osso corticale)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(1f,       new Color(1.00f, 1.00f, 1.00f))); // bianco (massima densità)

        // ── Alpha: quasi trasparente per aria/molle, sempre più opaco verso l'osso ──
        // Fat/muscolo leggermente più alti rispetto alla versione grigia per rendere
        // visibile il contorno del torace come in Slicer CT-Bones.
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(0f,       0.000f)); // fuori corpo: zero
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nAir,     0.000f)); // aria: zero
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nLung,    0.000f)); // polmone: trasparente
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nFat,     0.000f)); // grasso: trasparente
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nMuscle,   0.000f)); // muscolo: trasparente
        // Taglio secco a ~150 HU: sotto l'osso alpha resta 0 (nessun tessuto molle).
        // Poi ramp che dà opacità già allo spongioso/coste sottili (0.28) e satura
        // sulla cortex (0.90). Così le estremità sottili delle coste diventano visibili
        // senza far rientrare i tessuti molli.
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nBoneCut, 0.000f)); // taglio secco sotto l'osso
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nBone,    0.28f));  // osso spongioso / coste sottili: visibile
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nCortex,  0.90f));  // osso corticale: opaco
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(1f,       1.00f));  // densità massima: opaco

        tf.GenerateTexture();
        _volObj.SetTransferFunction(tf);

        // Passa la window grayscale all'AxialSliceController:
        // lo = nFat (inizio range visibile), hi = nCortex (osso corticale denso).
        axialSliceController?.SetGrayscaleWindow(nFat, nCortex);

        // Campionamento: 3× è un buon compromesso qualità/performance per le coste
        _volObj.SetSamplingRateMultiplier(5.0f);

        // ── Illuminazione volumetrica ─────────────────────────────────────────
        // Calcola il gradiente del volume (SmoothedCentralDifference = meno rumore)
        // e applica shading direzionale: le superfici verso la camera sono chiare,
        // quelle rivolte altrove più scure → profondità immediatamente leggibile.
        _volObj.SetGradientType(GradientType.SmoothedCentralDifference);
        _volObj.SetLightingEnabled(true);
        _volObj.SetLightSource(LightSource.ActiveCamera); // luce solidale alla camera

        // Ray termination: il raggio si ferma quando l'opacità è satura →
        // le coste posteriori non "trapassano" quelle anteriori già opache.
        _volObj.SetRayTerminationEnabled(true);

        // Applica il crop box per isolare la cassa toracica (se presente)
        volumeCropBox?.Apply(_volObj);

        Debug.Log("[CTLoader] CT caricata.");
        yield return null;
    }

    // ─── FRATTURE OBJ ─────────────────────────────────────────────────────────
    IEnumerator LoadFractures(string objDir)
    {
        if (_dataset == null)
        {
            Debug.LogError("[CTLoader] Dataset non disponibile, skip fratture.");
            yield break;
        }

        // ── Carica le predizioni di TUTTI i classificatori ────────────────────
        // Ogni classificatore ha un file JSON diverso ({id}{suffix}.json). Le mesh
        // sono le stesse per tutti: cambia solo l'etichetta di classe per costa.
        _baseColors.Clear();
        bool anyPredictions = false;
        for (int k = 0; k < FractureClasses.Classifiers.Length; k++)
        {
            string cPath = Path.Combine(_patientDir,
                CurrentPatientId + FractureClasses.Classifiers[k].fileSuffix + ".json");
            var dict = new Dictionary<int, FractureEntry>();

            if (File.Exists(cPath))
            {
                PredictionData pd = JsonUtility.FromJson<PredictionData>(File.ReadAllText(cPath));
                if (pd?.fractures != null)
                {
                    foreach (var f in pd.fractures) dict[f.rib_id] = f;
                    anyPredictions = true;
                }
                else Debug.LogError($"[CTLoader] JSON malformato: {cPath}");
            }
            else Debug.LogWarning($"[CTLoader] JSON classificatore non trovato: {cPath}");

            _classifierData[k] = dict;
        }

        // Nessun file di predizione → paziente non nel test set.
        if (!anyPredictions)
        {
            HasPredictions = false;
            Debug.LogWarning($"[CTLoader] Nessuna predizione per {CurrentPatientId}. " +
                             "Paziente non nel test set.");
            caseInfoPanel?.ShowNotInTestSet(CurrentPatientId);
            yield break;
        }

        HasPredictions = true;

        // ── Matrice voxel → Unity world ───────────────────────────────────────
        // UnityVolumeRendering posiziona il volume come:
        //   outerObject a (0,0,0) con rotazione identity
        //   meshContainer con localScale = (dimX*pX, dimY*pY, dimZ*pZ) e localRotation = Euler(90,0,0)
        // Voxel NIfTI [i, j, k] → Unity world:
        //   X = i*pixX - cx
        //   Y = cz  - k*pixZ      ← dalla rotazione 90°X: k (asse Z locale) → asse Y world negato
        //   Z = j*pixY - cy
        float pixX = _dataset.scale.x / _dataset.dimX;
        float pixY = _dataset.scale.y / _dataset.dimY;
        float pixZ = _dataset.scale.z / _dataset.dimZ;
        float cx   = _dataset.scale.x * 0.5f;
        float cy   = _dataset.scale.y * 0.5f;
        float cz   = _dataset.scale.z * 0.5f;

        // Matrix4x4 colonna-maggiore in Unity:
        // col0 = asse i → Unity, col1 = asse j → Unity, col2 = asse k → Unity, col3 = traslazione
        // k → +Y (non negato): allineato al flip (1,-1,1) applicato al volume CT sopra.
        Matrix4x4 T_vox = new Matrix4x4(
            new Vector4(pixX,  0f,    0f,    0f),  // i  → Unity X
            new Vector4(0f,    0f,    pixY,  0f),  // j  → Unity Z
            new Vector4(0f,    pixZ,  0f,    0f),  // k  → Unity Y
            new Vector4(-cx,  -cz,   -cy,    1f)   // traslazione
        );

        _fracturesParent = new GameObject($"{CurrentPatientId}_fractures");
        _fracturesParent.SetActive(false); // nascoste finché l'utente non seleziona una modalità

        // ── CARICA TUTTE LE MESH-FRATTURA (una sola volta) ────────────────────
        // Le mesh sono indipendenti dal classificatore: qui si caricano tutte quelle
        // presenti nella cartella; il colore/classe viene assegnato in ApplyColorMode
        // in base al classificatore selezionato. Ogni GameObject porta un
        // FractureMeshInfo con rib_id e flag ambigua.
        var loadedRibIds = new HashSet<int>();
        if (Directory.Exists(objDir))
        {
            foreach (string objPath in Directory.GetFiles(objDir, $"{CurrentPatientId}_fracture*_mesh.obj"))
            {
                int ribId = ExtractIndexFromName(Path.GetFileName(objPath), "_fracture");
                if (ribId < 0 || loadedRibIds.Contains(ribId)) continue;

                // ── Trasformazione OBJ mm → Unity world ────────────────────────
                // Le mesh OBJ sono in mm world space (affine NIfTI dal pipeline Python).
                // Serve: affine⁻¹ per tornare in spazio voxel, poi T_vox per Unity.
                string metaPath = objPath.Replace("_mesh.obj", "_meta.json");
                Matrix4x4 T_full = Matrix4x4.identity;
                if (File.Exists(metaPath))
                    T_full = T_vox * ParseAffineFromMetaJson(metaPath).inverse;
                else
                    Debug.LogWarning($"[CTLoader] Meta JSON non trovato: {metaPath}. " +
                                     "Le mesh potrebbero non essere allineate.");

                GameObject go = SimpleOBJLoader.Load(objPath, vertexTransform: T_full);
                if (go == null) continue;

                go.name = $"rib_{ribId:D2}";
                go.transform.SetParent(_fracturesParent.transform);
                go.AddComponent<MeshCollider>(); // necessario per il raycast al click
                var info = go.AddComponent<FractureMeshInfo>();
                info.ribId       = ribId;
                info.isAmbiguous = false;
                loadedRibIds.Add(ribId);

                foreach (var rend in go.GetComponentsInChildren<Renderer>())
                    SetFractureColor(rend, FractureClasses.UnclassifiedMeshColor);

                yield return null;
            }
        }

        // ── FRATTURE AMBIGUE ──────────────────────────────────────────────────
        // Caricate da ambiguousPath ({id}_ambiguous.json) → mesh _ambiguous{N}_mesh.obj.
        // Non appartengono a nessun classificatore: restano sempre "Unclassified".
        if (!string.IsNullOrEmpty(_ambiguousPath) && File.Exists(_ambiguousPath))
        {
            AmbiguousData ambData = JsonUtility.FromJson<AmbiguousData>(File.ReadAllText(_ambiguousPath));

            if (ambData?.fractures != null)
            {
                foreach (var amb in ambData.fractures)
                {
                    string meshFile = Path.Combine(objDir,
                        $"{CurrentPatientId}_ambiguous{amb.rib_id:D2}_mesh.obj");
                    string mPath = Path.Combine(objDir,
                        $"{CurrentPatientId}_ambiguous{amb.rib_id:D2}_meta.json");

                    if (!File.Exists(meshFile))
                    {
                        Debug.LogWarning($"[CTLoader] OBJ ambiguous non trovato: {meshFile}");
                        continue;
                    }

                    Matrix4x4 T_full = Matrix4x4.identity;
                    if (File.Exists(mPath))
                        T_full = T_vox * ParseAffineFromMetaJson(mPath).inverse;
                    else
                        Debug.LogWarning($"[CTLoader] Meta JSON ambiguous non trovato: {mPath}.");

                    GameObject go = SimpleOBJLoader.Load(meshFile, vertexTransform: T_full);
                    if (go == null) continue;

                    go.name = $"amb_{amb.rib_id:D2}";
                    go.transform.SetParent(_fracturesParent.transform);
                    go.AddComponent<MeshCollider>();
                    var info = go.AddComponent<FractureMeshInfo>();
                    info.ribId       = amb.rib_id;
                    info.isAmbiguous = true;
                    _nAmbiguousMeshes++;

                    foreach (var rend in go.GetComponentsInChildren<Renderer>())
                        SetFractureColor(rend, FractureClasses.UnclassifiedMeshColor);

                    Debug.Log($"[CTLoader] Caricata ambiguous: {Path.GetFileName(meshFile)}");
                    yield return null;
                }
            }
        }

        // ── Conteggi totali e stato iniziale del pannello Case INFO ────────────
        _totalFractureMeshes = _fracturesParent.transform.childCount;
        caseInfoPanel?.ShowSummary(CurrentPatientId, _totalFractureMeshes, _nAmbiguousMeshes);

        Debug.Log($"[CTLoader] Caricamento completato. " +
                  $"Mesh totali: {_totalFractureMeshes} (ambigue: {_nAmbiguousMeshes}).");
    }

    // ─── Helper: estrae l'indice numerico dopo un marker nel nome file ───────
    // Es. ("RibFrac108_fracture07_mesh.obj", "_fracture") → 7
    static int ExtractIndexFromName(string fileName, string marker)
    {
        int i = fileName.IndexOf(marker);
        if (i < 0) return -1;
        i += marker.Length;
        var sb = new StringBuilder();
        while (i < fileName.Length && char.IsDigit(fileName[i])) { sb.Append(fileName[i]); i++; }
        return sb.Length > 0 && int.TryParse(sb.ToString(), out int v) ? v : -1;
    }

    // ─── Parsing affine dal meta JSON ─────────────────────────────────────────
    // Il meta JSON contiene: "affine": [[r0c0, r0c1, r0c2, r0c3], [r1...], ...]
    // Formato row-major; Unity Matrix4x4 è column-major → trasporre al caricamento.
    static Matrix4x4 ParseAffineFromMetaJson(string metaPath)
    {
        string raw = File.ReadAllText(metaPath);

        int start = raw.IndexOf("\"affine\"");
        if (start < 0)
        {
            Debug.LogWarning("[CTLoader] Campo 'affine' non trovato nel meta JSON.");
            return Matrix4x4.identity;
        }

        start = raw.IndexOf('[', start);
        int depth = 0, end = start;
        for (int i = start; i < raw.Length; i++)
        {
            if      (raw[i] == '[') depth++;
            else if (raw[i] == ']') { depth--; if (depth == 0) { end = i; break; } }
        }

        string arrayStr = raw.Substring(start, end - start + 1);

        // Estrai tutti i float
        var nums = new List<float>();
        var sb   = new StringBuilder();
        foreach (char c in arrayStr)
        {
            if (char.IsDigit(c) || c == '.' || c == '-' || c == 'e' || c == 'E' || c == '+')
                sb.Append(c);
            else if (sb.Length > 0)
            {
                if (float.TryParse(sb.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                    nums.Add(v);
                sb.Clear();
            }
        }
        if (sb.Length > 0 && float.TryParse(sb.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float last))
            nums.Add(last);

        if (nums.Count < 12)
        {
            Debug.LogWarning("[CTLoader] Affine nel meta JSON ha meno di 12 valori.");
            return Matrix4x4.identity;
        }

        // Riempi la matrice 4x4 (row-major dal JSON → column-major Unity)
        Matrix4x4 m = Matrix4x4.identity;
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                int idx = row * 4 + col;
                if (idx < nums.Count) m[row, col] = nums[idx];
            }

        return m;
    }

}

// ─── STRUTTURE JSON ───────────────────────────────────────────────────────────
[System.Serializable]
public class FractureEntry
{
    public int    rib_id;
    public string predicted_class;
    public float  confidence;
    // Probabilita per classe. Ogni classificatore popola solo i campi che gli
    // competono; gli altri restano 0 (JsonUtility ignora le chiavi assenti).
    public float  prob_displaced;
    public float  prob_nondisplaced;
    public float  prob_buckle;
    public float  prob_segmental;
    public float  prob_severe;
    public float  prob_nonsevere;
}

[System.Serializable]
public class PredictionData
{
    public string          public_id;
    public int             n_fractures;
    public FractureEntry[] fractures;
}

[System.Serializable]
public class AmbiguousEntry
{
    public int  rib_id;
    public bool ambiguous;
}

[System.Serializable]
public class AmbiguousData
{
    public string           public_id;
    public int              n_fractures;
    public AmbiguousEntry[] fractures;
}
