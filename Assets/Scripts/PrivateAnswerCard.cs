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
    public string CardId => cardId;
    public string CurrentTier => currentZone != null ? currentZone.tierName : "Unclassified";
    public bool IsHeld => grab != null && grab.isSelected;
    public bool canProcess => isActiveAndEnabled;
    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        => board != null && board.CanInteract;

    private void Awake()
    {
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
    private void OnDisable() => ClearHover();
    private void OnGrabbed(SelectEnterEventArgs args) { body.isKinematic = false; }
    private void Update()
    {
        TierZone next = IsHeld && board != null && board.CanInteract ? board.FindDropZone(transform.position) : null;
        if (next == hoveredZone) return;
        ClearHover(); hoveredZone = next;
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, true);
    }
    private void ClearHover()
    {
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = null;
    }
    private void OnReleased(SelectExitEventArgs args)
    {
        ClearHover();
        if (suppressRelease) return;
        TierZone zone = board != null ? board.FindDropZone(transform.position) : null;
        if (zone == null || !board.TryPlaceCard(cardId, zone.tierName)) RestorePosition();
    }
    public void SetInteractionEnabled(bool enabled)
    {
        if (grab == null || grab.enabled == enabled) return;
        suppressRelease = true;
        grab.enabled = enabled;
        suppressRelease = false;
        if (!enabled) { ClearHover(); RestorePosition(); }
    }
    public bool CanPlace(TierZone zone) => zone != null && zone.snapPoints.Exists(p => p != null);
    public bool ApplyPlacement(TierZone zone, bool forceRelease = false)
    {
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
        if (currentZone == null) return;
        Transform point = currentZone.GetAvailableSnapPoint(gameObject);
        if (point != null) MoveTo(point);
    }
    private void MoveTo(Transform point)
    {
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        body.position = point.position; body.rotation = point.rotation;
        transform.SetPositionAndRotation(point.position, point.rotation);
        body.isKinematic = true;
    }
}
