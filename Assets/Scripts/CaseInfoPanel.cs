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

    [Tooltip("Mostra i conteggi per classe (Displaced / Non-displaced / Buckle)")]
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
        SetText(fractureCountText, "Paziente non nel test set");
        SetText(classBreakdownText, "Predizioni non disponibili");
    }

    // ── Punto 3.2: popola il pannello dai dati predizioni ─────────────────────
    public void Refresh(PredictionData data)
    {
        if (data == null) { Clear(); return; }

        SetText(patientIdText, "Patient ID: " + ExtractNumber(data.public_id));

        int nDisplaced    = 0;
        int nNonDisplaced = 0;
        int nBuckle       = 0;

        if (data.fractures != null)
        {
            foreach (var f in data.fractures)
            {
                switch (f.predicted_class)
                {
                    case "Displaced":     nDisplaced++;    break;
                    case "Non-displaced": nNonDisplaced++; break;
                    case "Buckle":        nBuckle++;       break;
                }
            }
        }

        int total = data.fractures != null ? data.fractures.Length : 0;
        SetText(fractureCountText, $"<b><i>Fratture totali:</i></b> {total}");
        SetText(classBreakdownText,
            $"<i>Displaced:</i> {nDisplaced}\n" +
            $"<i>Non-displaced:</i> {nNonDisplaced}\n" +
            $"<i>Buckle:</i> {nBuckle}");
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
