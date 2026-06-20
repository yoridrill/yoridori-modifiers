# YM VRoid Skirt Refine

VRoid のワンピース、スカート、ロングコート裾まわりのボーン、ウェイト、PhysBone、PhysBoneCollider、必要に応じた Rotation Constraint を、ビルド時に非破壊で整えるための NDMF ツールです。

前開きコート対応も含みますが、このツールの主軸はスカート調整です。

## 使い方

1. AvatarRoot またはアバター配下の GameObject に `Yoridori Modifiers/YM VRoid Skirt Refine` を追加します。
2. 必要に応じて Inspector の設定を変更します。
3. 通常どおり VRChat SDK からビルドまたはアップロードします。

VRoid アバターでは、Hierarchy のアバターを右クリックして `Yoridori Modifiers/Add Component with VRoid Defaults/YM VRoid Skirt Refine` から追加できます。

同一アバター内で有効に使われる `YM VRoid Skirt Refine` は1つだけです。複数ある場合は Inspector に警告が表示され、ビルド時にはアバターRootに近いものが使用されます。

## 基本方針

- 元Prefabや元メッシュは直接変更せず、NDMFのビルド処理中に非破壊で処理します。
- Preset は Inspector の Settings をまとめて変更するためだけのものです。
- ビルド時の処理は Preset ではなく、Settings 内の各オプションと数値を参照して実行します。
- VRoid標準の揺れボーンが複数セットあるモデルでは、元ボーンセットは全て置き換え対象にします。ただし、このツールが追加するボーンセットは代表1セット分だけです。
- Inspector上部に、今の設定で増えるRotation Constraint数と、下半身のPhysBone/PhysBoneCollider数の処理前後見積もりを表示します。

## 自動検出

Inspector を開いたとき、未指定の Bone Extension 枠には VRoid 標準名から検出したボーンが初期値として入ります。検出は大文字小文字を区別しない部分一致です。

ワンピースは Hips 配下の `L_SkirtSide` / `R_SkirtSide` と、左右 UpperLeg 配下の `L_SkirtFront` / `L_SkirtBack` / `R_SkirtFront` / `R_SkirtBack` を探します。

ロングコートは左右 LowerLeg 配下の `L_CoatSkirtFront` / `L_CoatSkirtSide` / `L_CoatSkirtBack` / `R_CoatSkirtFront` / `R_CoatSkirtSide` / `R_CoatSkirtBack` を探します。

新規追加またはコピー直後にボーンを自動検出できた場合のみ、Refine が自動で有効になります。このときの初期Presetは以下です。

- Long Coat Refine: `ロングスカート重め`
- One-Piece Refine: Long Coat Refine が有効なら `ロングコートに合わせる`
- One-Piece Refine: Long Coat Refine が無効なら `ショートスカート軽め`

手動でRefineをオン/オフした場合、Presetは自動で切り替わりません。

## One-Piece Refine

ワンピースやスカート裾まわりを調整します。

### Preset

以下から選択できます。

- `ショートスカート軽め`
- `ショートスカート重め`
- `スリムロングスカート軽め`
- `スリムロングスカート重め`
- `ロングスカート軽め`
- `ロングスカート重め`
- `ロングコートに合わせる`

Presetを選ぶと、Bone Extension、Rotation Constraint、PhysBone、PhysBone ColliderなどのSettingsに推奨値が反映されます。

### Bone Extension

Front-Left、Front-Right、Side-Left、Side-Right、Back-Left、Back-Right の6房分のボーンを指定します。

`ボーン追加` を有効にすると、既存チェーンの先端側へボーンを追加し、基本的に6段構成へ揃えます。ショートスカートPresetでは初期値として無効になります。

`ボーン追加` を無効にした場合も、PhysBoneの統合Root化とボーン軸の正規化は行います。

`Hipのウェイトを弱める` では、対象頂点のHipウェイトを揺れボーン側へ移す割合を調整できます。

### Rotation Constraint

`正面の付け根に使用` を有効にすると、Frontの1段目をUpperLegにRotation Constraintで連動させ、Frontの2段目以降は房ごとのPhysBoneで揺らします。Frontは統合RootのPhysBoneから除外されます。

### PhysBone Collider

`UpperLegに追加`、`LowerLegに追加` を有効にすると、左右の脚ボーン配下にカプセルPhysBoneColliderを追加します。追加前に、左右UpperLeg/LowerLeg配下の既存PhysBoneColliderは削除されます。

`床を追加` を有効にすると、アバタールート直下にPlane形状の床PhysBoneColliderを追加し、ワンピースの揺れボーンへ設定します。Planeは無限平面として扱われるため、Scaleは設定しません。

### PhysBone

