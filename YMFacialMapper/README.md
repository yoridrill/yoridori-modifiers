# YM Facial Mapper

BlendShape（Shape Key）の名前を指定して、VRChatのハンドサインに表情を割り当てるNDMFツールです。VRoidやMMDなど、共通のShape Key名を持つアバター間で設定を共有できます。

## 使い方

1. アバター配下に `Yoridori Modifiers/YM Facial Mapper` を追加します。
2. プリセットを選ぶか、各ハンドサインにShape Key名を入力します。
3. 名前の隣でウェイト（0〜100、初期値100）を調整します。
4. 必要に応じてEyelid／Visemeと左右の優先順位を設定します。

ビルド時に既存のジェスチャー表情を置き換えます。対象はGesture Controllerと、Gestureパラメータで動くFXレイヤーのうち、非0のBlendShapeカーブを含むレイヤーです。0固定の補正レイヤーは残し、対象レイヤー内のリセット用カーブは表情と一緒に除去します。

顔メッシュはAvatar Descriptorの `VisemeSkinnedMesh` を優先し、未設定の場合はShape Key名の一致数から選びます。

## 表情の組み合わせ

左右の表情は同時に適用できます。Eyelid／VisemeをONにすると、そのグループを使う表情同士が排他になります。

| 設定 | 表情中の動作 |
| --- | --- |
| Eyelid-L／Eyelid-R | 標準の目線・まばたきを停止 |
| Viseme | 標準の口パクを停止 |
| すべてOFF | 排他なし |

Eyelid-L／Rは左右の排他グループを区別する設定です。トラッキング停止は目全体に適用されます。

同じグループが競合した場合は、`Conflict Resolution` で指定した手を優先します（初期値は右手）。同じShape Keyに異なるウェイトを指定した場合も、この優先順位に従います。

## プリセット

同梱の `Presets.json` と `Assets/YM-Facial-Mapper-Presets.json` を読み込みます。プリセットを選ぶと、表情設定とメモが反映されます。

`Export` で名前を付けると、現在の設定とメモをユーザープリセットへ追加できます。メモには想定アバターや割り当て方などを記録できます。

## 外部ツールとの併用

Modular Avatarの処理後に、置き換えた元FXレイヤーへのLayerControlを検出し、YMの表情にも停止・復帰を引き継ぎます。抑制中はハンドサインによるTrackingControlを止め、解除時は現在のハンドサインへ復帰します。NK Installerのジェスチャー抑制も、この仕組みで連携します。

継承対象はWeight 0／1のON・OFF制御です。中間Weightや同一State内で競合する指定は対象外で、元のフェード時間は引き継ぎません。複数Stateからの制御は、最後の書き込みが優先されます。

抑制時のEyes設定は、Gesture依存レイヤー以外のFX Stateが指定した外部設定へ戻します。記録がない場合はTrackingを使います。連携にはAnimator内部Boolを使用し、Expression Parametersは増えません。

継承元が見つからないLayerControlはInspectorとビルド時に警告します。InspectorはMA統合前の事前診断です。小物など、表情以外のレイヤーを操作するLayerControlなら対応は不要です。

### Jerry’s Templates

公開パラメータ `FacialExpressionsDisabled` にも対応します。Boolはtrue、Intは非0、Floatは0.5より大きい値で抑制します。LayerControlによる抑制と併存する場合は、どちらかが有効な間、YMの表情を停止します。

## ライセンス

[MIT License](../LICENSE)
