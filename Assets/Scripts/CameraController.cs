using UnityEngine;
using UnityEngine.InputSystem;
using UnityVolumeRendering;

/// <summary>
/// Controlla la camera del visualizzatore.
/// - Auto-framing del volume al caricamento
/// - Tasto destro + drag  → orbita
/// - Scroll               → zoom
/// - Tasto centrale + drag → pan
/// - R                    → reset alla vista iniziale
///
/// Da aggiungere come componente alla Main Camera.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Sensibilità")]
    public float orbitSpeed  = 0.4f;
    public float zoomSpeed   = 0.15f;
    public float panSpeed    = 0.3f;

    // Stato orbita sferica
    private Vector3 _pivot     = Vector3.zero;
    private float   _distance  = 500f;
    private float   _yaw       = 0f;
    private float   _pitch     = 20f;

    // Posizione iniziale per Reset View
    private float _initYaw, _initPitch, _initDist;
    private Vector3 _initPivot;

    // Input
    private Vector2 _lastMousePos;

    void Start()
    {
        // Inizializza posizione mouse per evitare salto al primo frame
        if (Mouse.current != null)
            _lastMousePos = Mouse.current.position.ReadValue();

        // Imposta far clipping plane alto per volumi grandi in mm
        GetComponent<Camera>().farClipPlane = 100000f;
        GetComponent<Camera>().nearClipPlane = 0.1f;

        ApplyTransform();
    }

    /// <summary>
    /// Posiziona la camera per vedere l'intero volume.
    /// Chiamata da CTLoader dopo il caricamento completo.
    /// </summary>
    public void AutoFrame()
    {
        VolumeRenderedObject vol = FindAnyObjectByType<VolumeRenderedObject>();
        if (vol != null)
        {
            Bounds b = GetVolumeBounds(vol);
            _pivot    = b.center;
            _distance = b.extents.magnitude * 2.8f;
            _yaw      = 0f;
            _pitch    = 20f;
            Debug.Log($"[CameraController] AutoFrame: pivot={_pivot}, dist={_distance:F1}");
        }
        else
        {
            Debug.LogWarning("[CameraController] AutoFrame: nessun VolumeRenderedObject trovato.");
        }

        SaveInitialView();
        ApplyTransform();
    }

    void Update()
    {
        var mouse    = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null) return;

        bool rightDown  = mouse.rightButton.isPressed;
        bool middleDown = mouse.middleButton.isPressed;
        float scroll    = mouse.scroll.ReadValue().y;

        Vector2 mousePos = mouse.position.ReadValue();
        Vector2 delta    = mousePos - (Vector2)_lastMousePos;
        _lastMousePos    = mousePos;

        // ── Orbita (tasto destro) ─────────────────────────────────────────────
        if (rightDown)
        {
            _yaw   += delta.x * orbitSpeed;
            _pitch -= delta.y * orbitSpeed;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
        }

        // ── Pan (tasto centrale) ──────────────────────────────────────────────
        if (middleDown)
        {
            Vector3 right = transform.right;
            Vector3 up    = transform.up;
            float   scale = _distance * panSpeed * 0.01f;
            _pivot -= right * delta.x * scale;
            _pivot -= up    * delta.y * scale;
        }

        // ── Zoom (scroll) ─────────────────────────────────────────────────────
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _distance -= scroll * _distance * zoomSpeed * 0.01f;
            _distance  = Mathf.Max(_distance, 1f);
        }

        // ── Reset (R) ─────────────────────────────────────────────────────────
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            ResetView();

        if (rightDown || middleDown || Mathf.Abs(scroll) > 0.001f)
            ApplyTransform();
    }

    void ApplyTransform()
    {
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.position = _pivot + rot * new Vector3(0f, 0f, -_distance);
        transform.LookAt(_pivot);
    }

    void SaveInitialView()
    {
        _initYaw   = _yaw;
        _initPitch = _pitch;
        _initDist  = _distance;
        _initPivot = _pivot;
    }

    public void ResetView()
    {
        _yaw      = _initYaw;
        _pitch    = _initPitch;
        _distance = _initDist;
        _pivot    = _initPivot;
        ApplyTransform();
    }

    static Bounds GetVolumeBounds(VolumeRenderedObject vol)
    {
        Renderer[] renderers = vol.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(vol.transform.position, Vector3.one * 200f);

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);

        return b;
    }
}
