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
    private CameraController    _camCtrl;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        _ctLoader    = FindAnyObjectByType<CTLoader>();
        _detailPanel = FindAnyObjectByType<FractureDetailPanel>();
        _camCtrl     = FindAnyObjectByType<CameraController>();

        if (_ctLoader == null)
            Debug.LogError("[FractureSelector] CTLoader non trovato in scena.");
        if (_detailPanel == null)
            Debug.LogError("[FractureSelector] FractureDetailPanel non trovato in scena.");
        if (_camCtrl == null)
            Debug.LogWarning("[FractureSelector] CameraController non trovato — fly-to disabilitato.");
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool inClassifierMode = _ctLoader != null &&
                                FractureClasses.IsClassifierMode(_ctLoader.CurrentColorMode);

        // ── Hover highlight (ogni frame) ──────────────────────────────────────
        // Aggiorna quale frattura è sotto il cursore per il boost colore in CTLoader.
        if (!overUI && inClassifierMode)
        {
            Vector2 screenPos = mouse.position.ReadValue();
            Ray hoverRay = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

            if (Physics.Raycast(hoverRay, out RaycastHit hoverHit))
            {
                var info = hoverHit.collider.GetComponent<FractureMeshInfo>();
                _ctLoader.SetHoveredObject(info != null ? hoverHit.collider.gameObject : null);
            }
            else
            {
                _ctLoader.SetHoveredObject(null);
            }
        }
        else
        {
            _ctLoader?.SetHoveredObject(null);
        }

        // ── Click sinistro ────────────────────────────────────────────────────
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (overUI) return;

        // Il pannello dettaglio è disponibile solo in modalità classificatore.
        if (!inClassifierMode)
        {
            _detailPanel?.Hide();
            return;
        }

        Vector2 clickPos = mouse.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(new Vector3(clickPos.x, clickPos.y, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            var info = hit.collider.GetComponent<FractureMeshInfo>();

            if (info != null)
            {
                int ribId = info.ribId;

                // Mostra pannello dettaglio. Le mesh ambigue non hanno predizione.
                FractureEntry entry = null;
                if (!info.isAmbiguous)
                    _ctLoader?.FractureData.TryGetValue(ribId, out entry);
                _detailPanel?.Show(ribId, entry, _ctLoader != null ? _ctLoader.CurrentClassifierIndex : -1);

                // Centra e zooma la camera sulla frattura colpita
                Renderer rend = hit.collider.GetComponentInChildren<Renderer>()
                             ?? hit.collider.GetComponent<Renderer>();
                Vector3 center = rend != null ? rend.bounds.center : hit.point;

                // Distanza target: 3.5× la diagonale del bounding box, clampata [60, 300]
                float diagonal   = rend != null ? rend.bounds.size.magnitude : 100f;
                float targetDist = Mathf.Clamp(diagonal * 3.5f, 60f, 300f);
                _camCtrl?.FlyToTarget(center, targetDist);

                return;
            }
        }

        // Click su area vuota o su un oggetto che non è una frattura
        _detailPanel?.Hide();
    }
}
