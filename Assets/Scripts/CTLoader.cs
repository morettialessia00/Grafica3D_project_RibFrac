using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityVolumeRendering;

public class CTLoader : MonoBehaviour
{
    [Header("Paths")]
    public string niftiPath      = ""; // es. C:/..../RibFrac108-image.nii.gz
    public string predictionsPath = ""; // es. C:/..../RibFrac108_predictions.json
    public string objFolder      = ""; // es. C:/..../Models/Fractures/RibFrac108/

    private VolumeDataset        _dataset;
    private VolumeRenderedObject _volObj;

    void Start()
    {
        StartCoroutine(LoadAll());
    }

    IEnumerator LoadAll()
    {
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

        // ── Calcola soglia osso dal range HU reale del dataset ────────────────
        // GetMinDataValue/GetMaxDataValue restituiscono i valori grezzi del voxel
        // (unità HU, tipicamente: aria ≈ -1024, tessuto molle ≈ -100..80, osso ≈ 300..1800)
        float minVal = _dataset.GetMinDataValue();
        float maxVal = _dataset.GetMaxDataValue();
        Debug.Log($"[CTLoader] HU range: [{minVal:F0}, {maxVal:F0}]");

        // Soglie HU per l'osso (valori clinici standard)
        const float HU_BONE_LOW  = 200f;   // inizio osso trabecolare
        const float HU_BONE_HIGH = 700f;   // osso corticale denso

        // Normalizza in [0,1] rispetto al range del dataset
        float normLow  = Mathf.Clamp01((HU_BONE_LOW  - minVal) / (maxVal - minVal));
        float normHigh = Mathf.Clamp01((HU_BONE_HIGH - minVal) / (maxVal - minVal));
        // Margine minimo tra i due punti per evitare la ramp collassata
        if (normHigh - normLow < 0.01f) normHigh = Mathf.Min(normLow + 0.03f, 1f);

        Debug.Log($"[CTLoader] Soglia osso normalizzata: [{normLow:F3}, {normHigh:F3}]");

        UnityVolumeRendering.TransferFunction tf =
            ScriptableObject.CreateInstance<UnityVolumeRendering.TransferFunction>();

        // Colori: buio → beige osseo → bianco a densità massima
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(0.00f,    new Color(0.05f, 0.05f, 0.05f)));
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(normLow,  new Color(0.88f, 0.82f, 0.74f)));
        tf.colourControlPoints.Add(new UnityVolumeRendering.TFColourControlPoint(1.00f,    new Color(1.00f, 1.00f, 1.00f)));

        // Alpha: trasparente sotto l'osso, salita ripida, opacità piena sull'osso corticale
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(0.00f,           0.00f));
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(normLow - 0.005f, 0.00f)); // under-threshold: zero
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(normLow,          0.00f)); // soglia inferiore
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(normHigh,         1.00f)); // salita ripida
        tf.alphaControlPoints.Add(new UnityVolumeRendering.TFAlphaControlPoint(1.00f,            1.00f));

        tf.GenerateTexture();
        _volObj.SetTransferFunction(tf);

        // ── Aumenta il numero di campioni per ridurre i buchi nell'osso corticale
        // Default = 1.0; 4.0 quadruplica i passi del ray caster → meno gaps su strutture sottili
        _volObj.SetSamplingRateMultiplier(4.0f);

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

        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"[CTLoader] JSON predizioni non trovato: {jsonPath}");
            yield break;
        }

        string json = File.ReadAllText(jsonPath);
        PredictionData data = JsonUtility.FromJson<PredictionData>(json);

        if (data == null || data.fractures == null)
        {
            Debug.LogError("[CTLoader] JSON malformato.");
            yield break;
        }

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
        Matrix4x4 T_vox = new Matrix4x4(
            new Vector4(pixX,  0f,    0f,    0f),  // i  → Unity X
            new Vector4(0f,    0f,    pixY,  0f),  // j  → Unity Z
            new Vector4(0f,   -pixZ,  0f,    0f),  // k  → Unity Y (invertito)
            new Vector4(-cx,   cz,   -cy,    1f)   // traslazione
        );

        GameObject fracturesParent = new GameObject($"{data.public_id}_fractures");

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
            go.transform.SetParent(fracturesParent.transform);

            // Palette color-blind safe (piano sezione 4.3)
            Color col = frac.predicted_class switch
            {
                "Displaced"     => new Color(0.91f, 0.47f, 0.13f), // arancio #E87722
                "Non-displaced" => new Color(0.29f, 0.56f, 0.85f), // blu     #4A90D9
                "Buckle"        => new Color(0.61f, 0.35f, 0.71f), // viola   #9B59B6
                _               => Color.gray
            };

            foreach (var rend in go.GetComponentsInChildren<Renderer>())
                rend.material.color = col;

            Debug.Log($"[CTLoader] Caricata: {filename} → {frac.predicted_class} (conf={frac.confidence:P0})");
            yield return null;
        }

        Debug.Log($"[CTLoader] Fratture caricate: {data.fractures.Length}");
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
