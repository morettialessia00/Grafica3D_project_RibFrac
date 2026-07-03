using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pannello "Fracture Detail" — appare in basso a destra al click su una mesh-frattura.
///
/// Setup in Unity:
///   1. Nella Hierarchy, Right Click → Create Empty. Rinomina "FractureDetailPanel".
///   2. Inspector → Add Component → cerca "FractureDetailPanel".
///   Il pannello grafico viene costruito automaticamente a runtime nel Canvas.
///   Viene trovato automaticamente da FractureSelector tramite FindAnyObjectByType.
/// </summary>
public class FractureDetailPanel : MonoBehaviour
{
    private GameObject      _panel;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _classText;
    private TextMeshProUGUI _confidenceText;
    private GameObject      _probsContainer;
    private TextMeshProUGUI _probsText;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[FractureDetailPanel] Nessuna Canvas trovata in scena.");
            return;
        }

        BuildPanel(canvas.transform);
        _panel.SetActive(false);
    }

    // ── API pubblica ──────────────────────────────────────────────────────────
    /// <summary>
    /// Mostra il pannello con i dettagli della frattura cliccata.
    /// entry può essere null per le fratture non classificate (ambigue o non
    /// classificate dal classificatore selezionato).
    /// classifierIndex indica il classificatore attivo (0..3), -1 se nessuno.
    /// </summary>
    public void Show(int ribId, FractureEntry entry, int classifierIndex)
    {
        if (_panel == null) return;
        _panel.SetActive(true);

        _titleText.text = $"Fracture {ribId}";

        if (entry == null || classifierIndex < 0)
        {
            // Frattura non classificata: nessun dato di confidenza disponibile
            _classText.text = $"<color={FractureClasses.UnclassifiedHex}>Unclassified</color>";
            _confidenceText.gameObject.SetActive(false);
            _probsContainer.SetActive(false);
            return;
        }

        // Colore coerente con FractureLegend
        string hex = FractureClasses.Hex(entry.predicted_class);
        _classText.text = $"<color={hex}><b>{entry.predicted_class}</b></color>";

        _confidenceText.gameObject.SetActive(true);
        _confidenceText.text = $"Confidence: <b>{entry.confidence:P0}</b>";

        // Elenca le probabilità per le classi del classificatore attivo.
        string[] classes = FractureClasses.Classifiers[classifierIndex].classes;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < classes.Length; i++)
        {
            string cls = classes[i];
            float p = FractureClasses.ProbForClass(entry, cls);
            sb.Append($"<color={FractureClasses.Hex(cls)}>{cls}</color>  {p:P0}");
            if (i < classes.Length - 1) sb.Append('\n');
        }

        _probsContainer.SetActive(true);
        _probsText.text = sb.ToString();
    }

    public void Hide()
    {
        if (_panel != null) _panel.SetActive(false);
    }

    // ── Costruisce il pannello proceduralmente ────────────────────────────────
    void BuildPanel(Transform canvasRoot)
    {
        _panel = MakeRect("FractureDetail_Panel", canvasRoot);
        RectTransform rt = _panel.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 0f);
        rt.anchorMax        = new Vector2(1f, 0f);
        rt.pivot            = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-14f, 300f);
        rt.sizeDelta        = new Vector2(480f, 300f);

        Image bg = _panel.AddComponent<Image>();
        bg.color = new Color(0.149f, 0.208f, 0.282f, 0.92f); // pannello #263548

        VerticalLayoutGroup vlg = _panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(28, 32, 18, 24);
        vlg.spacing                = 6f;
        vlg.childAlignment         = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;

        // Titolo: "Fracture N"
        _titleText = AddText(_panel.transform, "—",
                             36f, FontStyles.Bold, new Color(0.816f, 0.910f, 1f, 1f), 420f, 50f); // testo #D0E8FF

        AddSpacer(_panel.transform, 8f);

        // Classe predetta (colorata)
        _classText = AddText(_panel.transform, "—",
                             32f, FontStyles.Normal, new Color(0.816f, 0.910f, 1f, 1f), 420f, 44f); // testo #D0E8FF

        // Confidenza
        _confidenceText = AddText(_panel.transform, "—",
                                  26f, FontStyles.Normal,
                                  new Color(0.420f, 0.670f, 0.820f, 1f), 420f, 36f); // testo secondario #6AAABB

        AddSpacer(_panel.transform, 8f);

        // Probabilità grezze (container separato per poterlo nascondere)
        _probsContainer = MakeRect("ProbsContainer", _panel.transform);
        LayoutElement probsLE = _probsContainer.AddComponent<LayoutElement>();
        probsLE.preferredWidth  = 420f;
        probsLE.preferredHeight = 120f;

        _probsText = _probsContainer.AddComponent<TextMeshProUGUI>();
        _probsText.fontSize      = 24f;
        _probsText.color         = new Color(0.420f, 0.670f, 0.820f, 1f); // testo secondario #6AAABB
        _probsText.lineSpacing   = 6f;
        _probsText.raycastTarget = false;
        _probsText.richText      = true;
    }

    // ── Helpers UI ────────────────────────────────────────────────────────────
    static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static TextMeshProUGUI AddText(Transform parent, string text, float fontSize,
                                   FontStyles style, Color color,
                                   float preferredWidth, float preferredHeight)
    {
        GameObject go = MakeRect("Text", parent);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = fontSize;
        tmp.fontStyle     = style;
        tmp.color         = color;
        tmp.raycastTarget = false;
        tmp.richText      = true;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = preferredWidth;
        le.preferredHeight = preferredHeight;

        return tmp;
    }

    static void AddSpacer(Transform parent, float height)
    {
        GameObject go = MakeRect("Spacer", parent);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
    }
}
