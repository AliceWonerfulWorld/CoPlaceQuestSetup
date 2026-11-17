using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Local interaction only. No Main card references or network transform.
[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
public class PrivateAnswerCard : MonoBehaviour, IXRSelectFilter
{
    [SerializeField] private string cardId;
    [SerializeField] private PrivateTierBoardController board;
    private XRGrabInteractable grab;
    private Rigidbody body;
    private TierZone currentZone, hoveredZone;
    private bool suppressRelease;
    private Vector3 grabStartPosition;
    private Quaternion grabStartRotation;
    public TierZone PreviewDropTarget => hoveredZone;
    public void ConfigureId(string id) => cardId = id;
    public string CardId => cardId;
    public string CurrentTier => currentZone != null ? currentZone.tierName : "Unclassified";
    public bool IsHeld => grab != null && grab.isSelected;
    public bool canProcess => isActiveAndEnabled;
    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        => board != null && board.CanInteract;

    private void Awake()
    {
        if (body != null) return;
        body = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        grab.selectFilters.Add(this);
        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
    }
    private void OnDestroy()
    {
        if (currentZone != null) currentZone.ReleaseCard(gameObject);
        ClearHover();
        if (grab == null) return;
        grab.selectFilters.Remove(this);
        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);
    }
    private void CancelDrag()
    {
        ClearHover();
        if (grab != null && grab.isSelected && grab.interactionManager != null)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
    }
    private void OnDisable() => CancelDrag();
    private void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
    private void OnApplicationPause(bool paused) { if (paused) CancelDrag(); }
    private void OnGrabbed(SelectEnterEventArgs args)
    {
        grabStartPosition = transform.position; grabStartRotation = transform.rotation; body.isKinematic = false;
    }
    private void LateUpdate() => UpdateDragFeedback(IsHeld);
    private void UpdateDragFeedback(bool held)
    {
        TierZone next = held && board != null && board.CanInteract ? board.ResolveDropTarget(gameObject, hoveredZone) : null;
        if (next != hoveredZone) { ClearHover(); hoveredZone = next; }
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, true);
    }
    private void ClearHover()
    {
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = null;
    }
    private void OnReleased(SelectExitEventArgs args)
    {
        TierZone zone = !suppressRelease && !args.isCanceled && board != null ? board.ResolveDropTarget(gameObject, hoveredZone) : null;
        ClearHover();
        if (suppressRelease) return;
        if (zone == null || !board.TryPlaceCard(cardId, zone.tierName)) RestorePosition();
    }
    public void SetInteractionEnabled(bool enabled)
    {
        if (!enabled) ClearHover();
        if (grab == null || grab.enabled == enabled) return;
        suppressRelease = true;
        grab.enabled = enabled;
        suppressRelease = false;
        if (!enabled) { ClearHover(); RestorePosition(); }
    }
    public bool CanPlace(TierZone zone) => zone != null && zone.PeekAvailableSnapPoint(gameObject) != null;
    public bool ApplyPlacement(TierZone zone, bool forceRelease = false)
    {
        if (body == null) Awake();
        if (zone == null || (!forceRelease && IsHeld)) return false;
        bool wasEnabled = grab != null && grab.enabled;
        if (forceRelease) SetInteractionEnabled(false);
        Transform point = zone.GetAvailableSnapPoint(gameObject);
        if (point == null) { if (forceRelease) SetInteractionEnabled(wasEnabled); return false; }
        if (currentZone != null && currentZone != zone) currentZone.ReleaseCard(gameObject);
        currentZone = zone;
        ClearHover(); MoveTo(point);
        if (forceRelease) SetInteractionEnabled(wasEnabled && board != null && board.CanInteract);
        return true;
    }
    private void RestorePosition()
    {
        Transform point = currentZone != null ? currentZone.GetAvailableSnapPoint(gameObject) : null;
        if (point != null) MoveTo(point);
        else MoveToPose(grabStartPosition, grabStartRotation);
    }
    private void MoveTo(Transform point) => MoveToPose(point.position, point.rotation);
    private void MoveToPose(Vector3 position, Quaternion rotation)
    {
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        body.position = position; body.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        body.isKinematic = true;
    }
}
