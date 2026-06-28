using UnityEngine;
using UnityEngine.InputSystem;
using UnityVolumeRendering;

/// <summary>
/// Controlla la camera del visualizzatore.
/// - Auto-framing del volume al caricamento
/// - Controlli via pannello UI (bottoni)
/// - enableMouseControls = true per ri-abilitare mouse (tasto destro orbita, scroll zoom, tasto centrale pan)
/// - R → reset alla vista iniziale
///
/// Da aggiungere come componente alla Main Camera.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Sensibilità Mouse")]
    public float orbitSpeed  = 0.4f;
    public float zoomSpeed   = 0.15f;
    public float panSpeed    = 0.3f;

    [Header("Velocità Bottoni UI")]
    public float buttonRotateSpeed = 60f;   // gradi al secondo
    public float buttonZoomSpeed   = 1.2f;  // fattore al secondo

    [Header("Controlli")]
    public bool enableMouseControls = false; // false = solo bottoni UI

    // Stato orbita sferica
    private Vector3 _pivot     = Vector3.zero;
    private float   _distance  = 500f;
    private float   _yaw       = 0f;
    private float   _pitch     = 20f;

    // Posizione iniziale per Reset View
    private float _initYaw, _initPitch, _initDist;
    private Vector3 _initPivot;

    // Input mouse
    private Vector2 _lastMousePos;

    // Stato bottoni UI
    private bool _btnRotateLeft, _btnRotateRight, _btnRotateUp, _btnRotateDown;
    private bool _btnZoomIn, _btnZoomOut;

    void Start()
    {
        if (Mouse.current != null)
            _lastMousePos = Mouse.current.position.ReadValue();

        GetComponent<Camera>().farClipPlane  = 100000f;
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
            _pivot = b.center;
            _yaw   = 0f;
            _pitch = 20f;
            Debug.Log($"[CameraController] AutoFrame: pivot={_pivot}, dist={_distance:F1} (mantenuta)");
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
        var keyboard = Keyboard.current;
        bool dirty   = false;

        // ── Controlli Mouse (opzionale) ───────────────────────────────────────
        if (enableMouseControls)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                bool  rightDown  = mouse.rightButton.isPressed;
                bool  middleDown = mouse.middleButton.isPressed;
                float scroll     = mouse.scroll.ReadValue().y;

                Vector2 mousePos = mouse.position.ReadValue();
                Vector2 delta    = mousePos - _lastMousePos;
                _lastMousePos    = mousePos;

                if (rightDown)
                {
                    _yaw   += delta.x * orbitSpeed;
                    _pitch -= delta.y * orbitSpeed;
                    _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
                    dirty = true;
                }

                if (middleDown)
                {
                    Vector3 right = transform.right;
                    Vector3 up    = transform.up;
                    float   scale = _distance * panSpeed * 0.01f;
                    _pivot -= right * delta.x * scale;
                    _pivot -= up    * delta.y * scale;
                    dirty = true;
                }

                if (Mathf.Abs(scroll) > 0.001f)
                {
                    _distance -= scroll * _distance * zoomSpeed * 0.01f;
                    _distance  = Mathf.Max(_distance, 1f);
                    dirty = true;
                }
            }
        }

        // ── Controlli Bottoni UI ──────────────────────────────────────────────
        float dt = Time.deltaTime;

        if (_btnRotateLeft)
        {
            _yaw -= buttonRotateSpeed * dt;
            dirty = true;
        }
        if (_btnRotateRight)
        {
            _yaw += buttonRotateSpeed * dt;
            dirty = true;
        }
        if (_btnRotateUp)
        {
            _pitch += buttonRotateSpeed * dt;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
            dirty = true;
        }
        if (_btnRotateDown)
        {
            _pitch -= buttonRotateSpeed * dt;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
            dirty = true;
        }
        if (_btnZoomIn)
        {
            _distance -= _distance * buttonZoomSpeed * dt;
            _distance  = Mathf.Max(_distance, 1f);
            dirty = true;
        }
        if (_btnZoomOut)
        {
            _distance += _distance * buttonZoomSpeed * dt;
            dirty = true;
        }

        // ── Reset (tasto R) ───────────────────────────────────────────────────
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            ResetView();

        if (dirty)
            ApplyTransform();
    }

    // ── Metodi pubblici per i bottoni UI ──────────────────────────────────────
    public void BeginRotateLeft()  { _btnRotateLeft  = true;  }
    public void EndRotateLeft()    { _btnRotateLeft  = false; }
    public void BeginRotateRight() { _btnRotateRight = true;  }
    public void EndRotateRight()   { _btnRotateRight = false; }
    public void BeginRotateUp()    { _btnRotateUp    = true;  }
    public void EndRotateUp()      { _btnRotateUp    = false; }
    public void BeginRotateDown()  { _btnRotateDown  = true;  }
    public void EndRotateDown()    { _btnRotateDown  = false; }
    public void BeginZoomIn()      { _btnZoomIn      = true;  }
    public void EndZoomIn()        { _btnZoomIn      = false; }
    public void BeginZoomOut()     { _btnZoomOut     = true;  }
    public void EndZoomOut()       { _btnZoomOut     = false; }

    // ── Metodi interni ────────────────────────────────────────────────────────
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
