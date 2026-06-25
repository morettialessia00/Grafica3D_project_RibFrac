using UnityEditor;
using UnityEngine;

/// <summary>
/// Utility per riordinare i figli di un GameObject nella Hierarchy
/// senza usare il drag and drop (workaround bug Unity 6).
///
/// Uso: seleziona il GameObject da spostare nella Hierarchy,
/// poi clicca il menu corrispondente in Tools.
/// </summary>
public static class HierarchyReorder
{
    [MenuItem("Tools/Reorder/Sposta selezionato → primo figlio")]
    static void MoveToFirst()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogError("[Reorder] Nessun GameObject selezionato."); return; }
        go.transform.SetSiblingIndex(0);
        Debug.Log($"[Reorder] '{go.name}' spostato in posizione 0 (primo figlio).");
    }

    [MenuItem("Tools/Reorder/Sposta selezionato → ultimo figlio")]
    static void MoveToLast()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogError("[Reorder] Nessun GameObject selezionato."); return; }
        go.transform.SetAsLastSibling();
        Debug.Log($"[Reorder] '{go.name}' spostato come ultimo figlio.");
    }

    [MenuItem("Tools/Reorder/Sposta selezionato → su di uno")]
    static void MoveUp()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogError("[Reorder] Nessun GameObject selezionato."); return; }
        int idx = go.transform.GetSiblingIndex();
        if (idx > 0) go.transform.SetSiblingIndex(idx - 1);
        Debug.Log($"[Reorder] '{go.name}' spostato a indice {go.transform.GetSiblingIndex()}.");
    }

    [MenuItem("Tools/Reorder/Sposta selezionato → giù di uno")]
    static void MoveDown()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogError("[Reorder] Nessun GameObject selezionato."); return; }
        int idx = go.transform.GetSiblingIndex();
        go.transform.SetSiblingIndex(idx + 1);
        Debug.Log($"[Reorder] '{go.name}' spostato a indice {go.transform.GetSiblingIndex()}.");
    }
}
