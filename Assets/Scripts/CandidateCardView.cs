using TMPro;
using UnityEngine;

// Presentation only. CandidateSyncTest remains responsible for visibility/reveal.
public class CandidateCardView : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.RawImage cardImage;
    [SerializeField] private UnityEngine.UI.AspectRatioFitter imageAspect;
    [SerializeField] private TMP_Text participantLabel;
    [SerializeField] private TMP_Text cardLabel;

    public void SetContent(int participantDisplayId, string cardId, Texture texture)
    {
        if (participantLabel != null) participantLabel.text = $"P{participantDisplayId}";
        if (cardLabel != null) cardLabel.text = cardId;
        if (cardImage != null)
        {
            cardImage.texture = texture;
            cardImage.color = texture != null ? Color.white : new Color(0.12f, 0.15f, 0.19f);
        }
        if (imageAspect != null && texture != null && texture.height > 0)
            imageAspect.aspectRatio = (float)texture.width / texture.height;
    }
}
