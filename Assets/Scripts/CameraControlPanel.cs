using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Pannello UI con bottoni per controllare la camera.
/// Collega i bottoni al CameraController tramite EventTrigger (tieni premuto).
///
/// Come usarlo:
/// 1. Aggiungi questo script a un GameObject nel Canvas (es. il Panel del pannello)
/// 2. Nell'Inspector, trascina la Main Camera nel campo "Camera Controller"
/// 3. Trascina ogni bottone nel campo corrispondente
/// </summary>
public class CameraControlPanel : MonoBehaviour
{
    [Header("Riferimento Camera")]
    public CameraController cameraController;

    [Header("Bottoni Rotazione")]
    public Button btnRotateUp;
    public Button btnRotateDown;
    public Button btnRotateLeft;
    public Button btnRotateRight;

    [Header("Bottoni Zoom")]
    public Button btnZoomIn;
    public Button btnZoomOut;

    [Header("Reset")]
    public Button btnReset;

    void Start()
    {
        if (cameraController == null)
        {
            Debug.LogError("[CameraControlPanel] Nessun CameraController assegnato! Trascinalo nell'Inspector.");
            return;
        }

        SetupHoldButton(btnRotateUp,    cameraController.BeginRotateUp,    cameraController.EndRotateUp);
        SetupHoldButton(btnRotateDown,  cameraController.BeginRotateDown,  cameraController.EndRotateDown);
        SetupHoldButton(btnRotateLeft,  cameraController.BeginRotateLeft,  cameraController.EndRotateLeft);
        SetupHoldButton(btnRotateRight, cameraController.BeginRotateRight, cameraController.EndRotateRight);
        SetupHoldButton(btnZoomIn,      cameraController.BeginZoomIn,      cameraController.EndZoomIn);
        SetupHoldButton(btnZoomOut,     cameraController.BeginZoomOut,     cameraController.EndZoomOut);

        if (btnReset != null)
            btnReset.onClick.AddListener(cameraController.ResetView);
    }

    /// <summary>
    /// Aggiunge EventTrigger a un bottone per gestire "tieni premuto".
    /// PointerDown → onDown(), PointerUp e PointerExit → onUp()
    /// </summary>
    void SetupHoldButton(Button btn, System.Action onDown, System.Action onUp)
    {
        if (btn == null) return;

        var trigger = btn.gameObject.AddComponent<EventTrigger>();

        AddTriggerEntry(trigger, EventTriggerType.PointerDown, _ => onDown());
        AddTriggerEntry(trigger, EventTriggerType.PointerUp,   _ => onUp());
        // PointerExit: se l'utente trascina il dito fuori dal bottone, la camera si ferma
        AddTriggerEntry(trigger, EventTriggerType.PointerExit, _ => onUp());
    }

    void AddTriggerEntry(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }
}
