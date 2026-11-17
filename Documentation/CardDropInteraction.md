# CoPlace：配置判定・ハイライト・スナップ予定枠

実装日: 2026-11-13。対象は `Assets/Scenes/QuestSetup.unity` の共有／個人Tier Board。既存の未コミット変更を保持して追加した。

## 配置しづらかった原因

1. **見た目と判定領域の不一致。** TierZoneのBoxColliderはlocal size `(0.86, 0.93, 5)`、center `(0.05, 0, -1.2)`。共有ボードでは幅2.322m・高さ約0.428mで、中心も右へ13.5cmずれていた。一方、行のRectTransformは高さ460pxに縮小済みなのに、背景・ハイライト等の子画像には600pxの固定高さが残り、見た目が隣接行へ重なっていた。
2. **奥行きの制約。** カード中心をCollider内に入れなければ候補にならず、Rayで掴んだカードがボードより手前に浮いていると、XYではTier上に見えても候補にならなかった。Rayが背景に当たった位置をドロップ先としていたわけではない。
3. **Eのハイライト参照がDを指していた。** セット追加時にDを複製したEについて、共有・個人とも参照の付け直しが不足していた。Eへ移動してもDが点灯する原因になっていた。
4. 共有と個人で判定処理が別々に実装され、権限喪失・無効化時の表示解除にも不足があった。既存ハイライト部品は存在したが、上記の理由で意図を十分に伝えられていなかった。

## 既存処理の確認

- カードは `XRGrabInteractable`。Sceneの左右コントローラーは `XRRayInteractor` を使い、XRI Default Input ActionsのSelect（コントローラーではGripButton）で選択する。Select Enter／Exitイベントを共有カードの `CardTierDetector`、個人カードの `PrivateAnswerCard` が受ける。
- SceneにXRDirectInteractorは配置されていない。判定は特定InteractorのRay命中位置に依存せず、共通のXRGrabイベントとカード中心を使うため、入力方式を混在させる別の判定は導入していない。実際のDirect Interaction機器試験は未実施。
- RaycastのTrigger Interactionは左右ともIgnoreである。TierのColliderはTriggerのまま維持し、広げたTriggerがRayによるカード選択を遮らない構成を保った。
- 共有カードはReleaseでTierとSnapPointを決定し、既存 `NormalPlacementSync.SendPlacement()` へ送る。NormalとReveal後の共同分類は同じ共有経路。
- 個人カードはReleaseで `PrivateTierBoardController.TryPlaceCard()` を呼び、既存候補変数・Submit回答ハッシュへ反映する。Submit／Cancel／Prepare→ACK→Reveal／Final AgreementのプロトコルとIDは変更していない。
- 配置先は従来どおりCard IDに対応する固定SnapPoint。A/B各8枚の4列×2行を維持し、空間的な挿入やID並べ替えは追加していない。
- Reset時は、セット／縮尺の適用後に全TierのColliderを再計算し、候補表示を消去する。既存の回答・配置・Submit・Reveal・確定状態の初期化経路を維持した。

## 判定方法と範囲

**`CardDropTargetResolver.Resolve()` が共有・個人のドラッグ表示とReleaseの両方に使われる唯一の配置先選択関数。** Releaseでは表示中の前候補も同じ関数へ渡し、境界のヒステリシス条件を共有する。

