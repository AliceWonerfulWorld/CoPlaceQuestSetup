using TMPro;
using UnityEngine;

// Presentation only. CandidateSyncTest remains responsible for visibility/reveal.
public class CandidateCardView : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.RawImage cardImage;
    [SerializeField] private UnityEngine.UI.AspectRatioFitter imageAspect;
    [SerializeField] private TMP_Text participantLabel;
    [SerializeField] private TMP_Text cardLabel;

    [SerializeField] private UnityEngine.UI.Image frame;
    [SerializeField] private UnityEngine.UI.Image participantBadge;

    public void SetContent(string label, Color accent, string cardId, Texture texture)
    {
        if (participantLabel != null)
        {
            participantLabel.text = label;
            participantLabel.color = new Color(accent.r, accent.g, accent.b, 1f);
        }
        if (frame != null) frame.color = new Color(accent.r, accent.g, accent.b, 0.72f);
        if (participantBadge != null) participantBadge.color = new Color(accent.r, accent.g, accent.b, 0.13f);
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
