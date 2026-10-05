# Proposed 独立回答 Submit フロー

## 変更内容

全カードのanswered=trueだけではRevealしません。各ParticipantがPrivate Boardの「この回答で確定」を押した後、全登録Participantの提出と全回答の有効性を確認し、既存のPrepare / ACK / Commitで一斉Revealします。Experimenterは提出・完了判定の対象外です。Registryの期待人数チェックも維持しています。

`CandidateSyncTest` のcandidate / answered / selectedAt同期は既存のままです。独立提出は、Final Agreement Readyとは異なるSession-scoped Client Variablesで管理します。

- `independentAnswerSubmitted`: true / false
- `independentAnswerSubmissionProof`: 提出ID + 既存全カード回答のSHA-256照合値

新しい回答同期は作っていません。Proofは提出の対象が現在の回答と一致することを確認するための情報です。提出IDは毎回変わるため、取消・再提出後に前のPrepare / ACKを再利用しません。

## Submit / Cancel

本人の対象全カードがanswered=true、Tier=A〜D、selectedAtが有効なUTC ISO timestampで、本人の送信した回答がNetSyncの読み戻し値と一致し、カードを掴んでいないときだけSubmit可能です。Reset中・未接続・未登録・Experimenter・Reveal後はSubmit不可。

Submitを受け付けた瞬間から、送信待ちを含めてPrivateカード操作を禁止します。`PrivateTierBoardController.CanInteract`、PrivateカードXR Select Filter、Editor入力に加え、`CandidateSyncTest.CanSend`も提出ロックを確認するので、回答変更・Unclassifiedへの取消もできません。

Reveal前は「確定を取り消す」を押せます。Submitted=falseとProof消去を送信し、取消の受信確認後に編集へ戻ります。本人のPrepare ACKも失効させます。取消は回答自体を削除しません。Prepare中にも取消を受け付け、全員の条件が崩れればCommitしません。Commitが成立した後の取消は不可です。

## UI

Private Boardの `LocalView/IndependentAnswerSubmitCanvas` に、既存Final AgreementパネルのDark / teal / 角丸スタイルとWorld Space raycasterを再利用した別パネルを追加。Ready処理・Readyコールバックは取り除き、Submit専用コンポーネントへ接続しました。Privateカード操作面の右側に離して配置。Transform / ScaleはInspectorから調整可能です。

- 未回答: 「すべてのカードを配置してください」、ボタン無効
- 全回答: 「この回答で確定」
- 提出後: 「回答確定済み / 相手の回答を待っています...」、「確定を取り消す」
- Reveal後 / Normal / Experimenter: パネル非表示

日本語の表示にはOFLライセンスのNoto Sans JPと、使用文字を事前収録したStatic TMP SDFを同梱しています。Quest側のOSフォントに依存しません。

## Reset / reconnect

既存Session ID / Epoch設計でSubmitted=false、Proof空文字へ初期化。ローカル送信待ち状態・提出ロックもReset開始イベントで解除します。Private回答、Readonly Reveal、Main配置、Final Agreementの既存Reset処理を維持。

同一Sessionの再接続では、NetSyncの初期Client Variable同期完了後に本人のSubmitted / Proofを読み戻し、Privateロックと提出済みUIを復元します。Reveal済みの場合は既存CommitからReadonly UIと後続Main状態を復元し、取消不可です。復元対象は、既存NetSyncが同じParticipantのClient Variablesとして復元するデータです。アプリ再起動で別Participant IDとして新規登録されるケースのID再割当て方式は変更していません。

## ファイル

新規（各metaを含む）:
- `Assets/Scripts/IndependentAnswerSubmission.cs`
- `Assets/Scripts/IndependentAnswerSubmissionUI.cs`
- `Assets/Editor/IndependentAnswerSubmissionSetup.cs`
- `Assets/Editor/IndependentAnswerSubmissionVerification.cs`
- `Assets/Fonts/IndependentAnswer/`（Noto Sans JP、OFL.txt、SubmitJapanese SDF）
- 本書と `IndependentAnswerSubmitted.png`

変更:
- `Assets/Scripts/CandidateSyncTest.cs`
- `Assets/Scripts/PrivateTierBoardController.cs`
- `Assets/Scripts/ProposedRevealCoordinator.cs`
- `Assets/Scripts/ExperimentSessionManager.cs`
- `Assets/Scenes/QuestSetup.unity`
- `Assets/Editor/ProposedRevealVerification.cs`
- `Assets/Editor/ProposedSharedPlacementVerification.cs`

## 検証

実NetSync Offline transport / 実コンポーネントを使う検証: Reveal / Submit 75項目、UI 14項目、Main / Final Agreement 45項目、Private / Normal 25項目、計159項目PASS。Editor / Android定義でのC#コンパイルも成功。

通常のOffline Play Modeでは、全回答後もWaitingForAnswers / Reveal=falseで待機し、ボタン操作後にReveal / Main Discussionへ移行、共同配置 / Final Agreement / Confirmedまで確認。元Sceneへ戻し、一時Sceneを削除済みです。Questの実際のGrabとEditorとのネットワーク2端末テストは未実施です。

## Editor + Quest確認手順

1. 変更後のQuestSetupをAndroidへビルドしてQuestに導入します。以前の第3段階APKには今回のSubmitフローは含まれません。
2. EditorとQuestを同一Roomに接続。Editorを一度ExperimenterとしてProposed / New Sessionを実行し、EditorをParticipantへ戻します。QuestはParticipant、期待人数は2。
3. Editor: F6/F7/F8（またはQ/W/E）+ 1〜4/0でPrivate回答。Quest: PrivateカードをGrab / Release。未回答がある本人の確定ボタンは無効。
4. P1だけ全カード回答、次にP2も全カード回答。どちらの時点でも未SubmitならRevealせず、相手のTierが見えないことを確認。
5. P1の「この回答で確定」を押す。本人のPrivate編集不可、P2は編集可能、Revealしないことを確認。
6. P1の「確定を取り消す」を押す。編集が戻り、置き直し・Unclassified取消・再提出が可能なことを確認。P1提出後の再接続でも提出済みUIとロックが復元されることを確認。
7. P1/P2双方が確定すると一斉Reveal。両者のReadonly初期回答とMainが表示され、提出取消不可となることを確認。
8. Mainを両端末から共同編集し、別のFinal Agreement Readyで全員Ready → Confirmedを確認。
9. New SessionでSubmitted=false、提出ロック・表示解除、Private初期化、Reveal消去を確認。Normalの共同配置・Ready・Resetも確認。
