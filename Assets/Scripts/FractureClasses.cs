using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Definizioni centrali dei classificatori e delle classi di frattura.
///
/// La tendina di visualizzazione ha 6 opzioni mappate su CurrentColorMode:
///   0 → No lesions     (mesh nascoste)
///   1 → All lesions    (tutte le mesh rosse, vista binaria)
///   2 → Classificatore 1  (Classifiers[0])
///   3 → Classificatore 2  (Classifiers[1])
///   4 → Classificatore 3  (Classifiers[2])
///   5 → Classificatore 4  (Classifiers[3])
///
/// Ogni classificatore legge un file JSON diverso (fileSuffix) e ha il proprio
/// insieme di classi (classes, in ordine di legenda). Colori e probabilita
/// sono risolti per nome di classe tramite gli helper qui sotto.
/// </summary>
public static class FractureClasses
{
    // ── Modalita ──────────────────────────────────────────────────────────────
    public const int ModeNone           = 0; // No lesions
    public const int ModeAll            = 1; // All lesions (binaria)
    public const int FirstClassifierMode = 2; // prima opzione classificatore

    public static bool IsClassifierMode(int mode) => mode >= FirstClassifierMode;

    /// <summary>Indice 0..3 del classificatore data la modalita (o -1 se non lo e).</summary>
    public static int ClassifierIndex(int mode) =>
        IsClassifierMode(mode) ? mode - FirstClassifierMode : -1;

    // ── Definizione di un classificatore ────────────────────────────────────────
    public class ClassifierDef
    {
        public string   displayName; // etichetta nella tendina
        public string   fileSuffix;  // suffisso file JSON (senza ".json")
        public string[] classes;     // classi in ordine di legenda
    }

    // Ordine allineato alle opzioni 2..5 della tendina.
    public static readonly ClassifierDef[] Classifiers =
    {
        new ClassifierDef
        {
            displayName = "4-class (D/ND/B/S)",
            fileSuffix  = "_predictions_4classes_D_ND_B_S",
            classes     = new[] { "Displaced", "Not displaced", "Buckled", "Segmental" }
        },
        new ClassifierDef
        {
            displayName = "3-class (D/ND/B)",
            fileSuffix  = "_predictions_3classes_D_ND_B",
            classes     = new[] { "Displaced", "Not displaced", "Buckled" }
        },
        new ClassifierDef
        {
            displayName = "3-class (S/ND/B)",
            fileSuffix  = "_predictions_3classes_SD_ND_B",
            classes     = new[] { "Severe", "Not displaced", "Buckled" }
        },
        new ClassifierDef
        {
            displayName = "2-class (S/NS)",
            fileSuffix  = "_predictions_2classes_SD_NDB",
            classes     = new[] { "Severe", "Not severe" }
        },
    };

    /// <summary>Etichette complete della tendina, nell'ordine delle modalita.</summary>
    public static List<string> DropdownOptions()
    {
        var opts = new List<string> { "No lesions", "All lesions" };
        foreach (var c in Classifiers) opts.Add(c.displayName);
        return opts;
    }

    // ── Colori per classe ───────────────────────────────────────────────────────
    // Palette color-blind safe. L'alpha viene applicato separatamente.
    private static readonly Dictionary<string, string> _hex = new Dictionary<string, string>
    {
        { "Displaced",     "#E74C3C" }, // rosso
        { "Not displaced", "#4A90D9" }, // blu
        { "Buckled",       "#9B59B6" }, // viola
        { "Segmental",     "#2ECC71" }, // verde
        { "Severe",        "#E87722" }, // arancio
        { "Not severe",    "#16A085" }, // verde acqua
    };

    public const string UnclassifiedHex = "#808080";
    public const float   MeshAlpha       = 0.35f;

    // Colore mesh per le fratture non classificate / ambigue.
    public static readonly Color UnclassifiedMeshColor = new Color(0.25f, 0.25f, 0.28f, 0.55f);

    /// <summary>Colore RGBA della mesh per una classe (alpha = MeshAlpha).</summary>
    public static Color MeshColor(string className)
    {
        if (className != null && _hex.TryGetValue(className, out string hex) &&
            ColorUtility.TryParseHtmlString(hex, out Color c))
        {
            c.a = MeshAlpha;
            return c;
        }
        return UnclassifiedMeshColor;
    }

    /// <summary>Colore semi-trasparente per la vista binaria "All lesions".</summary>
    public static readonly Color BinaryColor = new Color(1f, 0.15f, 0.15f, 0.35f);

    /// <summary>Stringa hex per rich text (#RRGGBB), grigio se sconosciuta.</summary>
    public static string Hex(string className) =>
        className != null && _hex.TryGetValue(className, out string h) ? h : UnclassifiedHex;

    // ── Probabilita per classe ──────────────────────────────────────────────────
    /// <summary>Restituisce la probabilita associata a una classe per l'entry data.</summary>
    public static float ProbForClass(FractureEntry e, string className)
    {
        if (e == null || className == null) return 0f;
        switch (className)
        {
            case "Displaced":     return e.prob_displaced;
            case "Not displaced": return e.prob_nondisplaced;
            case "Buckled":       return e.prob_buckle;
            case "Segmental":     return e.prob_segmental;
            case "Severe":        return e.prob_severe;
            case "Not severe":    return e.prob_nonsevere;
            default:              return 0f;
        }
    }
}
