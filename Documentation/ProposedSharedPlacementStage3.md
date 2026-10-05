# Proposed 第3段階 — Reveal参照 + Main最終共同配置

## 実装範囲

Private独立回答 → 全Participant × 全Card answered確認 → 既存ACK一斉Reveal → Main最終共同配置 → 既存Final Agreement → Confirmed。Reveal方式・候補同期方式は変更していません。

## ファイル

新規:
- `Assets/Scripts/ProposedSharedPlacementController.cs`（およびmeta）
- `Assets/Editor/ProposedSharedPlacementSetup.cs`（およびmeta）
- `Assets/Editor/ProposedSharedPlacementVerification.cs`（およびmeta）
- 本書と `ProposedSharedPlacementStage3.png`

変更:
- `Assets/Scenes/QuestSetup.unity`
- `Assets/Scripts/NormalPlacementSync.cs`
- `Assets/Scripts/NormalPlacementSync.Editor.cs`
- `Assets/Scripts/NormalPlacementConfirmation.cs`
- `Assets/Scripts/CardTierDetector.cs`
- `Assets/Scripts/FinalAgreementManager.cs`
- `Assets/Scripts/FinalAgreementUI.cs`
- `Assets/Scripts/PrivateTierBoardController.cs`
- `Assets/Scripts/ProposedRevealCoordinator.cs`
- `Assets/Scripts/ExperimentSessionManager.cs`
- `Assets/Editor/ProposedRevealVerification.cs`（検証用endpointの共用）

## Scene / UI配置

`ProposedReveal` に `ProposedSharedPlacementController` を追加し、既存Manager・Registry・Session・Reveal・NormalPlacementSync・FinalAgreementへ接続。

`ParticipantAnswerReveal` は、実際のMain操作面 `TierBoardCanvas` の左2.25m・上0.3m・後方0.4mへ配置しました。World位置は約 `(-2.25, 2.19, 0.325)`、Y回転-15°、Canvas Scaleは0.0009。右側の既存Final Agreement UIと離れ、MainのTier操作領域と重なりません。Transform、Canvas Scale、columns、spacingはInspectorで調整できます。

既存のDark / teal / 半透明のReadonly World Space UIをそのまま使用。Participantごとの画像・Card ID・A〜D配置を表示します。Collider・XRGrabInteractable・Selectableを持たず、回答位置を変更しません。

```text
ProposedReveal
  ProposedRevealCoordinator
  ProposedSharedPlacementController
ParticipantAnswerReveal
  ReadonlyParticipantBoardView
  ReadonlyMiniBoards（Runtime）
    Participant別 World Space Canvas（動的生成）
      Header / Tier A〜D / Card画像・Card ID
TierBoard
  TierBoardCanvas（既存Main操作面）
NormalPlacementSync
  NormalPlacementSync / FinalAgreementManager
  FinalAgreementCanvas（既存Ready UI）
Card_01 / Card_02 / Card_03（既存Main実カード）
PrivateTierBoard（Reveal後は非表示・入力不可）
```

## Reveal後だけMainを有効化

`IsSharedPlacementPhase` は Normal または `ProposedSharedPlacementController.IsActive` のときtrue。Proposedでは、既存Revealのcommit後に選出Participant（ClientNo最小）が既存Main配置キーを全Unclassifiedに初期化します。Private回答はコピーしません。

初期値・配置revision・未確定Board AgreementをNetSyncから読み戻して一致した後、Session-scoped `proposedSharedPlacementReady` にReveal IDを保存。このmarkerが届いて初めてMain表示・XR選択・Editor入力を有効化し、既存State `Discussion` へ移ります。それまでMainは表示・操作とも無効です。

既にmarkerが存在する再接続ではRoomのMain配置を復元し、MainをUnclassifiedへ上書きしません。

## 既存同期とFinal Agreement

Mainは既存 `NormalPlacementSync` と `CardTierDetector` を再利用。`normalPlacement_<cardId>` と既存revision/held/Ready方式を使用し、別の配置同期は追加していません。Editor・Quest双方の配置、自由順序、置き直し、Unclassifiedへの取消を同じ経路で処理します。

