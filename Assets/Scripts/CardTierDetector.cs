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
        grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        grabInteractable.selectExited.RemoveListener(OnReleased);
    }


    private void OnGrabbed(SelectEnterEventArgs args)
    {
        // 再び掴んだ時は動かせるようにする。
        rb.isKinematic = false;
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
        if (candidateZone == null)
           return;

        // 別のTierへ移動する場合は
        // 今まで使用していたSnapPointを開放する
        if (currentZone != null && currentZone != candidateZone)
        {
            currentZone.ReleaseCard(gameObject);
        }

        // 空いているSnapPointを取得して、このカードに割り当てる
        Transform snapPoint = 
            candidateZone.GetAvailableSnapPoint(gameObject);

        if (snapPoint == null) 
           return;

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
