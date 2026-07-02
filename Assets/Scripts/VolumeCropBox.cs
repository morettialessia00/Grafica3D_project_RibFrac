using UnityEngine;
using UnityVolumeRendering;

/// <summary>
/// Applica un CutoutBox (Inclusive) al volume CT per isolare la cassa toracica
/// tagliando braccia, lettino e altri elementi periferici.
///
/// Setup in Unity:
///   1. Aggiungi questo script a qualsiasi GameObject nella scena (es. CTLoader).
///   2. Nell'Inspector di CTLoader, trascina il GameObject nel campo "Volume Crop Box".
///   3. Il crop viene applicato automaticamente ad ogni caricamento paziente.
///   4. Regola cropX / cropY / cropZ e offsetX/Y/Z in Play Mode per trovare
///      il taglio ottimale — i cambiamenti sono visibili in tempo reale.
///
/// Come funziona:
///   Il CutoutBox Inclusive mostra SOLO i voxel dentro il box.
///   Il box è un figlio del volumeContainerObject, quindi le sue coordinate
///   sono normalizzate [−0.5, +0.5] rispetto al volume.
///   cropX=0.65 → mostra il 65 % della larghezza (taglia ~17.5 % per lato).
/// </summary>
public class VolumeCropBox : MonoBehaviour
{
    [Header("Dimensioni crop (0=niente, 1=volume intero)")]
    [Tooltip("Riduce la larghezza (sinistra-destra). Abbassalo per tagliare le braccia.")]
    [Range(0.1f, 1f)] public float cropX = 0.80f;

    [Tooltip("Riduce l'altezza (cranio-caudale). 1 = nessun taglio verticale.")]
    [Range(0.1f, 1f)] public float cropY = 0.65f;

    [Tooltip("Riduce la profondità (antero-posteriore). 1 = nessun taglio.")]
    [Range(0.1f, 1f)] public float cropZ = 1.0f;

    [Header("Offset centro (spazio normalizzato, 0 = centrato)")]
    [Tooltip("Sposta il box sull'asse sinistra-destra.")]
    [Range(-0.4f, 0.4f)] public float offsetX = 0f;

    [Tooltip("Sposta il box sull'asse cranio-caudale.")]
    [Range(-0.4f, 0.4f)] public float offsetY = 0f;

    [Tooltip("Sposta il box sull'asse antero-posteriore.")]
    [Range(-0.4f, 0.4f)] public float offsetZ = 0f;

    // ── Stato interno ─────────────────────────────────────────────────────────
    private CutoutBox _cutoutBox;

    // ── API pubblica ──────────────────────────────────────────────────────────

    /// <summary>
    /// Crea (o ricrea) il CutoutBox sul volume appena caricato.
    /// Chiamato da CTLoader subito dopo VolumeObjectFactory.CreateObject().
    /// </summary>
    public void Apply(VolumeRenderedObject volObj)
    {
        // Rimuovi il box del paziente precedente
        if (_cutoutBox != null)
        {
            Destroy(_cutoutBox.gameObject);
            _cutoutBox = null;
        }

        if (volObj == null) return;

        // Il box è figlio del volumeContainerObject:
        // le sue localScale/localPosition sono già in spazio normalizzato del volume.
        GameObject boxGO = new GameObject("VolumeCropBox_Instance");
        boxGO.transform.SetParent(volObj.volumeContainerObject.transform, false);
        boxGO.transform.localRotation = Quaternion.identity;

        _cutoutBox = boxGO.AddComponent<CutoutBox>();
        _cutoutBox.cutoutType = CutoutType.Inclusive;
        _cutoutBox.SetTargetObject(volObj);

        // Applica subito le dimensioni correnti
        ApplyTransform();
    }

    /// <summary>Rimuove il crop (es. al reset paziente).</summary>
    public void Clear()
    {
        if (_cutoutBox != null)
        {
            Destroy(_cutoutBox.gameObject);
            _cutoutBox = null;
        }
    }

    // ── Aggiornamento in tempo reale ──────────────────────────────────────────

    void Update()
    {
        // Aggiorna la trasformazione ogni frame: permette di regolare
        // i valori nell'Inspector in Play Mode e vedere il risultato live.
        ApplyTransform();
    }

    void ApplyTransform()
    {
        if (_cutoutBox == null) return;
        _cutoutBox.transform.localPosition = new Vector3(offsetX, offsetY, offsetZ);
        _cutoutBox.transform.localScale    = new Vector3(cropX,   cropY,   cropZ);
    }
}
