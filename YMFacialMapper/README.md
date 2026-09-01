# YM Facial Mapper

テキストで指定した BlendShape / Shape Key を、VRChat のハンドサインに合わせて非破壊で適用する NDMF ツールです。
VRoid、MMD など、決まった名前の Shape Key を持つアバターへ設定をコピーしやすくすることを目的にしています。

## 使い方

1. AvatarRoot 配下に `Yoridori Modifiers/YM Facial Mapper` を追加します。
2. Preset を選ぶか、各ハンドサインの Shape Key リストへ名前を入力します。
3. 必要に応じて `Eyelid-L` / `Eyelid-R` / `Viseme` をONにします。
4. ビルド時に NDMF が FX Animator に YM Facial Mapper 用レイヤーを追加します。

Shape Key は1行に1つ指定します。名前の隣にある数値欄で 0～100 のウェイトを調整できます。ウェイトの初期値は100です。以前のバージョンで保存した設定やプリセットもそのまま使用できます。

ビルド時は、既存の Gesture Controller と Gesture パラメータで動く FX レイヤーに含まれる BlendShape カーブをビルド結果上で取り除き、YM Facial Mapper の表情へ置き換えます。元の Animator Controller と AnimationClip アセットは変更しません。

## Eyelid / Viseme

`Eyelid-L` / `Eyelid-R` / `Viseme` は、表情エントリ全体の排他タグ兼トラッキング停止タグです。

- `Eyelid-L` OFF / `Eyelid-R` OFF / `Viseme` OFF
  - 排他なし。左右で同時適用できます。
  - まばたきと口パクは止めません。
- `Eyelid-L` ON
  - Eyelid-L グループを占有します。
  - 表情中はまばたきを止めます。
- `Eyelid-R` ON
  - Eyelid-R グループを占有します。
  - 表情中はまばたきを止めます。
- `Viseme` ON
  - Viseme グループを占有します。
  - 表情中は口パクを止めます。
- 複数ON
  - ONにしたグループをそれぞれ占有します。
  - `Eyelid-L` または `Eyelid-R` のどちらかがONなら、表情中はまばたきを止めます。
  - `Viseme` がONなら、表情中は口パクを止めます。

同じグループを占有する表情が左右同時に成立した場合、`排他衝突時の判定 / Conflict Resolution` で指定した手が優先されます。初期値は右手優先です。

## Jerry's Templates との併用

Jerry's Templates の Modular Avatar 版と併用する場合、`FacialExpressionsDisabled` など既知の無効化パラメータを検出すると、そのパラメータがONの間は YM Facial Mapper の表情を止めます。
Jerry's Templates 側のアセットやコンポーネントは変更しません。

YM Facial Mapper の `Viseme` がONのハンドサイン中は、Jerry's Templates の `Visemes Enabled` がONでも口パク/口トラッキングを止めます。

MA Merge Animatorなどで統合されたFX Animatorにある`GestureLeft` / `GestureRight`がInt以外の場合は、条件式の不整合を防ぐためNDMF Build Reportへエラーを表示し、YM Facial Mapperレイヤーを追加しません。

## Preset JSON

同梱の `Presets.json` に加えて、ユーザー設定として `Assets/YM-Facial-Mapper-Presets.json` を読み込みます。
同名Presetがある場合も両方表示されます。

Presetを選ぶと、設定値とメモ欄がInspectorへ反映されます。
メモ欄はPresetの意図や想定アバター、左右の割り当て方などを記録するための自由記入欄です。

`Export` を押すとプリセット名の入力ウィンドウが開きます。決定すると、現在の設定値とメモ欄の内容を `Assets/YM-Facial-Mapper-Presets.json` へ追加します。
ファイルが存在しない場合は新規作成し、既に存在する場合は既存Presetを残したまま末尾へ追加します。

## 制限

- 同じ Shape Key を複数の同時適用表情で指定した場合、Animator レイヤー順によって後のレイヤーが優先されます。
- 顔メッシュは `VRCAvatarDescriptor.VisemeSkinnedMesh` を優先し、未設定の場合は指定Shape Keyの一致数から自動検出します。

## ライセンス

このプロジェクトは MIT License で提供されています。詳細は [../LICENSE](../LICENSE) を参照してください。
