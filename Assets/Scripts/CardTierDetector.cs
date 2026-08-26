using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CardTierDetector : MonoBehaviour
{
    public string CurrentTier { get; private set; } = "Unclassified";

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
    }
}
