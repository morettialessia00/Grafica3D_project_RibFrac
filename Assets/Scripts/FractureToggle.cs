using UnityEngine;
using TMPro;

/// <summary>
/// Collega un TMP_Dropdown alla logica di colorazione di CTLoader.
/// Il valore del dropdown mappa direttamente sul mode:
///   0 – No lesions      : mesh fratture nascoste
///   1 – All lesions     : tutte le mesh rosse (vista binaria)
///   2 – Classificatore 1 : colori per classe (JSON 4classes)
///   3 – Classificatore 2 : colori per classe (JSON 3classes D_ND_B)
///   4 – Classificatore 3 : colori per classe (JSON 3classes SD_ND_B)
///   5 – Classificatore 4 : colori per classe (JSON 2classes SD_NDB)
///
/// Le opzioni della tendina vengono popolate da codice all'avvio
/// (FractureClasses.DropdownOptions) — non serve configurarle nell'Inspector.
///
/// Setup in Unity:
///   1. Crea GameObject → UI → Dropdown - TextMeshPro nella Canvas.
///   2. Aggiungi questo script DIRETTAMENTE al GameObject del Dropdown
///      (Add Component → FractureToggle). Lo script trova il Dropdown da solo.
///   3. Trascina il GameObject CTLoader nel campo "Ct Loader".
///   4. NON serve assegnare il campo Dropdown né configurare On Click ().
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

        // Popola le 6 opzioni da codice (No lesions / All lesions / 4 classificatori)
        dropdown.ClearOptions();
        dropdown.AddOptions(FractureClasses.DropdownOptions());

        // Disabilitato finché i dati non sono caricati
        dropdown.interactable = false;

        // Sincronizza il valore iniziale senza triggerare il callback
        dropdown.SetValueWithoutNotify(ctLoader != null ? ctLoader.CurrentColorMode : 0);
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }

    /// <summary>Chiamato da CTLoader dopo il caricamento completo.</summary>
    public void SetInteractable(bool value)
    {
        if (dropdown == null) return;

        // A ogni nuovo caricamento riparti da "No lesions" (mode 0), coerente
        // con CTLoader che resetta CurrentColorMode a 0.
        if (value) dropdown.SetValueWithoutNotify(0);
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
