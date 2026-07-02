using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityVolumeRendering;

/// <summary>
/// Visualizzatore slice assiale della CT in un pannello UI 2D.
///
/// Funzionamento:
///   - Graphics.Blit campiona la texture 3D a una posizione k fissa → RenderTexture → RawImage.
///   - Il pannello 2D (top-right) è costruito proceduralmente nel Canvas.
///   - Toggle e Slider risiedono nel pannello Controllers esistente (via Inspector).
///   - Di default il pannello è nascosto; la checkbox lo attiva.
///   - Un LineRenderer indica il bordo del taglio; un Quad semi-trasparente riempie il piano.
///
/// NOTE SUL PIANO E LE VERTEBRE:
///   Il piano 2D mostra SOLO i dati grezzi della texture CT (densità voxel → Transfer Function).
///   Le mesh separate (vertebre, fratture colorate) NON appaiono nella slice 2D perché
///   non fanno parte della texture 3D. Le vertebre sono comunque visibili come strutture
///   bianche nella CT (alta densità ossea), ma senza colorazione.
///   Per includere mesh nella slice servirebbero una camera secondaria + RenderTexture separata.
/// </summary>
public class AxialSliceController : MonoBehaviour
{
    [Header("Controlli — nel pannello Controllers")]
    [Tooltip("La Toggle (checkbox) nel pannello Controllers")]
    public Toggle sliceToggle;

    [Tooltip("Lo Slider nel pannello Controllers")]
    public Slider sliceSlider;

    [Tooltip("Slider per l'inclinazione del piano (−45° / +45°). Aggiungilo nel pannello Controllers.")]
    public Slider tiltSlider;

    [Header("Shader")]
    [Tooltip("Trascina qui Assets/Shaders/AxialSliceBlit.shader dalla finestra Project")]
    public Shader sliceBlitShader;

    [Header("Qualità render")]
    [Tooltip("Risoluzione della texture 2D quadrata. 512 = buona qualità.")]
    public int textureSize = 512;

    // ── Pannello 2D (costruito proceduralmente) ───────────────────────────────
    private GameObject _displayPanel;
    private RawImage   _rawImage;

    // ── Pannello espanso (overlay) ────────────────────────────────────────────
    private GameObject _expandedPanel;

    // ── Indicatore piano 3D — bordo LineRenderer ──────────────────────────────
    private GameObject   _indicatorGO;
    private LineRenderer _indicatorLR;

    // ── Indicatore piano 3D — riempimento semi-trasparente ────────────────────
    private GameObject _indicatorFillGO;
    private Material   _indicatorFillMat;

    private Bounds _volBounds;

    // ── Rendering ─────────────────────────────────────────────────────────────
    private Material      _sliceMat;
    private RenderTexture _renderTex;

    // ── Stato ─────────────────────────────────────────────────────────────────
    private VolumeRenderedObject _volObj;
    private bool _hasData;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        // RenderTexture
        _renderTex = new RenderTexture(textureSize, textureSize, 0,
                                       RenderTextureFormat.ARGB32);
        _renderTex.Create();

        // Materiale Blit
        if (sliceBlitShader != null)
            _sliceMat = new Material(sliceBlitShader);
        else
            Debug.LogError("[AxialSliceController] sliceBlitShader non assegnato nell'Inspector.");

