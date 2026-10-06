# Confirmed / Result Waiting

既存の `ExperimentState.Confirmed` を `ExperimentResultUI.LateUpdate` で検知します。
新しいState、結果Board、結果一覧、条件自動遷移は追加しません。

## Hierarchy

```text
TierBoard
  ExperimentResult (ExperimentResultUI)
    ResultContent (World Space Canvas / 既存XR Raycaster)
      Border / Surface
      Title: FINAL RESULT
      ConfirmedMessage: 配置が確定しました
      WaitingMessage: 実験は終了しました / 実験者の指示があるまでお待ちください
      ExperimenterControls (Experimenterのみ)
        Border / Surface
        SessionInformation (Mode / Session ID / Confirmed / 待機)
        NewSessionButton (New Session / Reset)
          Label
```

Main Canvas上端の近傍にDark / Tealの角丸パネルを配置。日本語フォントは
`Assets/Fonts/IndependentAnswer/ResultJapanese SDF.asset` の静的Atlasを使用。
表示以外の領域はRaycastを遮断しません。

NormalはMainとResultのみ。Proposedは既存Readonly Initial Answerを保持してMainとResultを併記。
MainのカードやTierZoneをコピー・再生成せず、既存FinalAgreementの最終Snapshotと
`SetBoardConfirmation` / `SetNormalConfirmationLock` による固定を利用します。
Confirmed中は配置送信とXR選択を拒否し、Ready操作パネルを非表示にします。

`OnSessionResetStarted` でResultを非表示。Reset処理中も表示条件から除外し、
ExperimenterのNew Sessionが受理された時点では直ちに非表示にします。
既存Session ResetがState / Confirmed / Ready / Main / Private / Revealを初期化します。
Result内のボタンは既存 `StartNewSession()` を呼び、次条件や実験開始を自動実行しません。

## Editor確認

1. QuestSetupを開く。既にResultをSceneに組み込んでいます。
2. Tools > Experiment > Verify Result Waiting Flows を実行。
   `Library/ExperimentResultVerification/result.json` のpassedとchecksを確認。
   既存LobbyからNormal / ProposedのFinal Agreement・Resetに加え、Result表示、
   Participant / Experimenter差、操作禁止、日本語Glyph、位置保持を検証します。
3. Play ModeではExperimenter + Participant 2台で同じRoomに接続。
   New Session → Normal選択 → Start → 全カード配置 → 各Participant Ready。
4. Confirmed後、配置が変化せず、Grab・Ready・配置変更不可、Resultが表示されること。
5. ExperimenterのResult内New Session / ResetでLobbyに戻り、Resultが消え、
   MainがUnclassified・Ready解除となること。
6. ProposedでPrivate回答 → Submit → Reveal → Main編集 → 全員Ready。
   Initial AnswerがReadonlyのまま残り、MainとResultを同時に確認できること。
   Reset後にInitial Answer・Resultが消えること。

## Quest確認

Android Build And Runで同じSceneをQuestへ導入し、EditorのExperimenterと
必要台数のParticipantで上記フローを両方式実行します。
Resultの日本語が欠けず読めること、Mainを覆わないこと、確定カードを
Ray / Direct Grabできないこと、ExperimenterだけResetを行えることを確認。
Reset後はResult / Revealが残らず、次SessionのStartを実験者が手動で行います。

## 実施結果

EditorのPreview Scene上でParticipant 2台 + ExperimenterのNetSync配信を再現し、48項目PASS。
Normal / ProposedのResult画像を保存済み。QuestのADB接続は確認済みですが、
実機Build / XR操作 / 複数端末の実ネットワークフローは未実施です。
