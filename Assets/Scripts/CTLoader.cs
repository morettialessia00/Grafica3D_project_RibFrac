using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityVolumeRendering;

public class CTLoader : MonoBehaviour
{
    [Header("Paths")]
    public string niftiPath       = ""; // es. C:/..../RibFrac108-image.nii.gz
    public string predictionsPath = ""; // es. C:/..../RibFrac108_predictions.json
    public string ambiguousPath   = ""; // es. C:/..../RibFrac108_ambiguous.json
    public string objFolder       = ""; // es. C:/..../Models/Fractures/RibFrac108/

    [Header("Visualizzazione")]
    [Tooltip("Fattore di scala uniforme del volume CT. Aumenta se la CT appare troppo piccola.")]
    public float ctScale = 1f;

    [Header("UI")]
    public CaseInfoPanel caseInfoPanel; // Trascina qui il GameObject con CaseInfoPanel
    [Tooltip("Il pulsante Load DATA — viene disabilitato dopo il primo caricamento")]
    public UnityEngine.UI.Button loadButton;

    private VolumeDataset        _dataset;
    private VolumeRenderedObject _volObj;
    private GameObject           _fracturesParent;

    // Buffer riusabile per il sort depth-based (nessuna allocazione per frame)
    private readonly List<(float sqDist, Renderer rend)> _depthSortBuffer
        = new List<(float, Renderer)>();

    // ── Dati predizioni esposti (rib_id → FractureEntry) ─────────────────────
    public Dictionary<int, FractureEntry> FractureData { get; private set; }
        = new Dictionary<int, FractureEntry>();
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

    // ── Punto 4: logica di colorazione a 3 stati ──────────────────────────────
    // 0 = Nessuna lesione  → mesh fratture nascoste
    // 1 = Lesione binaria  → tutte le mesh rosse
    // 2 = Lesione per tipo → colori per classe (Displaced / Non-displaced / Buckle)
    public int CurrentColorMode { get; private set; } = 0;

    public void ApplyColorMode(int mode)
    {
        CurrentColorMode = mode;

        if (_fracturesParent == null) return;

        if (mode == 0)
        {
            _fracturesParent.SetActive(false);
            return;
        }

        _fracturesParent.SetActive(true);

        foreach (Transform child in _fracturesParent.transform)
        {
            Color col = GetColorForChild(child.name, mode);
            foreach (var rend in child.GetComponentsInChildren<Renderer>())
                rend.material.color = col;
        }
    }

    // Nome GameObject: "rib_XX_Classe" (es. "rib_01_Displaced", "rib_02_Non-displaced")
    static Color GetColorForChild(string goName, int mode)
    {
        if (mode == 1) return new Color(1f, 0.15f, 0.15f, 0.35f); // rosso semi-trasparente

        // mode 2: colore per classe
        string[] parts = goName.Split('_');
        string cls = parts.Length >= 3 ? parts[2] : "";
        return cls switch
        {
            "Displaced"     => new Color(0.91f, 0.47f, 0.13f, 0.35f), // arancio #E87722
            "Non-displaced" => new Color(0.29f, 0.56f, 0.85f, 0.35f), // blu     #4A90D9
            "Buckle"        => new Color(0.61f, 0.35f, 0.71f, 0.35f), // viola   #9B59B6
            _               => new Color(0.5f, 0.5f, 0.5f, 0.35f)
        };
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

        for (int i = 0; i < _depthSortBuffer.Count; i++)
            _depthSortBuffer[i].rend.rendererPriority = i;
    }

    // ── Punto 3.3: caricamento esplicito, chiamato dal pulsante "Load DATA" ──
    public void LoadData()
    {
        StartCoroutine(LoadAll());
    }

