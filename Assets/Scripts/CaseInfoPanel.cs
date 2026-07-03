using UnityEngine;
using TMPro;

/// <summary>
/// Pannello "Case INFO" — mostra public_id e conteggi fratture per classe.
///
/// Setup in Unity (passo 3):
///   1. Crea un GameObject "CaseInfoPanel" figlio del Canvas.
///   2. Aggiungi testi TMP figli (es. "PatientIdText", "FractureCountText", "BreakdownText").
///   3. Aggiungi questo script a "CaseInfoPanel".
///   4. Nel Inspector, trascina i tre TextMeshProUGUI nei rispettivi campi.
///   5. Nel CTLoader Inspector, trascina "CaseInfoPanel" nel campo "Case Info Panel".
/// </summary>
public class CaseInfoPanel : MonoBehaviour
{
    [Header("Campi testo (TextMeshProUGUI)")]
    [Tooltip("Mostra il public_id del paziente, es. 'RibFrac108'")]
    public TextMeshProUGUI patientIdText;

    [Tooltip("Mostra il totale fratture, es. 'Fratture totali: 6'")]
    public TextMeshProUGUI fractureCountText;

    [Tooltip("Mostra i conteggi per classe (Severe / Not displaced / Buckled)")]
    public TextMeshProUGUI classBreakdownText;

    private const string PLACEHOLDER = "—";

    void Start()
    {
        Clear();
    }

    // ── Stato iniziale / reset ─────────────────────────────────────────────────
    public void Clear()
    {
        SetText(patientIdText,     PLACEHOLDER);
        SetText(fractureCountText, PLACEHOLDER);
        SetText(classBreakdownText, PLACEHOLDER);
    }

    // ── Punto 3.4: paziente non nel test set ──────────────────────────────────
    public void ShowNotInTestSet(string patientId)
    {
        SetText(patientIdText,     "Patient ID: " + ExtractNumber(patientId));
        SetText(fractureCountText, "Patient not in test set");
        SetText(classBreakdownText, "Predictions not available");
    }

    // ── Riepilogo (modi No lesions / All lesions) ─────────────────────────────
    // Mostra ID paziente e totale mesh; il dettaglio per classe compare quando
    // si seleziona un classificatore.
    public void ShowSummary(string patientId, int totalFractures, int nAmbiguous)
    {
        SetText(patientIdText, "Patient ID: " + ExtractNumber(patientId));
        SetText(fractureCountText, $"<b><i>Total fractures:</i></b> {totalFractures}");
        SetText(classBreakdownText,
            $"<i>Ambiguous:</i> {nAmbiguous}\n" +
            "<i>Select a classifier</i>\n<i>for the breakdown</i>");
    }

    // ── Dettaglio per classificatore ──────────────────────────────────────────
    // def          = classificatore attivo (definisce l'ordine delle classi)
    // counts       = conteggio per nome-classe
    // nUnclassified = mesh non classificate dal classificatore (incl. ambigue)
    public void ShowClassifier(string patientId, FractureClasses.ClassifierDef def,
                               int totalFractures, System.Collections.Generic.Dictionary<string, int> counts,
                               int nUnclassified)
    {
        SetText(patientIdText, "Patient ID: " + ExtractNumber(patientId));
        SetText(fractureCountText, $"<b><i>Total fractures:</i></b> {totalFractures}");

        var sb = new System.Text.StringBuilder();
        foreach (string cls in def.classes)
        {
            int n = counts != null && counts.TryGetValue(cls, out int v) ? v : 0;
            sb.Append($"<i>{cls}:</i> {n}\n");
        }
        sb.Append($"<i>Unclassified:</i> {nUnclassified}");
        SetText(classBreakdownText, sb.ToString());
    }

    // ── Helper null-safe ───────────────────────────────────────────────────────
    static void SetText(TextMeshProUGUI field, string value)
    {
        if (field != null) field.text = value;
    }

    // ── Estrae le cifre finali: "RibFrac108" → "108" ──────────────────────────
    static string ExtractNumber(string id)
    {
        int i = id.Length - 1;
        while (i >= 0 && char.IsDigit(id[i])) i--;
        string digits = id.Substring(i + 1);
        return digits.Length > 0 ? digits : id;
    }
}
