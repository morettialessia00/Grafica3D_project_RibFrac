using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pannello di selezione paziente. Si apre quando l'utente preme "Load DATA"
/// e mostra un bottone per ogni paziente trovato in StreamingAssets/Patients/.
///
/// Setup in Unity (vedi Sezione 7 della guida):
///   1. Crea un Panel figlio del Canvas, rinominalo "PatientSelectorPanel".
///   2. Aggiungi questo script al Panel.
///   3. Nell'Inspector, assegna: ctLoader e buttonContainer.
///   4. Collega il bottone "Load DATA" a PatientSelectorPanel.Open().
/// I bottoni vengono creati via codice a runtime, nessun prefab necessario.
/// </summary>
public class PatientSelectorPanel : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Il CTLoader nella scena")]
    public CTLoader ctLoader;

    [Tooltip("Il Transform contenitore in cui vengono creati i bottoni (con VerticalLayoutGroup)")]
    public Transform buttonContainer;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        // Il pannello parte nascosto
        gameObject.SetActive(false);
    }

    // ── API pubblica ──────────────────────────────────────────────────────────

    /// <summary>Apre il pannello e popola la lista pazienti.</summary>
    public void Open()
    {
        PopulateList();
        gameObject.SetActive(true);
    }

    /// <summary>Chiude il pannello senza caricare nulla.</summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    // ── Popolamento lista ─────────────────────────────────────────────────────
    void PopulateList()
    {
        // Rimuovi bottoni precedenti (per aggiornamenti a runtime)
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        List<PatientConfig> patients = PatientRegistry.GetAll();

        if (patients.Count == 0)
        {
            // Mostra un messaggio se non ci sono pazienti
            GameObject msgObj = new GameObject("NoPatients");
            msgObj.transform.SetParent(buttonContainer, false);
            var txt = msgObj.AddComponent<TextMeshProUGUI>();
            txt.text = "Nessun paziente trovato.\nVerifica la struttura di StreamingAssets/Patients/.";
            txt.fontSize = 14;
            txt.alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (PatientConfig config in patients)
        {
            // Cattura la variabile nel closure
            PatientConfig captured = config;

            // Crea il bottone via codice (nessun prefab necessario)
            GameObject btnObj = new GameObject(captured.id);
            btnObj.transform.SetParent(buttonContainer, false);

            // Sfondo del bottone
            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            // Componente Button
            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;

            // Altezza fissa: Layout Element comunica al VerticalLayoutGroup l'altezza voluta
            LayoutElement le = btnObj.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;

            // Testo figlio
            GameObject textObj = new GameObject("Label");
            textObj.transform.SetParent(btnObj.transform, false);
            TextMeshProUGUI label = textObj.AddComponent<TextMeshProUGUI>();
            label.text = captured.id;
            label.fontSize = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;

            // Stira il testo a coprire tutto il bottone
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            // Al click: chiudi il pannello e avvia il caricamento
            btn.onClick.AddListener(() =>
            {
                Close();
                ctLoader?.LoadPatient(captured);
            });
        }
    }
}