生成される統合Root、またはRotation Constraint下の房ごとのPhysBoneに適用するSimplified設定です。表示される数値やCurveを編集できます。

## Long Coat Refine

ロングコート裾まわりを調整します。VRoidのロングコートは膝下から揺れボーンが始まるため、根本側へボーンを追加します。

### Preset

以下から選択できます。

- `ショートスカート軽め`
- `ショートスカート重め`
- `ロングスカート軽め`
- `ロングスカート重め`
- `前開き`
- `ワンピースに合わせる`

Presetを選ぶと、Bone Extension、Rotation Constraint、PhysBone、PhysBone ColliderなどのSettingsに推奨値が反映されます。

### Bone Extension

Front-Left、Front-Right、Side-Left、Side-Right、Back-Left、Back-Right の6房分のボーンを指定します。

`ボーン追加` を有効にすると、既存チェーンの根本側へボーンを追加し、基本的に6段構成へ揃えます。Front / Back の2段チェーンと Side の3段チェーンの違いを考慮して、不足分だけ根本側へ追加します。

`下3段のボーンを除く` を有効にすると、ショートスカート向けに追加Root3本だけを使い、既存のロングコート揺れボーンを削除します。

`Frontを外側へずらす` は、前開きコート向けにFrontの揺れボーンを外側かつ少し後ろへずらします。

`付け根を上へずらす` は、追加Rootの高さをUpperLeg付近へ補正する量です。`-1` で補正なし、`0` でUpperLegの高さ、`1` でUpperLegより生成UpperLegコライダー半径分上、`2` でその2倍上へ揃えます。

`Hipのウェイトを弱める` では、対象頂点のHipウェイトを揺れボーン側へ移す割合を調整できます。

`Spineのウェイトを弱める` では、対象頂点のSpineウェイトを揺れボーン側へ移す割合を調整できます。前開きコートをより上の位置から揺らしたい場合に使用します。

### Rotation Constraint

`正面の付け根に使用` を有効にすると、Frontの1段目をUpperLegにRotation Constraintで連動させ、Frontの2段目以降は房ごとのPhysBoneで揺らします。Frontは統合RootのPhysBoneから除外されます。

`上3段のボーンに使用` を有効にすると、追加された上3段のうち1段目をUpperLeg、3段目をLowerLegにRotation Constraintで連動させ、下3段を房ごとのPhysBoneで揺らします。この場合も統合Rootは作成しますが、統合RootにPhysBoneは付けません。

`FrontのLimitsを正面に向ける` を有効にすると、Frontに個別追加されるPhysBoneのLimitsだけを正面寄りに向けます。統合RootのPhysBoneには適用されません。

### PhysBone Collider

`UpperLegに追加`、`LowerLegに追加` を有効にすると、左右の脚ボーン配下にカプセルPhysBoneColliderを追加します。追加前に、左右UpperLeg/LowerLeg配下の既存PhysBoneColliderは削除されます。

### PhysBone

生成される統合Root、またはRotation Constraint下の房ごとのPhysBoneに適用するSimplified設定です。表示される数値やCurveを編集できます。

## Match

`ロングコートに合わせる` / `ワンピースに合わせる` では、合わせる相手の揺れボーンへウェイトを移し、元の揺れボーンを削除します。

両方を合わせる設定にした場合や、合わせる相手のRefineが無効な場合は動作しません。Inspectorに警告が表示されます。

## Advanced

- `Constraint Mode`
  - 追加するRotation Constraintを `VRChat Constraints` または `Unity Constraints` から選べます。
- `Verbose Log`
  - ビルド時の検出ログを詳しく出力します。

## ビルド順

NDMFでは `YM Arm Patch` の後、`YM Mesh Trimmer` の前に実行されます。  
VRCQuestToolsが導入されている場合は、VRCQuestToolsのQuest変換より前に実行されます。

## VRCQuestToolsとの併用

VRCQuestToolsのQuest変換で `Remove Avatar Dynamics` を有効にしている場合、`YM VRoid Skirt Refine` がビルド時に生成したPhysBoneとPhysBoneColliderもVRCQuestTools側の削除対象になります。

`VQTのKeepリストに追加する` を有効にすると、生成したPhysBoneとPhysBoneColliderをVRCQuestToolsの `PhysBones to Keep` / `PhysBone Colliders to Keep` へビルド時に追加します。VRCQuestToolsへの依存は追加せず、対象アバターにVRCQuestToolsの `AvatarConverterSettings` がある場合だけ動作します。

追加後のKeepリスト数がQuestの制限を超える場合や、VRCQuestToolsが見つからない場合は、このオプションは動作しません。

## ライセンス

このプロジェクトは MIT License で提供されています。詳細は [../LICENSE](../LICENSE) を参照してください。
