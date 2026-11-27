using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CardTierDetector : MonoBehaviour, UnityEngine.XR.Interaction.Toolkit.Filtering.IXRSelectFilter
{
    public bool canProcess => isActiveAndEnabled;
    private bool CanEditSharedBoard => normalPlacementSync != null && normalPlacementSync.CanLocalEditSharedBoard;
    public bool Process(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor,
        UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable interactable)
        => CanDrag;

    private bool CanDrag => isActiveAndEnabled && CanEditSharedBoard && !normalPlacementSync.IsResettingSession &&
        !normalPlacementSync.IsLocalInteractionLocked && !normalPlacementSync.IsCardConfirmed(gameObject.name);
    public TierZone PreviewDropTarget => hoveredZone;
    public string CurrentTier { get; private set; } = "Unclassified";

    [SerializeField]
    private ExperimentManager experimentManager;

    [SerializeField]
    private NormalPlacementSync normalPlacementSync;

    // 今カードが入っている可能性のあるTier
    private TierZone candidateZone;

    // 現在カードが実際に配置されているTier
    private TierZone currentZone;

    [Tooltip("Drop targets cached in the scene. The card center determines the nearest valid Tier.")]
    [SerializeField] private TierZone[] placementZones;
    private TierZone hoveredZone;
    private readonly TierZone[] legacyZone = new TierZone[1];
    private Vector3 grabStartPosition;
    private Quaternion grabStartRotation;

    private bool normalConfirmationLocked;
    private bool grabWasEnabled;
    public bool CanConfirmNormalPlacement => grabInteractable != null && !grabInteractable.isSelected;

    public void SetNormalConfirmationLock(bool locked, TierZone finalZone)
    {
        if (grabInteractable == null || rb == null) return;
        if (locked) ClearHover();
        if (locked != normalConfirmationLocked)
        {
            normalConfirmationLocked = locked;
            if (locked)
            {
                grabWasEnabled = grabInteractable.enabled;
                grabInteractable.enabled = false;
                Debug.Log($"[Normal Card Locked] {gameObject.name}");
            }
            else grabInteractable.enabled = grabWasEnabled;
        }
        if (!locked || finalZone == null) return;
        Transform point = finalZone.GetAvailableSnapPoint(gameObject);
        if (point == null) return;
        if (currentZone == finalZone && CurrentTier == finalZone.tierName && rb.isKinematic &&
            !grabInteractable.isSelected && (transform.position - point.position).sqrMagnitude < 0.000001f &&
            Quaternion.Angle(transform.rotation, point.rotation) < 0.01f) return;
        if (currentZone != null && currentZone != finalZone) currentZone.ReleaseCard(gameObject);
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = null;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = point.position;
        rb.rotation = point.rotation;
        transform.SetPositionAndRotation(point.position, point.rotation);
        rb.isKinematic = true;
        currentZone = candidateZone = finalZone;
        CurrentTier = finalZone.tierName;
    }

    private Rigidbody rb;
    private XRGrabInteractable grabInteractable;

    private void Awake() 
    {
        if (rb != null) return;
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectFilters.Add(this);
        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    private void OnDestroy()
    {
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        if (currentZone != null) currentZone.ReleaseCard(gameObject);
        if (grabInteractable == null) return;
        grabInteractable.selectFilters.Remove(this);
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }


    private void OnGrabbed(SelectEnterEventArgs args)
    {
        if (normalPlacementSync != null && normalPlacementSync.RejectLocalInteraction(gameObject.name)) return;
        grabStartPosition = transform.position;
        grabStartRotation = transform.rotation;
        // 再び掴んだ時は動かせるようにする。
        rb.isKinematic = false;
    }

    private void LateUpdate() => UpdateDragFeedback(grabInteractable != null && grabInteractable.isSelected);
    private void UpdateDragFeedback(bool held)
    {
        TierZone target = held && CanDrag ? FindDropZone() : null;
        if (target != hoveredZone) { ClearHover(); hoveredZone = target; }
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, true);
    }
    private void ClearHover()
    {
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = null;
    }
    private void CancelDrag()
    {
        ClearHover();
        if (grabInteractable != null && grabInteractable.isSelected && grabInteractable.interactionManager != null)
            grabInteractable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grabInteractable);
    }
    private void OnDisable() => CancelDrag();
    private void OnApplicationFocus(bool focused) { if (!focused) CancelDrag(); }
    private void OnApplicationPause(bool paused) { if (paused) CancelDrag(); }
    private TierZone FindDropZone()
    {
        if (placementZones == null || placementZones.Length == 0)
        { legacyZone[0] = candidateZone; return CardDropTargetResolver.Resolve(legacyZone, gameObject, transform.position, hoveredZone); }
        return CardDropTargetResolver.Resolve(placementZones, gameObject, transform.position, hoveredZone);
    }

    private void RestorePreviousPlacement()
    {
        Transform point = currentZone != null ? currentZone.GetAvailableSnapPoint(gameObject) : null;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(point != null ? point.position : grabStartPosition,
            point != null ? point.rotation : grabStartRotation);
        rb.isKinematic = true;
        candidateZone = currentZone;
    }

    private void OnTriggerEnter(Collider other)
    {
        TierZone zone = other.GetComponent<TierZone>();

        if (zone == null)
            return;
        
        candidateZone = zone;

        Debug.Log(
            $"{gameObject.name} -> Tier {zone.tierName} candidate"
        );
    }

    private void OnTriggerExit(Collider other)
    {
        TierZone zone = other.GetComponent<TierZone>();

        if (zone == null)
            return;

        if (candidateZone == zone)
        {
            candidateZone = null;
        }
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        // Preserve the previous target while resolving hysteresis, then clear all presentation.
        TierZone target = CanDrag && !args.isCanceled ? FindDropZone() : null;
        ClearHover();
        if (target == null) { RestorePreviousPlacement(); return; }
        Transform point = target.GetAvailableSnapPoint(gameObject);
        if (point == null) { RestorePreviousPlacement(); return; }
        // Commit through the existing transport before moving; a rejected send must restore the old placement.
        if (!normalPlacementSync.SendPlacement(gameObject.name, target.tierName, true))
        {
            if (currentZone != target) target.ReleaseCard(gameObject);
            RestorePreviousPlacement(); return;
        }
        if (currentZone != null && currentZone != target) currentZone.ReleaseCard(gameObject);
        rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        rb.position = point.position; rb.rotation = point.rotation;
        transform.SetPositionAndRotation(point.position, point.rotation); rb.isKinematic = true;
        currentZone = candidateZone = target; CurrentTier = target.tierName;
        Debug.Log($"[Card Drop] {gameObject.name} -> {CurrentTier}", this);
    }

    public void ResetSessionPlacement(TierZone zone)
    {
        // Disabling releases any current XR selection; reset guards suppress its exit send.
        SetNormalConfirmationLock(false, null);
        bool enabled = grabInteractable.enabled;
        grabInteractable.enabled = false;
        if (currentZone != null) currentZone.ReleaseCard(gameObject);
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = null; currentZone = candidateZone = null;
        Transform point = zone.GetAvailableSnapPoint(gameObject);
        if (point == null) { grabInteractable.enabled = enabled; throw new System.InvalidOperationException("Unclassified SnapPoint missing: " + name); }
        rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        rb.position = point.position; rb.rotation = point.rotation;
        transform.SetPositionAndRotation(point.position, point.rotation); rb.isKinematic = true;
        currentZone = candidateZone = zone; CurrentTier = zone.tierName;
        grabInteractable.enabled = enabled;
    }

    // Called only by NormalPlacementSync. This never calls OnReleased or sends variables.
    // While held, return false so the synchronizer can retain the latest remote placement.
    public bool ApplySyncedPlacement(TierZone zone)
    {
        if (normalPlacementSync == null || !normalPlacementSync.IsSharedPlacementPhase
            || zone == null || grabInteractable.isSelected)
            return false;

        if (normalPlacementSync.RejectConfirmedPlacement(gameObject.name)) return false;
        Transform snapPoint = zone.GetAvailableSnapPoint(gameObject);
        if (snapPoint == null) return false;

        if (currentZone != null && currentZone != zone)
            currentZone.ReleaseCard(gameObject);

        // Clear both velocities before freezing, including a previously kinematic card.
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = snapPoint.position;
        rb.rotation = snapPoint.rotation;
        transform.SetPositionAndRotation(snapPoint.position, snapPoint.rotation);
        rb.isKinematic = true;

        currentZone = zone;
        candidateZone = zone;
        CurrentTier = zone.tierName;
        Debug.Log($"[Normal Placement Apply] {gameObject.name} -> Tier {CurrentTier}");
        return true;
    }
}
