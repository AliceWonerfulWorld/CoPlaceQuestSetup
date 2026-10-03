using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CardTierDetector : MonoBehaviour
{
    public string CurrentTier { get; private set; } = "Unclassified";

    // 候補同期用
    [SerializeField]
    private CandidateSyncTest candidateSync;

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
    private Vector3 grabStartPosition;
    private Quaternion grabStartRotation;

    private Rigidbody rb;
    private XRGrabInteractable grabInteractable;

    private void Awake() 
    {
        rb = GetComponent<Rigidbody>();
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrabbed);
        grabInteractable.selectExited.AddListener(OnReleased);
    }

    private void OnDestroy()
    {
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        if (currentZone != null) currentZone.ReleaseCard(gameObject);
        if (grabInteractable == null) return;
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }


    private void OnGrabbed(SelectEnterEventArgs args)
    {
        grabStartPosition = transform.position;
        grabStartRotation = transform.rotation;
        // 再び掴んだ時は動かせるようにする。
        rb.isKinematic = false;
    }

    private void Update()
    {
        TierZone target = grabInteractable != null && grabInteractable.isSelected ? FindDropZone() : null;
        if (target == hoveredZone) return;
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = target;
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, true);
    }

    private TierZone FindDropZone()
    {
        // Legacy/test setups may still use trigger-driven selection.
        if (placementZones == null || placementZones.Length == 0) return candidateZone;
        TierZone closest = null;
        float best = float.PositiveInfinity;
        foreach (var zone in placementZones)
        {
            if (zone == null || !zone.ContainsDropPoint(transform.position)) continue;
            float distance = (zone.transform.position - transform.position).sqrMagnitude;
            if (distance < best) { closest = zone; best = distance; }
        }
        return closest;
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
        if (hoveredZone != null) hoveredZone.SetHovered(gameObject, false);
        hoveredZone = null;
        if (candidateSync != null && experimentManager != null &&
            experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Proposed &&
            candidateSync.IsCardRevealed(gameObject.name))
        {
            // Keep the physical card consistent with the frozen revealed answer.
            if (currentZone != null)
            {
                Transform previousPoint = currentZone.GetAvailableSnapPoint(gameObject);
                if (previousPoint != null)
                {
                    rb.isKinematic = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    transform.SetPositionAndRotation(previousPoint.position, previousPoint.rotation);
                    rb.isKinematic = true;
                    candidateZone = currentZone;
                }
            }
            Debug.LogWarning($"[Candidate] Reveal後のカード変更・取消は禁止: {gameObject.name}", this);
            return;
        }
        candidateZone = FindDropZone();
        if (candidateZone == null)
        {
            RestorePreviousPlacement();
            return;
        }

        // Reserve the new slot before releasing the old one. Full/invalid targets
        // return the card to its prior position without changing the answer.
        Transform snapPoint = candidateZone.GetAvailableSnapPoint(gameObject);
        if (snapPoint == null)
        {
            RestorePreviousPlacement();
            return;
        }
        if (currentZone != null && currentZone != candidateZone)
            currentZone.ReleaseCard(gameObject);

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        transform.position = snapPoint.position;
        transform.rotation = snapPoint.rotation;

        // 配置後は物理演算で動かないよう固定
        rb.isKinematic = true;

        // 現在位置を更新
        currentZone = candidateZone;
        CurrentTier = candidateZone.tierName;

        Debug.Log(
            $"{gameObject.name} classified as Tier {CurrentTier}"
            );
        
        if (experimentManager != null &&
            experimentManager.CurrentMode == ExperimentManager.ExperimentMode.Normal)
        {
            if (normalPlacementSync != null)
                normalPlacementSync.SendPlacement(gameObject.name, CurrentTier);
        }
        // Proposedの候補・回答状態・selectedAtは既存処理をそのまま使用する。
        else if (candidateSync != null)
        {
            if (CurrentTier == "Unclassified")
            {
                candidateSync.CancelCandidate(
                    gameObject.name
                );
            }
            else
            {
                candidateSync.SendCandidate(
                    gameObject.name,
                    CurrentTier
                );
            }
        }
    }

    // Called only by NormalPlacementSync. This never calls OnReleased or sends variables.
    // While held, return false so the synchronizer can retain the latest remote placement.
    public bool ApplySyncedPlacement(TierZone zone)
    {
        if (normalPlacementSync == null || !normalPlacementSync.IsNormalMode
            || zone == null || grabInteractable.isSelected)
            return false;

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
