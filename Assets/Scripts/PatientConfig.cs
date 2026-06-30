/// <summary>
/// Struttura dati che descrive un paziente con tutti i percorsi ai file necessari.
/// Costruita da PatientRegistry a runtime scansionando StreamingAssets/Patients/.
/// </summary>
[System.Serializable]
public class PatientConfig
{
    public string id;               // es. "RibFrac108"
    public string niftiPath;        // path assoluto al .nii.gz
    public string predictionsPath;  // path assoluto al _predictions.json
    public string ambiguousPath;    // path assoluto al _ambiguous.json
    public string meshFolder;       // path assoluto alla cartella Meshes/
}
