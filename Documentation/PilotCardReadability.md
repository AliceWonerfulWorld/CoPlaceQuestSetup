# 予備実験カードの視認性改善

実装日: 2026-11-12。Set A／Bの写真・イベント名・Card ID・課題・評価基準は変更していない。元PNGの書き換えも行っていない。

## 調査結果

| 項目 | 変更前 | 改善後 |
| --- | --- | --- |
| 共有カードの実寸 | 24×16cm（厚さ3cm） | 54×22cm（厚さ3cm） |
| 個人カードの実寸 | 14.4×9.6cm。カードのlocalScaleに親ボード0.6倍が掛かっていた | 共有と同じ54×22cm。Pilotセットではボード縮尺を1倍に補正し、共有と同じ高さへ合わせる |
| イベント名 | 720×480 PNG内の小さな焼き込み文字 | 独立した白い太字TextMeshPro。固定42pt、Canvasは1unit=1mm、名前領域510×88mm。自動縮小なし |
| Card ID | PNG内の焼き込み | 補助表示22pt。イベント名から分離 |
| 写真 | 名前やフッターを含むPNG全体 | 元PNGの写真部分をUV指定し、縦横比を保持。画像内の小文字・重複した名前は表示しない |
| 配置 | 8枚横一列、約25.65cmピッチ | 各Tier／Unclassifiedとも4列×2行。横58cm・縦23cmピッチ |
| Reveal | 横一列の小さな画像と縮小可能なラベル | 各Tierを4列×2行に変更。写真アイコン＋固定30ptの太字名、長い名前は2行で全文表示。参加者ボードは縦に並べる |
| Texture Import | maxSize=2048、MipMap有効、圧縮、Bilinear、NPOT=ToNearest | maxSize／MipMap／圧縮は維持。NPOT=Noneにして実ロード720×480を確認。Clamp、Bilinear、Aniso=2 |

カード本体は独立したPrefabではなく、`QuestSetup` 内のCube MeshRenderer、Rigidbody、BoxCollider、XRGrabInteractable、CardTierDetector／PrivateAnswerCardを持つSceneオブジェクト。従来の `Assets/UI/CandidateCards/CandidateCard.prefab` は候補表示用で、今回の掴むカードの本体ではない。

旧横一列配置は `TierZone.ConfigureCardSlots()` にあり、カードID順にSnapPointのX座標を決めていた。今回も同じID順を使い、4列×2行の座標に変更した。座標は各端末で同じ式から算出する。

## 表示と操作への影響

`PilotCardFace` がカード表面にWorld Space Canvasを作り、写真と名前を表示する。写真のUV領域は元720×480画像の `(x=14, y=170, width=692, height=296)`（UVの下原点）で、約2.338:1の縦横比を保持する。元写真の内容を描き直したり、イベント名を画像へ再生成したりしていない。

元PNGの焼き込み文字が側面から出ないよう、カードの土台には無地のMaterialを使う。画像と名前の対応元は従来どおりセットカタログ。表示中のTextureは `PilotCardFace.SourceTexture` でも確認できる。

カード本体の拡大に伴い既存BoxColliderも同じ実寸になる。表面UIは全GraphicのRaycast Targetを無効にし、GraphicRaycasterやColliderを追加しないため、既存XR選択判定を遮らない。Canvasはカードの子なので、掴んだ際も写真と名前が追従する。

共有カードの表面UIは、既存のRendererの表示状態に合わせて隠す。Proposed独立回答中に共有カードの文字面だけが残らないことを検証した。個人ボードは拡大後も元の閲覧面の奥行きを維持し、縦位置は共有ボードに揃える。下段カードの下端は床から約22.5cm上に収まる。

Normal／Proposedのカードには同じコンポーネント、写真領域、字体、実寸を使う。Card ID、同期変数、回答ハッシュ、Submit／Revealの制御、Final Agreementのプロトコルは変更していない。A／B／Developmentの選択手順も従来どおり。Developmentは元の料理画像・Material・3枚構成・25cm実寸へ戻す。

## 検証

### Unity Editorで実施