- 判定点はカードの `transform.position`（カード中心）。写真の端・コントローラーのRay命中点は使用しない。
- 基準面は各Tierの実際の背景行RectTransform。背景のOutline／Surface／Interior／DropHighlightもこのRectへ追従するよう修正した。
- Pilotセットの各行は幅2.7m・高さ46cm、行間ピッチ50cm。Xは背景行の幅、Yは背景の上下に各2cmを加えた範囲を使う。隣接行は中間線で接し、行間の判定の穴をなくした。Developmentの縮小個人ボードでは上下余白も親縮尺に合わせる。
- 深さは背景面から**手前80cm、背後12cm**。この範囲外ではXYが重なっていても候補を出さない。離れた位置から意図せずボードへ吸着させないため、無限平面投影にはしていない。
- 複数候補が境界で接した場合は行中心への垂直距離で決定する。完全な同距離ではTier IDの順で決定する。
- ドラッグ中は前候補を**境界から約1.5cm**保持する。5mm程度の揺れで点滅させず、隣側へ2cm移動した場合には候補が切り替わる。保持は有効なボード領域内だけで行い、外側へ出たカードを吸い戻す判定には使わない。
- 対応する固定スロットが使えない場合は候補から除外する。
- Colliderの中心・寸法も同じ背景Rect・余白・奥行きから算出する。Pilot適用時の個人ボードの縮尺変更後にも再計算する。

無効領域、キャンセル、権限不足、送信拒否の場合は元のスロットへ復帰する。共有カードは送信が受理されてからローカル位置を確定するようにし、送信失敗で見た目だけが変わったままにならないようにした。

## ハイライトと予定位置プレビュー

- 選択中かつ既存Role／Mode／Phase／Ready／Confirmedの操作条件を満たす場合だけ、Resolveの候補行を半透明の明るい緑青色で表示する。
- A〜Eに加え、従来の回答取消／未分類への返却先であるUnclassifiedも同じ方式で示す。
- 各行は独立したハイライトを持つ。EはE自身の行へ参照を修正した。Tier名はハイライトより前面に描画し、読める状態を維持する。
- 1つのローカルボードで原則1つだけ表示する。両手で別々のカードを同時に掴んだ場合は、先に候補を表示したドラッグが表示を所有し、終了後にもう一方へ引き継ぐ。カード選択自体の仕様は変更していない。
- **P1の配置予定枠も実装済み。** 固定SnapPointのカード寸法と回転に合わせた枠を表示する。Z-fightingを避けるため、カード表面より4mm手前へ出す。XY位置と回転は実際のスナップ位置と一致する。
- 枠は各Tierで初回に生成し、以降は再利用する。アニメーション、毎フレームの生成、Collider、GraphicRaycaster、Raycast Targetは追加しない。
- `PeekAvailableSnapPoint()` は予約せずに予定スロットを返す。ハイライト／プレビューだけでは、配置変数・回答・スロット占有を変更しない。
- Release、領域外、選択キャンセル、無効化、Submit、ローカルReady、確定、Mode／Phase変更、Reset、フォーカス喪失、一時停止時に表示を解除する。フォーカス喪失／一時停止／カードコンポーネント無効化時は、選択中ならXRIのキャンセルAPIを呼び、キャンセル扱いで復帰する。
- 表示のネットワーク通信は追加していない。カードの配置・回答は従来の送信だけを使う。

## テスト

### 自動検証（Unity Editor、実機ではない）

`Tools > Experiment > Verify Card Drop Targets`: **1163項目成功**。結果は `Documentation/CardDropVerification.json`。最新ソースを再コンパイルし、完了を確認してから実行した。

- A/B両セット、Normal／Proposed、8枚の連続配置・再配置を実コンポーネントで検証。
- A〜E／Unclassifiedの中央、左右端、全隣接境界、5mmの揺れと2cmの越境、手前／背後の範囲外を検証。
- ハイライト・Release先・予定枠・実スナップ位置の一致、元の位置への復帰、終了時の消去を検証。
- ドラッグ中の予約／配置通信がないこと、使用不能スロットに偽の候補を出さないこと、同時ローカルドラッグの単一表示を検証。
- Experimenterの操作禁止、Submit／Cancel、Reveal、共同分類、Final Agreement、無効化、フォーカス喪失、一時停止、ドラッグ中Resetを検証。
- 参加者2＋実験者1のNetSync offline transportと、XRの選択／解放ハンドラーへの模擬イベントを使う。**実際のコントローラー、Ray追従、Direct Interaction、無線遅延は再現していない。**

