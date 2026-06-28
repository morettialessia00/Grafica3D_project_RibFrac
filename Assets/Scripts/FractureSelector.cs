using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Raycast al click sinistro → apre FractureDetailPanel sulla frattura colpita.
/// Click su area vuota o oggetto non-frattura → chiude il pannello.
///
/// Setup in Unity:
///   1. Hierarchy → Right Click → Create Empty. Rinomina "FractureSelector".
///   2. Inspector → Add Component → cerca "FractureSelector".
///   CTLoader e FractureDetailPanel vengono trovati automaticamente a runtime.
/// </summary>
public class FractureSelector : MonoBehaviour
{
    private CTLoader            _ctLoader;
    private FractureDetailPanel _detailPanel;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        _ctLoader    = FindAnyObjectByType<CTLoader>();
        _detailPanel = FindAnyObjectByType<FractureDetailPanel>();

        if (_ctLoader == null)
            Debug.LogError("[FractureSelector] CTLoader non trovato in scena.");
        if (_detailPanel == null)
            Debug.LogError("[FractureSelector] FractureDetailPanel non trovato in scena.");
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // Reagisce solo al frame esatto del click — non ai frame di hold
        if (!mouse.leftButton.wasPressedThisFrame) return;

        // Non intercettare click su elementi UI (bottoni, toggle, ecc.)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector2 screenPos = mouse.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            int ribId = ParseRibId(hit.collider.gameObject.name);

            if (ribId >= 0)
            {
                // Mesh-frattura colpita: cerca i dati nel dizionario predizioni.
                // Per fratture non classificate (segmental/ambigue) il TryGetValue
                // restituisce false e entry rimane null → FractureDetailPanel
                // mostra "Unclassified" senza dati di confidenza.
                FractureEntry entry = null;
                _ctLoader?.FractureData.TryGetValue(ribId, out entry);
                _detailPanel?.Show(ribId, entry);
                return;
            }
        }

        // Click su area vuota o su un oggetto che non è una frattura
        _detailPanel?.Hide();
    }

    // ── Helper: parsa rib_id dal nome del GameObject ──────────────────────────
    // Formato atteso: "rib_03_Displaced", "rib_07_Unclassified", ecc.
    // Restituisce -1 se il nome non corrisponde al pattern.
    static int ParseRibId(string goName)
    {
        // Tutti i GameObject-frattura iniziano con "rib_" (assegnato in CTLoader)
        if (!goName.StartsWith("rib_")) return -1;

        string[] parts = goName.Split('_');
        // parts[0] = "rib", parts[1] = "03", parts[2+] = classe
        if (parts.Length < 3) return -1;

        return int.TryParse(parts[1], out int id) ? id : -1;
    }
}