- Unity 6000.3.17f1でコンパイル、Scene設定を保存。
- `Verify Pilot Card Readability`: **1696項目成功**。A→B→Aで名前全文、太字固定サイズ、共有／個人の実寸一致、写真のアスペクト、4×2の配置、全Tierの重なり・はみ出し、床上の高さ、選択を遮らないUI、Reveal名、Development復元を検証。
- `Verify Pilot Card Sets`: **409項目成功**。実コンポーネントとNetSync offline transportで参加者2＋実験者1を模擬。A/B切り替え、Normal同期と確定、独立回答、Submit、同時Reveal、共同分類、Final Agreement、Reset、再接続を確認。
- 1.0m／1.5mの距離を置いた透視投影カメラでA/Bの共有・個人カードを描画し、8枚の名前と写真を目視確認。カメラはカード列中心に向け、縦FOV75度、1600×1000で撮影した。**Questのレンズ・表示解像度や立位の目線を再現するものではない。**
- 全16枚の名前について、TMPの文字列、文字数、表示文字とoverflow状態を検証。旧レイアウトにあった自動縮小はイベント名には使用しない。

記録:

- `Documentation/PilotCardReadabilityVerification.json`
- `Documentation/PilotCardSetVerification.json`
- `Documentation/PilotReadability/A-Shared-1.5m.png`
- `Documentation/PilotReadability/B-Private-1.5m.png`
- `Documentation/PilotReadability/B-Proposed.png`
- その他の距離別画像: `Library/PilotReadability/`

再実行はUnityの `Tools > Experiment > Verify Pilot Card Readability` と `Verify Pilot Card Sets`。設定は組み込み済みで、通常は更新メニューの再実行は不要。

### Quest 3実機

**今回の改善版はQuest 3実機では未検証。APKのビルド・インストールも今回行っていない。** Editorで名前が欠けないことと、Questで通常位置から読めることは別の検証。

推奨確認手順:

1. 今回の変更を含めてAndroid APKを再ビルドし、Questへインストールする。以前インストールしたAPKには反映されない。
2. 実験者PCと同じRoomへ接続する。Questが1台だけなら、一時的にExpected Participant Countを1へ設定する。
3. Set Aを選び、New Session / Reset → Normal → Start。カード面から約1.5m、次に約1mの位置で、前へ移動せず8枚全ての名前を読めるか確認する。長いA05「チーム対抗クイズ大会」、A08「キャンパス写真ラリー」の末尾も確認する。
4. 全8枚を同じTierへ置き、上下2行で写真・名前・IDが隠れないこと、指し示したカードを正しく掴めること、ドロップ・確定ができることを確認する。A〜E各段も確認する。
5. Proposedで個人カードも同じ大きさ・高さに見え、8枚を分類してSubmitできること、Reveal側でも名前を識別でき、共有分類・確定できることを確認する。参加者数1では自分のSubmitだけで公開される。
6. Set Bへ切り替えてResetし、B07／B08を含む全8枚で繰り返す。さらにAへ戻し、前回のカードや配置が残らないことを確認する。
7. 2台を使う本番前には、実ネットワークで両者の同時操作・配置同期・公開前の秘匿・2人のSubmit待ちと合意も確認する。1台試験はこれらの代替ではない。試験後は参加者数を2へ戻す。

実機では立位の視線から上下のTierを見る際の読みやすさも確認する。頭の向きを変えるだけで識別できるかを確認し、ボードへ近づく必要が残る場合は閲覧距離と読めなかった名前を記録する。

## 今回の変更ファイル

追加（Unity資産には対応する `.meta` も追加）:

- `Assets/Scripts/PilotCardFace.cs`
- `Assets/Editor/PilotCardReadabilitySetup.cs`
- `Assets/Editor/PilotCardReadabilityVerification.cs`
- `Assets/PilotCards/PilotCardBacking.mat`
- `Documentation/PilotCardReadability.md`
- `Documentation/PilotCardReadabilityVerification.json`
- `Documentation/PilotReadability/` のEditor描画3枚

変更:

- `Assets/Scripts/PilotCardSetController.cs`: 新しい表面、実寸、個人ボード補正、写真／名前対応。
- `Assets/Scripts/TierZone.cs`: 4×2の固定配置。
- `Assets/Scripts/PrivateTierBoardController.cs`: 共有カードを隠す際に表面Canvasも隠す。
- `Assets/Scripts/ReadonlyParticipantBoardView.cs`: 4×2の名前中心のReveal表示。
- `Assets/Editor/PilotCardSetSetup.cs`: 新規導入・表示更新でも今回の設定を維持。
- `Assets/Editor/PilotCardSetVerification.cs`: 無地の土台ではなく実際に表示する写真の対応を検証。
- `Assets/Scenes/QuestSetup.unity`: フォント・無地Material参照、Revealのサイズ・配置。
- `Assets/PilotCards/` のPNG16枚の `.meta`: Texture Import設定。**PNG本体は変更なし。**
- `Documentation/PilotCardSets.md`: 最新の表示方式への案内。

既存のステージ済み変更・未コミット変更、Androidビルド設定は破棄していない。コミット・配布は行っていない。