既存 `Verify Pilot Card Sets` **409項目も再実行して成功**。A/B／DevelopmentのNormal／Proposed、同期、独立回答、Submit、Reveal、確定、Resetの回帰を確認した。検証の最終件数と成功／失敗は各JSONに記録する。

Editorの画像: `Documentation/CardDropPreview/`。Scene設定は組み込み済み。通常は `Update Card Drop Targets` の再実行は不要。

### Quest 3実機

**今回の実装版はQuest 3実機で未検証。APKビルド／インストールも未実施。**

推奨確認手順:

1. 最新コードのAPKを再ビルドしてQuestへインストールする。以前のAPKには今回の修正は反映されない。
2. PCと同じRoomへ接続し、Aを選択してReset→Normal→Start。1台試験なら一時的にExpected Participant Countを1にする。
3. カードを掴み、背景から20〜80cm程度手前でA〜Eへ動かす。背景の点灯と枠の予定位置を確認し、離した結果が一致することを確かめる。
4. 全ての境界をゆっくり往復し、小さな手ぶれで点滅せず、境界を越えると1つの行へ切り替わることを確かめる。左右端と背景の外、手前80cmより遠い位置でも意図しない吸着が起きないことを確認する。
5. 全8枚を同じTierへ順に配置し、8枚を別のTierへ再配置する。移動中だけ表示され、離した後には枠も点灯も残らないことを確認する。
6. Proposedの個人ボードでも同様に確認する。Submit後は点灯せず、Cancelで操作が戻り、Reveal後は共有ボード側だけ操作・点灯できることを確認する。
7. カード操作中にReset、アプリ一時停止／復帰、コントローラー追跡中断も確認し、表示や掴み状態が残らないことを確認する。
8. Bへ切り替えて繰り返す。最後に2台で、同時操作とネットワーク同期、両者のSubmit待ち・最終合意を確認する。1台試験後は参加者数を2へ戻す。

## 残る課題・注意

- 手前80cm・ヒステリシス1.5cmは明示した初期値であり、Questの実際の持ち方での最終調整は未実施。RayがTier上を指していても、カード本体が有効な奥行きへ入っていなければ点灯／配置しない。
- 物理デバイスのRay追従、トラッキング中断からの復帰、無線同期時の操作感、実機フレームレートは未検証。
- 判定はカード中心基準。大きなカードの一部だけが行に掛かっている場合、中心が示す候補が採用される。点灯と予定枠を操作時の基準にする。
- 2枚を両手で同時にドラッグする場合、1つだけ点灯する表示所有ルールがある。通常の1枚ずつの操作を主対象としている。

## 今回の変更ファイル

追加（Unityファイルには `.meta` も追加）:

- `Assets/Scripts/CardDropTargetResolver.cs`
- `Assets/Editor/CardDropSetup.cs`
- `Assets/Editor/CardDropVerification.cs`
- `Documentation/CardDropInteraction.md`
- `Documentation/CardDropVerification.json`
- `Documentation/CardDropPreview/` のEditor描画

変更:

- `Assets/Scripts/TierZone.cs`: 背景基準の範囲、Collider再計算、非予約のスロット取得、ローカルの単一ハイライトと予定枠。
- `Assets/Scripts/CardTierDetector.cs`: 共通判定を使う共有カードのHover／Release、送信拒否時の復帰、権限・中断時の解除。
- `Assets/Scripts/PrivateAnswerCard.cs`: 共通判定を使う個人カードのHover／Release、キャンセルと中断時の解除。
- `Assets/Scripts/PrivateTierBoardController.cs`: 個人側の判定を共通化し、事前確認でスロットを予約しないよう変更。
- `Assets/Editor/PilotCardSetSetup.cs`: 新規導入時にも今回の行・Collider・参照設定を適用。
- `Assets/Scenes/QuestSetup.unity`: 12行のRect追従設定、独立したハイライト参照、背景に合わせたColliderと範囲パラメータ。

カード画像・Card ID・Tier ID・セット選択・ネットワーク変数仕様は変更していない。既存のステージ済み／未コミット変更を破棄せず、コミットやAPK配布は行っていない。
