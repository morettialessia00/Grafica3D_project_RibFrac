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

    [Header("Framing")]
    [Tooltip("Quanto spostare il punto di messa a fuoco verso l'alto (frazione dell'altezza del volume). " +
             "Aumenta per far scendere la CT nel frame. Default 0.15.")]
    public float autoFrameVerticalBias = 0.15f;


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

    // Stato animazione fly-to (centra su frattura al click)
    private bool    _flyActive;
    private Vector3 _flyFromPivot;
    private Vector3 _flyToPivot;
    private float   _flyFromDist;
    private float   _flyToDist;
    private float   _flyT;
    private const float FlyDuration = 0.45f;

    void Start()
    {
        if (Mouse.current != null)
            _lastMousePos = Mouse.current.position.ReadValue();

        Camera cam = GetComponent<Camera>();
        cam.farClipPlane  = 100000f;
        cam.nearClipPlane = 0.1f;

        ApplyTransform();
    }

    /// <summary>
    /// Posiziona la camera per vedere l'intero volume.
    /// Chiamata da CTLoader dopo il caricamento completo.
    /// </summary>
    public void AutoFrame()
    {
        VolumeRenderedObject vol = FindAnyObjectByType<VolumeRenderedObject>(FindObjectsInactive.Include);
        if (vol != null)
        {
            Bounds b = GetVolumeBounds(vol);
            _pivot    = b.center;
            _pivot.y += b.size.y * autoFrameVerticalBias;
            _yaw      = 0f;
            _pitch    = 0f;
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
                    _flyActive = false; // l'utente orbita: interrompi fly-to
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
                    _flyActive = false;
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
            _flyActive = false;
            _distance -= _distance * buttonZoomSpeed * dt;
            _distance  = Mathf.Max(_distance, 1f);
            dirty = true;
        }
        if (_btnZoomOut)
        {
            _flyActive = false;
            _distance += _distance * buttonZoomSpeed * dt;
            dirty = true;
        }

        // ── Reset (tasto R) ───────────────────────────────────────────────────
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            ResetView();

        // ── Animazione fly-to (centra + zoom sulla frattura selezionata) ──────────
        if (_flyActive)
        {
            _flyT += Time.deltaTime / FlyDuration;
            if (_flyT >= 1f) { _flyT = 1f; _flyActive = false; }

            // Smoothstep: accelera in uscita, decelera in arrivo
            float s  = _flyT * _flyT * (3f - 2f * _flyT);
            _pivot    = Vector3.Lerp(_flyFromPivot, _flyToPivot, s);
            _distance = Mathf.Lerp(_flyFromDist,   _flyToDist,   s);
            dirty     = true;
        }

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

    /// <summary>
    /// Anima pivot e distanza verso worldPos/targetDistance in modo fluido (smoothstep 0.45 s).
    /// Utile per centrare e zoomare su una frattura selezionata.
    /// L'animazione si interrompe se l'utente inizia a orbitare o zoomare.
    /// </summary>
    public void FlyToTarget(Vector3 worldPos, float targetDistance)
    {
        _flyFromPivot = _pivot;
        _flyToPivot   = worldPos;
        _flyFromDist  = _distance;
        _flyToDist    = targetDistance;
        _flyT         = 0f;
        _flyActive    = true;
    }

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
