using UnityEngine;
using TMPro;

/// <summary>
/// Collega un TMP_Dropdown alla logica di colorazione a 3 stati di CTLoader.
/// Il valore del dropdown mappa direttamente sul mode:
///   0 – Nessuna lesione  : mesh fratture nascoste
///   1 – Lesione binaria  : tutte le mesh rosse
///   2 – Lesione per tipo : colori per classe (Displaced / Non-displaced / Buckle)
///
/// Setup in Unity:
///   1. Crea GameObject → UI → Dropdown - TextMeshPro nella Canvas.
///   2. Nel Dropdown Inspector → Options: aggiungi 3 voci nell'ordine:
///        "Nessuna lesione" / "Lesione binaria" / "Lesione per tipo"
///   3. Aggiungi questo script DIRETTAMENTE al GameObject del Dropdown
///      (Add Component → FractureToggle). Lo script trova il Dropdown da solo.
///   4. Trascina il GameObject CTLoader nel campo "Ct Loader".
///   5. NON serve assegnare il campo Dropdown né configurare On Click ().
/// </summary>
public class FractureToggle : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Il CTLoader nella scena")]
    public CTLoader ctLoader;

    [Tooltip("Lascia vuoto: viene trovato automaticamente se lo script è sul Dropdown")]
    public TMP_Dropdown dropdown;

    void Start()
    {
        // Auto-discovery: se non assegnato manualmente, cerca sul proprio GameObject
        if (dropdown == null)
            dropdown = GetComponent<TMP_Dropdown>();

        if (dropdown == null)
        {
            Debug.LogError("[FractureToggle] TMP_Dropdown non trovato. " +
                           "Aggiungi questo script direttamente al GameObject del Dropdown.");
            return;
        }

        // Disabilitato finché i dati non sono caricati
        dropdown.interactable = false;

        // Sincronizza il valore iniziale senza triggerare il callback
        dropdown.SetValueWithoutNotify(ctLoader != null ? ctLoader.CurrentColorMode : 0);
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    /// <summary>Chiamato da CTLoader dopo il caricamento completo.</summary>
    public void SetInteractable(bool value)
    {
        if (dropdown != null)
            dropdown.interactable = value;
    }

    void OnDestroy()
    {
        if (dropdown != null)
            dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
    }

    private void OnDropdownChanged(int value)
    {
        if (ctLoader == null)
        {
            Debug.LogError("[FractureToggle] CTLoader non assegnato nel Inspector.");
            return;
        }

        ctLoader.ApplyColorMode(value);
    }
}