    IEnumerator LoadAll()
    {
        // Disabilita il pulsante subito: evita caricamenti multipli
        if (loadButton != null) loadButton.interactable = false;

        yield return StartCoroutine(LoadVolume(niftiPath));
        yield return StartCoroutine(LoadFractures(predictionsPath, objFolder));

        // Framing automatico della camera dopo il caricamento completo
        CameraController cam = FindAnyObjectByType<CameraController>();
        if (cam != null)
            cam.AutoFrame();
        else
            Debug.LogWarning("[CTLoader] CameraController non trovato in scena.");
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
        float nBone   = N( 300f, 0.61f); // osso spongioso (inizio osso)
        float nCortex = N( 700f, 0.74f); // osso corticale denso

        Debug.Log($"[CTLoader] TF positions — lung:{nLung:F3} fat:{nFat:F3} bone:{nBone:F3} cortex:{nCortex:F3}");

        UnityVolumeRendering.TransferFunction tf =
            ScriptableObject.CreateInstance<UnityVolumeRendering.TransferFunction>();

        // ── Colori: scala di grigio CT clinica ────────────────────────────────
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(0f,       new Color(0.00f, 0.00f, 0.00f))); // nero (aria)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nLung,    new Color(0.10f, 0.10f, 0.10f))); // grigio scuro (polmone)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nFat,     new Color(0.28f, 0.26f, 0.24f))); // grigio medio-scuro (grasso)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nMuscle,  new Color(0.48f, 0.44f, 0.40f))); // grigio medio (muscolo)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nBone,    new Color(0.82f, 0.78f, 0.70f))); // beige chiaro (osso spongioso)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(nCortex,  new Color(0.96f, 0.96f, 0.94f))); // quasi bianco (osso corticale)
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(1f,       new Color(1.00f, 1.00f, 1.00f))); // bianco (massima densità)

        // ── Alpha: quasi trasparente per aria/molle, sempre più opaco verso l'osso ──
        // L'obiettivo è vedere il contorno del torace (faint) e le coste chiaramente.
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(0f,       0.000f)); // fuori corpo: zero
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nAir,     0.000f)); // aria: zero
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nLung,    0.006f)); // polmone: quasi trasparente
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nFat,     0.010f)); // tessuto molle: faint
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nMuscle,  0.018f)); // muscolo: lievemente visibile
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nBone,    0.12f));  // osso spongioso: salto di opacità
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(nCortex,  0.85f));  // osso corticale: quasi pieno
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(1f,       1.00f));  // densità massima: opaco

        tf.GenerateTexture();
        _volObj.SetTransferFunction(tf);

        // Campionamento: 3× è un buon compromesso qualità/performance per le coste
        _volObj.SetSamplingRateMultiplier(3.0f);

        Debug.Log("[CTLoader] CT caricata.");
        yield return null;
    }

    // ─── FRATTURE OBJ ─────────────────────────────────────────────────────────
    IEnumerator LoadFractures(string jsonPath, string objDir)
    {
        if (_dataset == null)
        {
            Debug.LogError("[CTLoader] Dataset non disponibile, skip fratture.");
            yield break;
        }

        // ── Punto 3.4: JSON mancante → paziente non nel test set ─────────────
        if (!File.Exists(jsonPath))
        {
            HasPredictions    = false;
            CurrentPatientId  = ExtractPatientId(jsonPath);
            Debug.LogWarning($"[CTLoader] JSON predizioni non trovato: {jsonPath}. " +
                             "Paziente non nel test set.");
            caseInfoPanel?.ShowNotInTestSet(CurrentPatientId);
            yield break;
        }

        string json = File.ReadAllText(jsonPath);
        PredictionData data = JsonUtility.FromJson<PredictionData>(json);

        if (data == null || data.fractures == null)
        {
            Debug.LogError("[CTLoader] JSON malformato.");
            yield break;
        }

        // ── Punto 3.1: costruisce il dizionario rib_id → FractureEntry ────────
        FractureData.Clear();
        foreach (var f in data.fractures)
            FractureData[f.rib_id] = f;

        CurrentPatientId = data.public_id;
        HasPredictions   = true;

        // Il pannello Case INFO viene aggiornato alla fine, dopo aver contato
        // anche le fratture segmentali e ambigue (vedi chiamata a caseInfoPanel?.Refresh
        // alla fine di questo metodo).

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

        _fracturesParent = new GameObject($"{data.public_id}_fractures");
        _fracturesParent.SetActive(false); // nascoste finché l'utente non preme il pulsante

        // Traccia i rib_id già caricati per evitare duplicati tra i tre tipi
        var loadedRibIds = new HashSet<int>();
        int nUnclassified = 0; // segmental + ambigue

        // ── FRATTURE CLASSIFICATE ─────────────────────────────────────────────
        foreach (var frac in data.fractures)
        {
            string filename = $"{data.public_id}_fracture{frac.rib_id:D2}_mesh.obj";
            string metaName = $"{data.public_id}_fracture{frac.rib_id:D2}_meta.json";
            string objPath  = Path.Combine(objDir, filename);
            string metaPath = Path.Combine(objDir, metaName);

            if (!File.Exists(objPath))
            {
                Debug.LogWarning($"[CTLoader] OBJ non trovato: {objPath}");
                continue;
            }

            // ── Calcola trasformazione OBJ mm → Unity world ───────────────────
            // Le mesh OBJ sono in mm world space (trasformate con l'affine NIfTI dal pipeline Python).
            // Serve: affine⁻¹ per tornare in spazio voxel, poi T_vox per Unity.
            Matrix4x4 T_full = Matrix4x4.identity;
            if (File.Exists(metaPath))
            {
                Matrix4x4 affine    = ParseAffineFromMetaJson(metaPath);
                Matrix4x4 affineInv = affine.inverse;
                T_full = T_vox * affineInv;
            }
            else
            {
                Debug.LogWarning($"[CTLoader] Meta JSON non trovato: {metaPath}. " +
                                 "Le mesh potrebbero non essere allineate.");
            }

            GameObject go = SimpleOBJLoader.Load(objPath, vertexTransform: T_full);
            if (go == null) continue;

            go.name = $"rib_{frac.rib_id:D2}_{frac.predicted_class}";
            go.transform.SetParent(_fracturesParent.transform);
            go.AddComponent<MeshCollider>(); // punto 5.1: necessario per il raycast al click
            loadedRibIds.Add(frac.rib_id);

            // Palette color-blind safe (piano sezione 4.3)
            Color col = frac.predicted_class switch
            {
                "Displaced"     => new Color(0.91f, 0.47f, 0.13f, 0.35f), // arancio #E87722
                "Non-displaced" => new Color(0.29f, 0.56f, 0.85f, 0.35f), // blu     #4A90D9
                "Buckle"        => new Color(0.61f, 0.35f, 0.71f, 0.35f), // viola   #9B59B6
                _               => new Color(0.5f, 0.5f, 0.5f, 0.35f)
            };

            foreach (var rend in go.GetComponentsInChildren<Renderer>())
                rend.material.color = col;

            Debug.Log($"[CTLoader] Caricata: {filename} → {frac.predicted_class} (conf={frac.confidence:P0})");
            yield return null;
        }

        // ── FRATTURE SEGMENTALI ───────────────────────────────────────────────
        // Non appaiono in predictions.json → scansiono il folder e le tratto come non classificate.
        if (Directory.Exists(objDir))
        {
            foreach (string mPath in Directory.GetFiles(objDir, "*_fracture*_meta.json"))
            {
                if (ParseLabelNameFromMeta(mPath) != "segmental") continue;

                int ribId = ParseLabelIdFromMeta(mPath);
                if (ribId < 0 || loadedRibIds.Contains(ribId)) continue;

                string meshFile = mPath.Replace("_meta.json", "_mesh.obj");
                if (!File.Exists(meshFile)) continue;

                Matrix4x4 affine = ParseAffineFromMetaJson(mPath);
                Matrix4x4 T_full = T_vox * affine.inverse;

                GameObject go = SimpleOBJLoader.Load(meshFile, vertexTransform: T_full);
                if (go == null) continue;

                go.name = $"rib_{ribId:D2}_Unclassified";
                go.transform.SetParent(_fracturesParent.transform);
                go.AddComponent<MeshCollider>(); // punto 5.1
                loadedRibIds.Add(ribId);
                nUnclassified++;

                foreach (var rend in go.GetComponentsInChildren<Renderer>())
                    rend.material.color = new Color(0.5f, 0.5f, 0.5f, 0.35f);

                Debug.Log($"[CTLoader] Caricata segmental (non classificata): {Path.GetFileName(meshFile)}");
                yield return null;
            }
        }

        // ── FRATTURE AMBIGUE ──────────────────────────────────────────────────
        // Caricate da ambiguousPath ({id}_ambiguous.json) → mesh _ambiguous{N}_mesh.obj.
        if (!string.IsNullOrEmpty(ambiguousPath) && File.Exists(ambiguousPath))
        {
            string ambJson = File.ReadAllText(ambiguousPath);
            AmbiguousData ambData = JsonUtility.FromJson<AmbiguousData>(ambJson);

            if (ambData?.fractures != null)
            {
                foreach (var amb in ambData.fractures)
                {
                    string meshFile = Path.Combine(objDir,
                        $"{data.public_id}_ambiguous{amb.rib_id:D2}_mesh.obj");
                    string mPath = Path.Combine(objDir,
                        $"{data.public_id}_ambiguous{amb.rib_id:D2}_meta.json");

                    if (!File.Exists(meshFile))
                    {
                        Debug.LogWarning($"[CTLoader] OBJ ambiguous non trovato: {meshFile}");
                        continue;
                    }

                    Matrix4x4 T_full = Matrix4x4.identity;
                    if (File.Exists(mPath))
                    {
                        Matrix4x4 affine = ParseAffineFromMetaJson(mPath);
                        T_full = T_vox * affine.inverse;
                    }
                    else
                    {
                        Debug.LogWarning($"[CTLoader] Meta JSON ambiguous non trovato: {mPath}.");
                    }

                    GameObject go = SimpleOBJLoader.Load(meshFile, vertexTransform: T_full);
                    if (go == null) continue;

                    go.name = $"rib_{amb.rib_id:D2}_Unclassified";
                    go.transform.SetParent(_fracturesParent.transform);
                    go.AddComponent<MeshCollider>(); // punto 5.1
                    nUnclassified++;

                    foreach (var rend in go.GetComponentsInChildren<Renderer>())
                        rend.material.color = new Color(0.5f, 0.5f, 0.5f, 0.35f);

                    Debug.Log($"[CTLoader] Caricata ambiguous: {Path.GetFileName(meshFile)}");
                    yield return null;
                }

                Debug.Log($"[CTLoader] Fratture ambigue caricate: {ambData.fractures.Length}");
            }
        }

        // ── Punto 3.2: aggiorna il pannello Case INFO con tutti i conteggi ──────
        // Usa childCount invece di loadedRibIds.Count: le fratture ambigue vengono
        // caricate ma non aggiunte al HashSet, quindi childCount è l'unico conteggio affidabile.
        int totalLoaded = _fracturesParent.transform.childCount;
        caseInfoPanel?.Refresh(data, totalLoaded, nUnclassified);

        Debug.Log($"[CTLoader] Caricamento completato. " +
                  $"Classificate: {data.fractures.Length}, Non classificate: {nUnclassified}, Totale: {loadedRibIds.Count}");
    }

    // ─── Helper: estrae public_id dal percorso del file JSON ─────────────────
    // Es. "C:/.../RibFrac108_predictions.json" → "RibFrac108"
    // Es. "C:/.../RibFrac108_predictions.json" → "RibFrac108"
    static string ExtractPatientId(string jsonPath)
    {
        const string suffix = "_predictions";
        string name = Path.GetFileNameWithoutExtension(jsonPath);
        return name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;
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

    // ─── Helper: legge label_name dal meta JSON ─────────────────────────────
    static string ParseLabelNameFromMeta(string metaPath)
    {
        string raw = File.ReadAllText(metaPath);
        int idx = raw.IndexOf("\"label_name\"");
        if (idx < 0) return "";
        int colon = raw.IndexOf(':', idx);
        if (colon < 0) return "";
        int q1 = raw.IndexOf('"', colon + 1);
        if (q1 < 0) return "";
        int q2 = raw.IndexOf('"', q1 + 1);
        if (q2 < 0) return "";
        return raw.Substring(q1 + 1, q2 - q1 - 1);
    }

    // ─── Helper: legge label_id dal meta JSON ──────────────────────────────
    static int ParseLabelIdFromMeta(string metaPath)
    {
        string raw = File.ReadAllText(metaPath);
        int idx = raw.IndexOf("\"label_id\"");
        if (idx < 0) return -1;
        int colon = raw.IndexOf(':', idx);
        if (colon < 0) return -1;
        int start = colon + 1;
        while (start < raw.Length && (raw[start] == ' ' || raw[start] == '\n' ||
                                      raw[start] == '\r' || raw[start] == '\t'))
            start++;
        var sb2 = new StringBuilder();
        for (int i = start; i < raw.Length && char.IsDigit(raw[i]); i++)
            sb2.Append(raw[i]);
        return sb2.Length > 0 && int.TryParse(sb2.ToString(), out int v) ? v : -1;
    }
}

// ─── STRUTTURE JSON ───────────────────────────────────────────────────────────
[System.Serializable]
public class FractureEntry
{
    public int    rib_id;
    public string predicted_class;
    public float  confidence;
    public float  prob_displaced;
    public float  prob_nondisplaced;
    public float  prob_buckle;
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