        // Pannello 2D nel Canvas
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null) BuildDisplayPanel(canvas.transform);
        else Debug.LogError("[AxialSliceController] Nessuna Canvas trovata in scena.");

        // Indicatori 3D
        CreateIndicatorLine();
        CreateIndicatorFill();

        // Toggle
        if (sliceToggle != null)
        {
            sliceToggle.SetIsOnWithoutNotify(false);
            sliceToggle.interactable = false;
            sliceToggle.onValueChanged.AddListener(OnToggleChanged);
            ResizeToggleLabel(sliceToggle, 26f);
        }
        else Debug.LogWarning("[AxialSliceController] sliceToggle non assegnata nell'Inspector.");

        // Slider posizione
        if (sliceSlider != null)
        {
            sliceSlider.minValue = 0f;
            sliceSlider.maxValue = 1f;
            sliceSlider.SetValueWithoutNotify(0.5f);
            sliceSlider.interactable = false;
            sliceSlider.onValueChanged.AddListener(OnSliderChanged);
            WrapSliderWithLabel(sliceSlider, "Position");
        }
        else Debug.LogWarning("[AxialSliceController] sliceSlider non assegnato nell'Inspector.");

        // Slider inclinazione
        if (tiltSlider != null)
        {
            tiltSlider.minValue = -90f;
            tiltSlider.maxValue =   0f;
            tiltSlider.SetValueWithoutNotify(0f);
            tiltSlider.interactable = false;
            tiltSlider.onValueChanged.AddListener(OnTiltChanged);
            WrapSliderWithLabel(tiltSlider, "Tilt");
        }
        else Debug.LogWarning("[AxialSliceController] tiltSlider non assegnato nell'Inspector.");

        // StyleSlider applicato dopo il layout (coroutine)
        StartCoroutine(ApplySliderStyles());

        SetDisplayVisible(false);
        SetIndicatorVisible(false);
    }

    // ── Grayscale window (impostata da CTLoader dopo il calcolo della TF) ──────
    private float _winLo = 0.25f;
    private float _winHi = 0.65f;

    /// <summary>
    /// Imposta la finestra di visualizzazione grayscale in spazio normalizzato [0,1].
    /// Chiamare da CTLoader dopo aver calcolato nFat e nCortex.
    /// </summary>
    public void SetGrayscaleWindow(float lo, float hi)
    {
        _winLo = lo;
        _winHi = hi;
        if (_sliceMat != null)
        {
            _sliceMat.SetFloat("_UseGrayscale", 1.0f);
            _sliceMat.SetFloat("_WinLo", _winLo);
            _sliceMat.SetFloat("_WinHi", _winHi);
        }
    }

    // ── API pubblica (chiamata da CTLoader) ───────────────────────────────────

    /// <summary>Chiamato da CTLoader subito dopo il caricamento del volume CT.</summary>
    public void OnPatientLoaded(VolumeRenderedObject volObj)
    {
        _volObj  = volObj;
        _hasData = false;

        if (_sliceMat == null || _volObj == null) return;

        // Calcola bounds del volume in world space
        _volBounds = _volObj.meshRenderer.bounds;

        // Texture 3D dei voxel
        Texture dataTex = _volObj.meshRenderer.sharedMaterial.GetTexture("_DataTex");

        // Transfer Function texture
        Texture tfTex = _volObj.transferFunction != null
                        ? (Texture)_volObj.transferFunction.GetTexture()
                        : _volObj.meshRenderer.sharedMaterial.GetTexture("_TFTex");

        if (dataTex == null)
        {
            Debug.LogWarning("[AxialSliceController] _DataTex non trovata nel materiale volume.");
            return;
        }

        _sliceMat.SetTexture("_DataTex", dataTex);
        if (tfTex != null) _sliceMat.SetTexture("_TFTex", tfTex);

        // Configura il piano di riempimento con le dimensioni reali del volume
        if (_indicatorFillGO != null)
        {
            // Ruota il Quad (che di default è nel piano XY) nel piano XZ (orizzontale)
            _indicatorFillGO.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
            // Scala per coprire l'intera estensione X e Z del volume
            _indicatorFillGO.transform.localScale = new Vector3(_volBounds.size.x, _volBounds.size.z, 1f);
            // Centra sul volume (Y viene aggiornata da UpdateIndicatorPosition)
            Vector3 c = _volBounds.center;
            _indicatorFillGO.transform.position = new Vector3(c.x, c.y, c.z);
        }

        _hasData = true;

        if (sliceToggle != null) sliceToggle.interactable = true;
        // Gli slider sono manipolabili SOLO quando il toggle "Axial Section" è attivo
        SetSlidersInteractable(sliceToggle != null && sliceToggle.isOn);

        if (sliceToggle != null && sliceToggle.isOn)
        {
            UpdateSlice();
            UpdateIndicatorPosition();
        }
    }

    /// <summary>Chiamato da CTLoader al reset del paziente.</summary>
    public void OnPatientReset()
    {
        _volObj  = null;
        _hasData = false;

        if (sliceToggle != null) { sliceToggle.SetIsOnWithoutNotify(false); sliceToggle.interactable = false; }
        if (sliceSlider != null) { sliceSlider.SetValueWithoutNotify(0.5f); sliceSlider.interactable = false; }
        if (tiltSlider  != null) { tiltSlider.SetValueWithoutNotify(0f);    tiltSlider.interactable  = false; }

        SetDisplayVisible(false);
        SetIndicatorVisible(false);
    }

    // ── Callbacks UI ─────────────────────────────────────────────────────────

    void OnToggleChanged(bool isOn)
    {
        // Abilita gli slider solo quando la sezione assiale è attiva (e c'è un volume caricato)
        SetSlidersInteractable(isOn && _hasData);
        SetDisplayVisible(isOn);
        SetIndicatorVisible(isOn && _hasData);
        if (isOn && _hasData)
        {
            UpdateSlice();
            UpdateIndicatorPosition();
        }
    }

    /// <summary>Abilita/disabilita gli slider Position e Tilt in blocco.</summary>
    void SetSlidersInteractable(bool value)
    {
        if (sliceSlider != null) sliceSlider.interactable = value;
        if (tiltSlider  != null) tiltSlider.interactable  = value;
    }

    void OnSliderChanged(float value)
    {
        bool panelVisible = _displayPanel != null && _displayPanel.activeSelf;
        if (!_hasData || !panelVisible) return;
        UpdateSlice();
        UpdateIndicatorPosition();
    }

    void OnTiltChanged(float value)
    {
        bool panelVisible = _displayPanel != null && _displayPanel.activeSelf;
        if (!_hasData || !panelVisible) return;
        UpdateSlice();
        UpdateIndicatorPosition();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    void UpdateSlice()
    {
        if (_sliceMat == null || !_hasData) return;
        float sliceK   = sliceSlider != null ? sliceSlider.value : 0.5f;
        float tiltDeg  = tiltSlider  != null ? tiltSlider.value  : 0f;
        _sliceMat.SetFloat("_SliceY",    sliceK);
        _sliceMat.SetFloat("_TiltAngle", tiltDeg * Mathf.Deg2Rad);
        Graphics.Blit(null, _renderTex, _sliceMat);
    }

    // ── Indicatore piano 3D ───────────────────────────────────────────────────

    /// <summary>Crea il LineRenderer (bordo cyan) del piano.</summary>
    void CreateIndicatorLine()
    {
        _indicatorGO = new GameObject("AxialSlice_Indicator");
        _indicatorLR = _indicatorGO.AddComponent<LineRenderer>();

        _indicatorLR.loop           = true;
        _indicatorLR.positionCount  = 4;
        _indicatorLR.startWidth     = 3f;
        _indicatorLR.endWidth       = 3f;
        _indicatorLR.useWorldSpace  = true;

        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.310f, 0.765f, 0.973f, 1f); // #4FC3F7 slate-blue accent
        _indicatorLR.material          = mat;
        _indicatorLR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _indicatorLR.receiveShadows    = false;
    }

    /// <summary>Crea il Quad semi-trasparente che riempie il piano di taglio.</summary>
    void CreateIndicatorFill()
    {
        _indicatorFillGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _indicatorFillGO.name = "AxialSlice_Fill";

        // Rimuovi il collider (non serve per una visualizzazione)
        Collider col = _indicatorFillGO.GetComponent<Collider>();
        if (col != null) Destroy(col);

        MeshRenderer mr = _indicatorFillGO.GetComponent<MeshRenderer>();
        _indicatorFillMat       = new Material(Shader.Find("Sprites/Default"));
        _indicatorFillMat.color = new Color(0.310f, 0.765f, 0.973f, 0.20f); // #4FC3F7 semi-trasparente
        mr.material             = _indicatorFillMat;
        mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = false;

        _indicatorFillGO.SetActive(false);
    }

    void UpdateIndicatorPosition()
    {
        if (_indicatorLR == null || !_hasData) return;

        // Y del centro del piano (inversione per Y flippato nel volume)
        // Se la linea non corrisponde alla slice, inverti: cambia (1f - sliceSlider.value) in sliceSlider.value
        float t      = 1f - (sliceSlider != null ? sliceSlider.value : 0.5f);
        float worldY = Mathf.Lerp(_volBounds.min.y, _volBounds.max.y, t);

        // Angolo di inclinazione (rotazione attorno all'asse X = sinistra-destra paziente)
        float tiltDeg = tiltSlider != null ? tiltSlider.value : 0f;
        float tiltRad = tiltDeg * Mathf.Deg2Rad;
        float cosT    = Mathf.Cos(tiltRad);
        float sinT    = Mathf.Sin(tiltRad);

        // Centro del piano in world space
        float cx = _volBounds.center.x;
        float cz = _volBounds.center.z;

        // Per ciascun angolo del rettangolo, ruotiamo il vettore (0, dz) attorno all'asse X.
        // Rotazione attorno a X: y_new = -dz*sinT, z_new = dz*cosT
        float dzMin = _volBounds.min.z - cz;
        float dzMax = _volBounds.max.z - cz;

        _indicatorLR.SetPosition(0, new Vector3(_volBounds.min.x, worldY - dzMin * sinT, cz + dzMin * cosT));
        _indicatorLR.SetPosition(1, new Vector3(_volBounds.max.x, worldY - dzMin * sinT, cz + dzMin * cosT));
        _indicatorLR.SetPosition(2, new Vector3(_volBounds.max.x, worldY - dzMax * sinT, cz + dzMax * cosT));
        _indicatorLR.SetPosition(3, new Vector3(_volBounds.min.x, worldY - dzMax * sinT, cz + dzMax * cosT));

        // Piano riempimento: aggiorna posizione e rotazione
        if (_indicatorFillGO != null)
        {
            _indicatorFillGO.transform.position = new Vector3(cx, worldY, cz);
            // Euler(90, 0, 0) = piano orizzontale; aggiungiamo il tilt attorno a X
            // Se il quad risulta inclinato dalla parte opposta al bordo, inverti il segno di tiltDeg
            _indicatorFillGO.transform.rotation = Quaternion.Euler(90f + tiltDeg, 0f, 0f);
        }
    }

    void SetIndicatorVisible(bool visible)
    {
        if (_indicatorGO     != null) _indicatorGO.SetActive(visible);
        if (_indicatorFillGO != null) _indicatorFillGO.SetActive(visible);
    }

    // ── Helpers UI ────────────────────────────────────────────────────────────

    void SetDisplayVisible(bool visible)
    {
        if (_displayPanel  != null) _displayPanel.SetActive(visible);
        // Nasconde l'overlay se si spegne il pannello principale
        if (!visible && _expandedPanel != null) _expandedPanel.SetActive(false);
    }

    void ToggleExpanded()
    {
        if (_expandedPanel == null) return;
        bool show = !_expandedPanel.activeSelf;
        _expandedPanel.SetActive(show);
        // Porta l'overlay in primo piano nel Canvas
        if (show) _expandedPanel.transform.SetAsLastSibling();
    }

    /// <summary>Ridimensiona l'etichetta del Toggle (supporta sia TMP che legacy Text).</summary>
    static void ResizeToggleLabel(Toggle toggle, float fontSize)
    {
        // TextMeshPro (caso comune in Unity 6)
        TextMeshProUGUI tmp = toggle.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) { tmp.fontSize = fontSize; return; }

        // Legacy Text (fallback)
        Text legacyText = toggle.GetComponentInChildren<Text>();
        if (legacyText != null) legacyText.fontSize = Mathf.RoundToInt(fontSize);
    }

    // ── Costruisce il pannello 2D ─────────────────────────────────────────────
    //
    //  Usa RectTransform espliciti invece di VerticalLayoutGroup per evitare
    //  il bug in cui childControlHeight=false ignora LayoutElement.preferredHeight.
    //
    //  ┌──────────────────────────┐  ← top-right, 14px dal bordo, Y=-270
    //  │    Sezione Assiale CT    │  ← titolo 24pt bold
    //  │  ┌────────────────────┐  │
    //  │  │   RawImage 450px   │  │
    //  │  └────────────────────┘  │
    //  └──────────────────────────┘
    //
    void BuildDisplayPanel(Transform canvasRoot)
    {
        const float IMG_SIZE = 450f;
        const float PAD      = 12f;
        const float TITLE_H  = 42f;
        const float SPACING  = 8f;
        const float PANEL_W  = IMG_SIZE + PAD * 2f;
        const float PANEL_H  = PAD + TITLE_H + SPACING + IMG_SIZE + PAD;

        // --- Pannello contenitore ---
        _displayPanel = new GameObject("AxialSlice_Panel", typeof(RectTransform));
        _displayPanel.transform.SetParent(canvasRoot, false);

        RectTransform panelRT = _displayPanel.GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(1f, 1f);
        panelRT.anchorMax        = new Vector2(1f, 1f);
        panelRT.pivot            = new Vector2(1f, 1f);
        panelRT.anchoredPosition = new Vector2(-14f, -220f);
        panelRT.sizeDelta        = new Vector2(PANEL_W, PANEL_H);

        Image bg = _displayPanel.AddComponent<Image>();
        bg.color = new Color(0.149f, 0.208f, 0.282f, 0.92f); // #263548 slate-blue panel

        // --- Titolo (ancorato in alto, larghezza piena) ---
        GameObject titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(_displayPanel.transform, false);

        RectTransform titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin        = new Vector2(0f, 1f);
        titleRT.anchorMax        = new Vector2(1f, 1f);
        titleRT.pivot            = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -PAD);
        titleRT.sizeDelta        = new Vector2(0f, TITLE_H); // width=0 → stretched dagli anchor

        TextMeshProUGUI title = titleGO.AddComponent<TextMeshProUGUI>();
        title.text          = "Axial CT Slice";
        title.fontSize      = 24f;
        title.fontStyle     = FontStyles.Bold;
        title.color         = new Color(0.816f, 0.910f, 1f, 1f); // #D0E8FF
        title.alignment     = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        // --- Immagine CT (quadrata, dimensione esplicita) ---
        GameObject imgGO = new GameObject("SliceImage", typeof(RectTransform));
        imgGO.transform.SetParent(_displayPanel.transform, false);

        RectTransform imgRT = imgGO.GetComponent<RectTransform>();
        // Ancoriamo al centro-top del pannello e usiamo sizeDelta esplicito
        imgRT.anchorMin        = new Vector2(0.5f, 1f);
        imgRT.anchorMax        = new Vector2(0.5f, 1f);
        imgRT.pivot            = new Vector2(0.5f, 1f);
        imgRT.anchoredPosition = new Vector2(0f, -(PAD + TITLE_H + SPACING));
        imgRT.sizeDelta        = new Vector2(IMG_SIZE, IMG_SIZE); // quadrato esplicito

        _rawImage               = imgGO.AddComponent<RawImage>();
        _rawImage.texture       = _renderTex;
        _rawImage.color         = Color.white;
        _rawImage.raycastTarget = true;   // deve ricevere click

        // Rende l'immagine cliccabile (apre l'overlay espanso)
        Button expandBtn = imgGO.AddComponent<Button>();
        ColorBlock cb = expandBtn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(0.78f, 0.90f, 1f, 1f);
        cb.pressedColor     = new Color(0.68f, 0.82f, 1f, 1f);
        expandBtn.colors    = cb;
        expandBtn.onClick.AddListener(ToggleExpanded);

        // Piccola scritta-hint sotto il pannello
        GameObject hintGO = new GameObject("HintText", typeof(RectTransform));
        hintGO.transform.SetParent(_displayPanel.transform, false);
        RectTransform hintRT = hintGO.GetComponent<RectTransform>();
        hintRT.anchorMin        = new Vector2(0f, 0f);
        hintRT.anchorMax        = new Vector2(1f, 0f);
        hintRT.pivot            = new Vector2(0.5f, 0f);
        hintRT.anchoredPosition = new Vector2(0f, 4f);
        hintRT.sizeDelta        = new Vector2(0f, 22f);
        TextMeshProUGUI hint = hintGO.AddComponent<TextMeshProUGUI>();
        hint.text          = "click to expand";
        hint.fontSize      = 14f;
        hint.fontStyle     = FontStyles.Italic;
        hint.color         = new Color(0.50f, 0.67f, 0.80f, 0.7f);
        hint.alignment     = TextAlignmentOptions.Center;
        hint.raycastTarget = false;

        // Costruisce il pannello overlay espanso
        BuildExpandedPanel(canvasRoot);
    }

    // ── Pannello overlay espanso ──────────────────────────────────────────────
    //
    //  Occupa i 2/3 destri dello schermo, centrato verticalmente.
    //  Lascia libero il lato sinistro dove risiedono gli slider del Controller.
    //
    //  ┌─────────────────────────────────────────┐
    //  │   [controller]   │  [overlay espanso]   │
    //  │   [  sliders  ]  │  ┌───────────────┐   │
    //  │                  │  │  RawImage CT  │   │
    //  │                  │  └───────────────┘   │
    //  └─────────────────────────────────────────┘
    //
    void BuildExpandedPanel(Transform canvasRoot)
    {
        _expandedPanel = new GameObject("AxialSlice_Expanded", typeof(RectTransform));
        _expandedPanel.transform.SetParent(canvasRoot, false);

        // Posizione fissa in pixel: 460 sx, 30 dx, 30 basso, 230 alto
        RectTransform panelRT = _expandedPanel.GetComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = new Vector2(460f,  30f);   // left, bottom
        panelRT.offsetMax = new Vector2(-30f, -230f);  // -right, -top

        Image bg = _expandedPanel.AddComponent<Image>();
        bg.color = new Color(0.118f, 0.165f, 0.227f, 0.97f); // #1E2A3A quasi opaco

        // ── Titolo ──
        GameObject titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(_expandedPanel.transform, false);
        RectTransform titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin        = new Vector2(0f, 1f);
        titleRT.anchorMax        = new Vector2(1f, 1f);
        titleRT.pivot            = new Vector2(0.5f, 1f);
        titleRT.anchoredPosition = new Vector2(0f, -14f);
        titleRT.sizeDelta        = new Vector2(-60f, 48f); // -60 per non sovrapporsi al close btn
        TextMeshProUGUI title = titleGO.AddComponent<TextMeshProUGUI>();
        title.text          = "Axial CT Slice";
        title.fontSize      = 30f;
        title.fontStyle     = FontStyles.Bold;
        title.color         = new Color(0.816f, 0.910f, 1f, 1f); // #D0E8FF
        title.alignment     = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        // ── Bottone chiudi (×) in alto a destra ──
        GameObject closeGO = new GameObject("CloseBtn", typeof(RectTransform));
        closeGO.transform.SetParent(_expandedPanel.transform, false);
        RectTransform closeRT = closeGO.GetComponent<RectTransform>();
        closeRT.anchorMin        = new Vector2(1f, 1f);
        closeRT.anchorMax        = new Vector2(1f, 1f);
        closeRT.pivot            = new Vector2(1f, 1f);
        closeRT.anchoredPosition = new Vector2(-10f, -10f);
        closeRT.sizeDelta        = new Vector2(44f, 44f);
        Image closeBg = closeGO.AddComponent<Image>();
        closeBg.color = new Color(0.72f, 0.25f, 0.25f, 0.90f);
        Button closeBtn = closeGO.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        ColorBlock ccb = closeBtn.colors;
        ccb.normalColor      = new Color(0.72f, 0.25f, 0.25f, 0.90f);
        ccb.highlightedColor = new Color(0.88f, 0.35f, 0.35f, 1f);
        ccb.pressedColor     = new Color(0.55f, 0.15f, 0.15f, 1f);
        closeBtn.colors      = ccb;
        closeBtn.onClick.AddListener(() => _expandedPanel.SetActive(false));

        GameObject closeLabelGO = new GameObject("X", typeof(RectTransform));
        closeLabelGO.transform.SetParent(closeGO.transform, false);
        RectTransform closeLabelRT = closeLabelGO.GetComponent<RectTransform>();
        closeLabelRT.anchorMin = Vector2.zero;
        closeLabelRT.anchorMax = Vector2.one;
        closeLabelRT.offsetMin = Vector2.zero;
        closeLabelRT.offsetMax = Vector2.zero;
        TextMeshProUGUI closeLabel = closeLabelGO.AddComponent<TextMeshProUGUI>();
        closeLabel.text          = "X";
        closeLabel.fontSize      = 26f;
        closeLabel.fontStyle     = FontStyles.Bold;
        closeLabel.color         = Color.white;
        closeLabel.alignment     = TextAlignmentOptions.Center;
        closeLabel.raycastTarget = false;

        // ── Container: definisce lo spazio disponibile per l'immagine ──
        // Stretched con margini: lascia 76px in alto (titolo) e 16px sugli altri lati.
        GameObject containerGO = new GameObject("ImageContainer", typeof(RectTransform));
        containerGO.transform.SetParent(_expandedPanel.transform, false);
        RectTransform containerRT = containerGO.GetComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = new Vector2(16f,  16f);
        containerRT.offsetMax = new Vector2(-16f, -76f);

        // ── Immagine grande: centrata nel container, proporzioni 1:1 (texture quadrata) ──
        // AspectRatioFitter.FitInParent ridimensiona l'elemento per stare nel parent
        // mantenendo il rapporto, senza deformare. Pivot e anchor al centro → centrata.
        GameObject imgGO = new GameObject("ExpandedImage", typeof(RectTransform));
        imgGO.transform.SetParent(containerGO.transform, false);
        RectTransform imgRT = imgGO.GetComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0.5f, 0.5f);
        imgRT.anchorMax = new Vector2(0.5f, 0.5f);
        imgRT.pivot     = new Vector2(0.5f, 0.5f);
        imgRT.sizeDelta = Vector2.zero; // sarà impostato da AspectRatioFitter

        RawImage expandedImg = imgGO.AddComponent<RawImage>();
        expandedImg.texture       = _renderTex;
        expandedImg.color         = Color.white;
        expandedImg.raycastTarget = false;

        AspectRatioFitter arf = imgGO.AddComponent<AspectRatioFitter>();
        arf.aspectMode  = AspectRatioFitter.AspectMode.FitInParent;
        arf.aspectRatio = 1.0f; // RenderTexture quadrata (textureSize × textureSize)

        _expandedPanel.SetActive(false);
    }

    // ── Label slider ──────────────────────────────────────────────────────────

    /// <summary>
    /// Inserisce un wrapper con HorizontalLayoutGroup attorno allo slider,
    /// aggiungendo un'etichetta testuale alla sua sinistra.
    /// Funziona sia dentro un VerticalLayoutGroup che senza layout sul parent.
    /// </summary>
    static void WrapSliderWithLabel(Slider slider, string labelText,
                                    float labelWidth = 100f, float fontSize = 22f,
                                    float rowHeight = 32f)
    {
        if (slider == null) return;

        Transform originalParent = slider.transform.parent;
        int       siblingIndex   = slider.transform.GetSiblingIndex();

        // ── Wrapper (prende il posto dello slider nella gerarchia padre) ──────
        GameObject wrapper = new GameObject("Row_" + labelText, typeof(RectTransform));
        wrapper.transform.SetParent(originalParent, false);
        wrapper.transform.SetSiblingIndex(siblingIndex);

        // Eredita le impostazioni di layout già presenti sullo slider (se ci sono)
        LayoutElement srcLE  = slider.GetComponent<LayoutElement>();
        LayoutElement wrapLE = wrapper.AddComponent<LayoutElement>();
        if (srcLE != null)
        {
            wrapLE.preferredHeight = srcLE.preferredHeight;
            wrapLE.preferredWidth  = srcLE.preferredWidth;
            wrapLE.flexibleWidth   = srcLE.flexibleWidth;
            wrapLE.flexibleHeight  = srcLE.flexibleHeight;
            wrapLE.minHeight       = srcLE.minHeight;
        }
        else
        {
            // Legge l'altezza impostata nell'editor per preservare il layout del pannello
            RectTransform sliderRT = slider.GetComponent<RectTransform>();
            float originalH = (sliderRT != null && sliderRT.sizeDelta.y > 0f)
                              ? sliderRT.sizeDelta.y
                              : 36f;
            wrapLE.preferredHeight = originalH;
            wrapLE.flexibleWidth   = 1f;
        }

        // ── Altezza fissa della riga ──────────────────────────────────────────
        // Fissa l'altezza a rowHeight a prescindere dalle impostazioni del
        // ControlPanel (Child Control Height OFF / Force Expand Height OFF):
        // impostiamo sia il LayoutElement che il RectTransform così la riga
        // non viene più "gonfiata" dallo spazio libero del pannello.
        wrapLE.minHeight       = rowHeight;
        wrapLE.preferredHeight = rowHeight;
        wrapLE.flexibleHeight  = 0f;

        RectTransform wrapperRT = wrapper.GetComponent<RectTransform>();
        wrapperRT.sizeDelta = new Vector2(wrapperRT.sizeDelta.x, rowHeight);

        HorizontalLayoutGroup hlg = wrapper.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        hlg.spacing                = 6f;

        // ── Label a sinistra ──────────────────────────────────────────────────
        GameObject labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(wrapper.transform, false);

        TextMeshProUGUI tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text          = labelText;
        tmp.fontSize      = fontSize;
        tmp.color         = Color.white;
        tmp.alignment     = TextAlignmentOptions.MidlineRight;
        tmp.raycastTarget = false;

        LayoutElement labelLE = labelGO.AddComponent<LayoutElement>();
        labelLE.preferredWidth = labelWidth;
        labelLE.flexibleWidth  = 0f; // larghezza fissa → non si espande

        // ── Slider a destra (sposta dentro il wrapper) ────────────────────────
        slider.transform.SetParent(wrapper.transform, false);

        LayoutElement sliderLE = slider.GetComponent<LayoutElement>();
        if (sliderLE == null) sliderLE = slider.gameObject.AddComponent<LayoutElement>();
        sliderLE.flexibleWidth  = 1f;  // prende tutto lo spazio rimanente
        sliderLE.flexibleHeight = 1f;
    }

    // ── Coroutine: applica stile slider dopo il layout ────────────────────────

    System.Collections.IEnumerator ApplySliderStyles()
    {
        // Aspetta che Unity finisca di calcolare tutti i layout del primo frame
        yield return new WaitForEndOfFrame();
        if (sliceSlider != null) StyleSlider(sliceSlider);
        if (tiltSlider  != null) StyleSlider(tiltSlider);

    }

    // ── Stile visivo slider ───────────────────────────────────────────────────

    /// <summary>
    /// Rende il track sottile e il handle più grande e maneggevole.
    /// Funziona sul prefab default Unity (figli: Background, Fill Area, Handle Slide Area/Handle).
    /// </summary>
    static void StyleSlider(Slider slider, float trackH = 6f, float handleW = 16f, float handleH = 28f)
    {
        if (slider == null) return;

        // Background (la barra del track)
        Transform bg = slider.transform.Find("Background");
        if (bg != null)
        {
            RectTransform rt = bg.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(rt.anchorMin.x, 0.5f);
                rt.anchorMax = new Vector2(rt.anchorMax.x, 0.5f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, trackH);
            }
        }

        // Fill Area (la parte colorata che avanza con il valore)
        Transform fillArea = slider.transform.Find("Fill Area");
        if (fillArea != null)
        {
            RectTransform rt = fillArea.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(rt.anchorMin.x, 0.5f);
                rt.anchorMax = new Vector2(rt.anchorMax.x, 0.5f);
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, trackH);
            }
        }

        // Handle: forza anchor verticali a centro + dimensione esplicita
        Transform handleArea = slider.transform.Find("Handle Slide Area");
        if (handleArea != null)
        {
            Transform handle = handleArea.Find("Handle");
            if (handle != null)
            {
                RectTransform rt = handle.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(rt.anchorMin.x, 0.5f);
                    rt.anchorMax = new Vector2(rt.anchorMax.x, 0.5f);
                    rt.sizeDelta = new Vector2(handleW, handleH);
                }
            }
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (sliceToggle      != null) sliceToggle.onValueChanged.RemoveListener(OnToggleChanged);
        if (sliceSlider      != null) sliceSlider.onValueChanged.RemoveListener(OnSliderChanged);
        if (tiltSlider       != null) tiltSlider.onValueChanged.RemoveListener(OnTiltChanged);
        if (_renderTex       != null) { _renderTex.Release(); Destroy(_renderTex); }
        if (_sliceMat        != null) Destroy(_sliceMat);
        if (_indicatorFillMat != null) Destroy(_indicatorFillMat);
    }
}
