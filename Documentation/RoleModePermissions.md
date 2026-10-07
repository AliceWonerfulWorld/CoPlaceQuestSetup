# Production Role / Mode / Card permissions (S1–S3)

## Production

QuestSetup defaults to `AutoByPlatform`, with 2 expected Participants.
Unity Editor resolves to Experimenter; every Player build (including Quest) resolves to Participant.
The ExperimentManager references the scene Participant Registry.

Inspector: ExperimentManager > ExperimentParticipantRegistry > Production Role / Development Override.
Explicit Participant or Experimenter is a development override. Console reports `[Development Role Override]`.
Restore AutoByPlatform and expected count 2 before production builds.

## Start / Mode

Start requires local Experimenter, published Experimenter role, NetSync ready,
active Session (ID and activation time), no Reset, exactly the expected registered
Participants, their session-specific Idle states, synchronized Mode, and local Idle.
The Lobby shows Japanese blocking reasons, including actual / expected counts.
Participants cannot Start.

ExperimentManager.CanChangeMode / ModeChangeBlockReason is the central guard.
Only a connected Experimenter in pre-Start Lobby / Setup with Idle peers may change Mode.
Start pending, retained current-session Start, all active phases and Reset block changes.
Direct SetMode logs `[Mode Change Rejected]`; incompatible received Mode is also rejected
using the Mode in the retained Start record. New Session restores Lobby permissions.
The old ExperimentModeUI canvas stays hidden; use Lobby controls.

## Answering / viewing

ExperimentParticipantRegistry.CanOperateInSession supplies common permission:
Participant role, NetSync ready, registry membership, active Session, no Reset.
Normal and Proposed Main add their phase / Confirmed checks.
Private input adds independent-answer, Submit / Reveal lock checks.
Final Agreement Ready uses the same common permission and existing agreement checks.

Experimenter cannot Grab, locally send Main placements, answer Private cards or Ready.
Receiving placements and reading consent / result remain enabled.
CardTierDetector fails closed if its placement adapter is absent.
Editor keyboard input additionally requires explicit localRole = Participant,
not merely an effective Participant role.

## Editor + Quest verification

1. Production: AutoByPlatform, expected 2, PC Editor + two Quest builds on the same server / room.
2. One Quest: New Session may be prepared, but Start shows `参加者が不足しています 1 / 2`.
3. Two Quests: only Editor sees Mode / Start controls. Select Normal and Start.
4. Editor F6–F8 / 0–4 and direct SetMode must not change cards or Mode; rejection logs appear for SetMode.
5. Quest placement must appear on the other Quest and Editor. Only Quest participants can Ready.
6. Confirmed retains Main and Result; New Session returns to Lobby and permits Mode selection.
7. Repeat Proposed: no Editor Private answer; both Quest Submit, Reveal, shared editing, Ready and Result still work.
8. During Reset, Mode / card / Ready operations must remain disabled.

Development with Editor + one Quest and no separate Experimenter endpoint:
start Play with Editor as Auto/Experimenter and expected count 1; prepare Session / Mode
and Start with the Quest connected. Then, in the runtime Inspector, set expected count 2
and explicitly override Editor Role to Participant. Wait until both Participants are registered
before keyboard input / Submit / Ready. Switch back to Experimenter to manage New Session.
These are runtime test settings; do not save or build them into production.
Alternatively keep a separate Experimenter endpoint while the answering Editor is overridden.
The answering Participant never receives a Start/Mode bypass.

## Automated verification

Tools > Experiment > Verify Result Waiting Flows now includes S1–S3 permission cases
and full Normal / Proposed / Reset / same-session restoration flows using three isolated
offline NetSync endpoints. Report: Library/ExperimentResultVerification/result.json.
Tools > Experiment > Verify Lobby and Experiment Flows provides a separate Lobby regression.
These are simulated delivery checks; physical Quest Grab and online transport still need the steps above.

2026-10-06 verification: Result / permission flows passed 71 checks; existing Lobby flows passed 38 checks. Editor compilation completed without errors. No Quest build / physical device validation performed in this change.
