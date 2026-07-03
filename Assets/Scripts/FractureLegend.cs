using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Legenda colori fratture — pannello in alto a destra nel Canvas.
///
/// Modo 0 (No lesions):       pannello nascosto.
/// Modo 1 (All lesions):      mostra "Fracture" in rosso.
/// Modi 2-5 (Classificatori): mostra le classi del classificatore selezionato
///                            (colori da FractureClasses) + Unclassified.
///
/// Setup in Unity:
///   1. Nella Hierarchy, clicca col destro su un punto vuoto → Create Empty.
///      Rinomina il nuovo GameObject "FractureLegend".
///   2. Con "FractureLegend" selezionato, nell'Inspector clicca Add Component
///      e cerca "FractureLegend".
///   3. Il campo "Ct Loader" si compila da solo se c'è un CTLoader in scena;
///      in caso contrario trascinaci il GameObject CTLoader.
///   Il pannello grafico viene creato automaticamente a runtime nel Canvas.
/// </summary>
public class FractureLegend : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Lascia vuoto: trovato automaticamente se c'è un CTLoader in scena")]
    public CTLoader ctLoader;

    private GameObject      _panel;
    private TextMeshProUGUI _content;   // blocco rich-text unico, aggiornato per modo
    private int             _lastMode = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        if (ctLoader == null)
            ctLoader = FindAnyObjectByType<CTLoader>();

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[FractureLegend] Nessuna Canvas trovata in scena.");
            return;
        }

        BuildPanel(canvas.transform);
        Refresh(ctLoader != null ? ctLoader.CurrentColorMode : 0);
    }

    void Update()
    {
        if (ctLoader == null || _panel == null) return;
        int mode = ctLoader.CurrentColorMode;
        if (mode == _lastMode) return;
        Refresh(mode);
    }

    // ── Costruisce il pannello proceduralmente ────────────────────────────────
    void BuildPanel(Transform canvasRoot)
    {
        _panel = MakeRect("FractureLegend_Panel", canvasRoot);
        RectTransform rt = _panel.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-14f, 14f);
        rt.sizeDelta        = new Vector2(334f, 250f);

        Image bg = _panel.AddComponent<Image>();
        bg.color = new Color(0.149f, 0.208f, 0.282f, 0.92f); // pannello #263548

        VerticalLayoutGroup vlg = _panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(20, 24, 14, 20);
        vlg.spacing                = 2f;
        vlg.childAlignment         = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;

        // Titolo
        _panel.transform.SetParent(canvasRoot, false); // assicura ordine corretto
        AddText(_panel.transform, "Legend", 30f, FontStyles.Bold, new Color(0.816f, 0.910f, 1f, 1f), // #D0E8FF
                preferredWidth: 300f, preferredHeight: 42f);

        AddSpacer(_panel.transform, 6f);

        // Blocco rich-text unico: il testo viene rigenerato in Refresh in base al modo.
        // Uso ■ (U+25A0) colorato inline — perfettamente allineato col testo.
        GameObject block = AddRichBlock(_panel.transform, "",
            fontSize: 28f, preferredWidth: 300f, preferredHeight: 200f);
        _content = block.GetComponent<TextMeshProUGUI>();
    }

    // ── Contenuto per modo ─────────────────────────────────────────────────────
    void Refresh(int mode)
    {
        _lastMode = mode;
        if (_panel == null) return;

        _panel.SetActive(mode > 0);
        if (mode <= 0 || _content == null) return;

        if (mode == FractureClasses.ModeAll)
        {
            _content.text = "<color=#FF2626>■</color>  Fracture";
            return;
        }

        int k = FractureClasses.ClassifierIndex(mode);
        if (k < 0) { _content.text = ""; return; }

        var sb = new System.Text.StringBuilder();
        foreach (string cls in FractureClasses.Classifiers[k].classes)
            sb.Append($"<color={FractureClasses.Hex(cls)}>■</color>  {cls}\n");
        sb.Append($"<color={FractureClasses.UnclassifiedHex}>■</color>  Unclassified");
        _content.text = sb.ToString();
    }

    // ── Helpers UI ────────────────────────────────────────────────────────────
    static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void AddText(Transform parent, string text, float size, FontStyles style,
                        Color color, float preferredWidth, float preferredHeight)
    {
        GameObject go = MakeRect("Text_" + text, parent);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = size;
        tmp.fontStyle     = style;
        tmp.color         = color;
        tmp.raycastTarget = false;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;
    }

    static void AddSpacer(Transform parent, float height)
    {
        GameObject go = MakeRect("Spacer", parent);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
    }

    // Blocco TMP con rich text (■ colorati inline = allineamento perfetto)
    static GameObject AddRichBlock(Transform parent, string richText,
                                   float fontSize, float preferredWidth, float preferredHeight)
    {
        GameObject go = MakeRect("RichBlock", parent);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = richText;
        tmp.fontSize      = fontSize;
        tmp.color         = new Color(0.816f, 0.910f, 1f, 1f); // #D0E8FF
        tmp.lineSpacing   = 8f;
        tmp.raycastTarget = false;
        tmp.richText      = true;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;

        return go;
    }
}