`FinalAgreementManager.IsAgreementPhase` をMain共同配置フェーズへ拡張。Readyした本人はGrab/Editor配置不可、他Participantの配置変更でReadyが無効化・解除、全Participant Readyで既存Board Confirmedが成立します。ProposedのAgreement IDはReveal IDに結び付け、同一Sessionに残る古いNormalの承認・確定を適用しません。ExperimenterはProposed Mainと参照UIを閲覧できますがMain編集不可です。

Reveal UIはcommit済み初期回答スナップショットを描画し続けます。Main編集・Ready・Confirmedによってcandidateや初期回答を変更しません。再接続でConfirmedをRevealedへ戻さないようState復元を調整しました。

## Reset

既存Session ID / Epoch / reset ACK設計を維持。新markerもSession Resetのglobal初期値へ追加。Reset時は共有フェーズ解除、参照UI消去、Main / Private全Unclassified、候補・answered・selectedAt初期化、Ready・Board Confirmed解除、State Idleになります。

## 検証

- 実NetSync Offline transportと実コンポーネントを使う独立2 endpointの第3段階検証: **43項目PASS**。
- 既存Reveal検証: **58項目PASS**。
- 既存Private / Normal検証: **25項目PASS**。
- 一時Sceneを使用した通常のOffline Play Mode: Private A/B/C → Reveal → Main全Unclassified / Discussion → Main C/B/D → Ready → Confirmed。初期回答A/B/Cは保持。検証後は元Sceneへ戻し一時Sceneを削除しました。
- Quest向けDevelopment APK: **BuildResult Succeeded**。警告21件。BuildReportのエラー1件は長時間ビルド中の制御API `/api/exec` が30秒でタイムアウトしたログで、ビルドエラーではありません。APK（約102MB）のManifest・arm64 IL2CPP / Unityライブラリを確認しました。ビルドの一時XR preload設定は復元済みです。
- `ProposedSharedPlacementStage3.png` は2 endpoint検証時の描画。左にP1/P2初期回答、中央に共同配置C/C/D、右にBoard Confirmedを表示。

Editor + Questの実ネットワーク・実際の手でのXR Grabについては、下記手順で最終確認が必要です。

## Editor + Quest確認手順

1. 両端末を同じRoomへ接続。Editorを一度ExperimenterとしてProposedへ切り替え、New Sessionを実施した後、EditorのLocal RoleをParticipantへ変更します。QuestはParticipant（AutoByPlatformでも可）。Registryの期待人数を2に合わせます。
2. EditorのGame ViewでF6 / F7 / F8（またはQ / W / E）でPrivate Card_01 / 02 / 03を選択し、1 / 2 / 3 / 4 / 0でA / B / C / D / Unclassifiedへ配置。例: Editor A/B/C、Quest D/B/A。
3. QuestではPrivate CardをGrab・Releaseして回答。最後の未回答が残る間は相手回答・参照UIが見えず、Mainが触れないことを確認。
4. 全員全カード回答後、両端末で参照UIとMainが表示されることを確認。Mainの3枚はUnclassifiedで、Privateは操作不可。
5. Reveal後のEditor入力はNormalと同じF6 / F7 / F8 + 1〜4 / 0でMainを操作。Questは既存Main CardをGrab・Release。双方からA〜D・Unclassified・置き直しを行い、相手端末へ反映されることを確認。
6. Mainを編集しても左のP1/P2 INITIAL ANSWERが変化しないことを確認。
7. 片方が「This board is OK」を押し、本人がMainを動かせないことを確認。他方がMainを動かすとReady解除。双方ReadyでBoard Confirmedとなり、両端末でMain操作が禁止されることを確認。
8. 同一Session再接続でMain最終配置・Confirmed・初期回答参照が復元されることを確認。New Sessionで参照UI消去、Main / Private全Unclassified、Ready / Confirmed解除を確認。
9. Normalへ切り替え、従来通りMain配置同期・Final Agreement・Resetを確認。

第3段階APK: `Builds/ProposedSharedPlacementStage3.apk`。端末へ導入する場合は、このAPKを使用してください。
