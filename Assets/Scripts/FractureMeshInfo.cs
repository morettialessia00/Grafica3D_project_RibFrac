using UnityEngine;

/// <summary>
/// Metadato attaccato a ogni GameObject-frattura caricato da CTLoader.
/// Conserva l'id della costa e se la mesh proviene da una frattura ambigua.
/// Usato per ricolorare per classificatore e per il pannello dettaglio.
/// </summary>
public class FractureMeshInfo : MonoBehaviour
{
    public int  ribId;
    public bool isAmbiguous;
}
