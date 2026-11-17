using System;
using System.Linq;
using TMPro;
using UnityEngine;

// Selection is captured by Session Reset control, never taken from a peer's Inspector.
public sealed class PilotCardSetController : MonoBehaviour
{
    public enum CardSet { Development, A, B }
    [Serializable] public class Card
    {
        public string id, displayName;
        public Texture texture;
        public Material material;
    }
    [Tooltip("Experimenter only: applied on the NEXT New Session / Reset. Changing this does not alter a running trial.")]
    [SerializeField] private CardSet nextSessionSet = CardSet.Development;
    [SerializeField] private Card[] setA, setB, development;
    [SerializeField] private CardTierDetector[] sharedCards;
    [SerializeField] private PrivateAnswerCard[] privateCards;
    [SerializeField] private TierZone[] sharedZones, privateZones;
    [SerializeField] private NormalPlacementSync normalSync;
    [SerializeField] private CandidateSyncTest candidates;
    [SerializeField] private PrivateTierBoardController privateBoard;
    [SerializeField] private ReadonlyParticipantBoardView readonlyBoard;
    [SerializeField] private TMP_FontAsset cardFont;
    [SerializeField] private Material cardBacking;
    private Vector3 originalPrivateScale, originalPrivatePosition;
    private bool privateTransformCaptured;
    public string NextSessionSetId => nextSessionSet.ToString();
    public string ActiveSetId { get; private set; } = "Development";

    // Called only inside the session reset guard, before defaults and ACK are built.
    public void ApplySessionSet(string setId)
    {
        Card[] data = setId == "A" ? setA : setId == "B" ? setB : setId == "Development" ? development : null;
        if (data == null || data.Length != (setId == "Development" ? 3 : 8) ||
            sharedCards.Length < data.Length || privateCards.Length < data.Length ||
            data.Any(c => c == null || string.IsNullOrEmpty(c.id) || c.texture == null || c.material == null) ||
            data.Select(c => c.id).Distinct().Count() != data.Length)
            throw new InvalidOperationException("Invalid or missing card set: " + setId);
        bool pilot = setId != "Development";
        if (pilot && (cardFont == null || cardBacking == null)) throw new InvalidOperationException("Pilot card presentation assets missing; run Update Pilot Card Readability");
        if (!privateTransformCaptured)
        {
            originalPrivateScale = privateBoard.transform.localScale; originalPrivatePosition = privateBoard.transform.position;
            privateTransformCaptured = true;
        }
        // Keep the private board's viewing plane in place while removing its 0.6x shrink.
        if (pilot)
        {
            Vector3 anchor = privateZones.First(z => z.tierName == "C").transform.position;
            privateBoard.transform.localScale = Vector3.one;
            privateBoard.transform.position += anchor - privateZones.First(z => z.tierName == "C").transform.position;
            // Use the shared board's vertical extent: both rows of Unclassified stay above the floor.
            var position = privateBoard.transform.position;
            position.y += sharedZones.First(z => z.tierName == "C").transform.position.y - privateZones.First(z => z.tierName == "C").transform.position.y;
            privateBoard.transform.position = position;
        }
        else { privateBoard.transform.localScale = originalPrivateScale; privateBoard.transform.position = originalPrivatePosition; }
        // Release previous reservations (including cards that will be hidden).
        foreach (var zone in sharedZones.Concat(privateZones))
            foreach (var card in sharedCards.Select(c => c.gameObject).Concat(privateCards.Select(c => c.gameObject))) zone.ReleaseCard(card);
        for (int i = 0; i < sharedCards.Length; i++)
        {
            bool visible = i < data.Length;
            if (visible)
            {
                sharedCards[i].transform.localScale = setId == "Development" ? new Vector3(0.25f, 0.25f, 0.03f) : new Vector3(PilotCardFace.Width, PilotCardFace.Height, 0.03f);
                privateCards[i].transform.localScale = setId == "Development" ? new Vector3(0.41666666f, 0.41666666f, 0.05f) : new Vector3(PilotCardFace.Width, PilotCardFace.Height, 0.03f);
                sharedCards[i].name = data[i].id;
                privateCards[i].name = "Private_" + data[i].id;
                privateCards[i].ConfigureId(data[i].id);
                SetVisual(sharedCards[i].gameObject, data[i], pilot);
                SetVisual(privateCards[i].gameObject, data[i], pilot);
            }
            sharedCards[i].gameObject.SetActive(visible);
            privateCards[i].gameObject.SetActive(visible);
        }
        string[] ids = data.Select(c => c.id).ToArray();
        foreach (var zone in sharedZones) zone.ConfigureCardSlots(ids);
        foreach (var zone in privateZones) zone.ConfigureCardSlots(ids.Select(id => "Private_" + id).ToArray());
        normalSync.ConfigureCards(sharedCards.Take(data.Length).ToArray());
        privateBoard.ConfigureCards(privateCards.Take(data.Length).ToArray());
        candidates.ConfigureCards(ids);
        readonlyBoard.ConfigureCards(data.Select(c => new ReadonlyParticipantBoardView.CardVisual {
            cardId = c.id, displayName = c.displayName, texture = c.texture }).ToArray());
        ActiveSetId = setId;
        Debug.Log("[Pilot Cards] Session set " + setId + " / " + string.Join(",", ids), this);
    }
    private void SetVisual(GameObject target, Card card, bool pilot)
    {
        // Existing cards have one mesh renderer; keep original UV/material conventions.
        target.GetComponent<Renderer>().sharedMaterial = pilot ? cardBacking : card.material;
        var legacyLabel = target.transform.Find("CardName");
        if (legacyLabel != null) legacyLabel.gameObject.SetActive(!pilot);
        var face = target.GetComponent<PilotCardFace>();
        if (pilot)
        {
            if (face == null) face = target.AddComponent<PilotCardFace>();
            face.enabled = true;
            face.Configure(card.id, card.displayName, card.texture, cardFont);
        }
        else if (face != null) { face.SetVisible(false); face.enabled = false; }
    }
}
