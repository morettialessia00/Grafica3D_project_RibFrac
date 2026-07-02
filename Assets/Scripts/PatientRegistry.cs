using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Scansiona Application.streamingAssetsPath/Patients/ e restituisce
/// la lista dei pazienti disponibili in base alla naming convention.
///
/// Naming convention attesa per paziente con ID "RibFracXXX":
///   StreamingAssets/Patients/RibFracXXX/RibFracXXX-image.nii.gz
///   StreamingAssets/Patients/RibFracXXX/RibFracXXX_predictions.json
///   StreamingAssets/Patients/RibFracXXX/RibFracXXX_ambiguous.json   (opzionale)
///   StreamingAssets/Patients/RibFracXXX/Meshes/                     (opzionale)
/// </summary>
public static class PatientRegistry
{
    private static List<PatientConfig> _cache = null;

    /// <summary>
    /// Restituisce la lista dei pazienti trovati. Il risultato viene
    /// cachato dopo la prima chiamata (nessuna re-scansione a ogni accesso).
    /// </summary>
    public static List<PatientConfig> GetAll()
    {
        if (_cache != null) return _cache;

        _cache = new List<PatientConfig>();

        string patientsRoot = Path.Combine(Application.streamingAssetsPath, "Patients");

        if (!Directory.Exists(patientsRoot))
        {
            Debug.LogError($"[PatientRegistry] Cartella non trovata: {patientsRoot}\n" +
                           "Assicurati che la struttura StreamingAssets/Patients/ esista.");
            return _cache;
        }

        foreach (string patientDir in Directory.GetDirectories(patientsRoot)
            .OrderBy(d => { var m = Regex.Match(Path.GetFileName(d), @"\d+$"); return m.Success ? int.Parse(m.Value) : int.MaxValue; })
            .ThenBy(d => Path.GetFileName(d)))
        {
            string id = Path.GetFileName(patientDir);

            string nifti       = Path.Combine(patientDir, $"{id}-image.nii.gz");
            string predictions = Path.Combine(patientDir, $"{id}_predictions.json");
            string ambiguous   = Path.Combine(patientDir, $"{id}_ambiguous.json");
            string meshFolder  = Path.Combine(patientDir, "Meshes");

            // Il file NIfTI è obbligatorio: senza di esso il paziente viene ignorato.
            if (!File.Exists(nifti))
            {
                Debug.LogWarning($"[PatientRegistry] NIfTI non trovato per '{id}', " +
                                 $"paziente ignorato. Path atteso: {nifti}");
                continue;
            }

            _cache.Add(new PatientConfig
            {
                id              = id,
                niftiPath       = nifti,
                predictionsPath = File.Exists(predictions) ? predictions : "",
                ambiguousPath   = File.Exists(ambiguous)   ? ambiguous   : "",
                meshFolder      = Directory.Exists(meshFolder) ? meshFolder : patientDir
            });

            Debug.Log($"[PatientRegistry] Trovato paziente: {id}");
        }

        Debug.Log($"[PatientRegistry] Totale pazienti trovati: {_cache.Count}");
        return _cache;
    }

    /// <summary>
    /// Forza la ri-scansione al prossimo accesso (utile se i file cambiano a runtime).
    /// </summary>
    public static void InvalidateCache() => _cache = null;
}
