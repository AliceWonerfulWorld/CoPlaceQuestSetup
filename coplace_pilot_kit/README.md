# CoPlace 予備実験用カードセット（Ver.1）

- `participant_task_instructions.md`：参加者に読む共通説明・方式別説明、研究実施者メモ
- `cards/Set_A/`：セットAの8枚のPNG画像（720×480px）
- `cards/Set_B/`：セットBの8枚のPNG画像（720×480px）
- `metadata/card_manifest.csv`：カードID、表示名、画像の対応表（Excelでも開けるUTF-8 BOM）
- `previews/card_contact_sheet.png`：16枚のプレビュー一覧

**Unityへの取り込み方法：** PNGを `Assets/...` にコピーし、Texture Typeを`Sprite (2D and UI)`に設定。CoPlaceのカード定義へ`card_id`と画像を対応付けてください。既存のカード読み込み方式に合わせたコード変更は別途必要な場合があります。

**実験上の注意：** 画像にはランキングの正解や判断材料を記載していません。カードセット間の難度は未検証です。予備実験で確認してください。
