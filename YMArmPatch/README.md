# YM Arm Patch

肩、前腕、親指の見た目をビルド時に非破壊で補正する NDMF ツールです。  
オフセットした追加ボーンを Constraint で連動させ、VRoid で起きやすいなで肩、手首まわりのねじれ、開きすぎる親指を抑えます。

通常の Add Component では、非VRoidモデルでも使いやすいようにニュートラルな初期値になっています。  
VRoid 向けの値をまとめて入れたい場合は、Hierarchy のアバター右クリックメニューから `Yoridori Modifiers/Create YM Components Object for VRoid` を使ってください。

## 使い方

1. アバターの Prefab ルート、Armature ルート、または補正したい階層に `Yoridori Modifiers/YM Arm Patch` を追加します。
2. 必要な Fix だけ左端のチェックを有効にします。
3. `Preview` で見え方を確認します。
4. Build 時に NDMF が補正用ボーンと Constraint を追加します。

`Preview` では VRChat SDK 内の Idle モーションを再生して、腕まわりの見え方を確認できます。  
Preview 表示が残ったり、モデルが見えなくなった場合は、`Advanced` の `Reset Preview` を押してください。

## 初期値

通常追加時は以下のように、補正量が入らない状態です。

- `Shoulder Fix`: 無効
- `Forearm Fix`: 無効
- `Thumb Fix`: 無効
- `Euler Offset`: すべて `0`
- `Elbow Scale` / `Wrist Scale`: すべて `(1, 1, 1)`

VRoid 向けの推奨値はプリセットから追加できます。

## VRoid プリセット

Hierarchy でアバターを右クリックし、以下から選択できます。

- `Yoridori Modifiers/Create YM Components Object for VRoid/Long Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/Short Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/Kimono`
- `Yoridori Modifiers/Create YM Components Object for VRoid/(via VRM 0.0) Long Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/(via VRM 0.0) Short Sleeves`
- `Yoridori Modifiers/Create YM Components Object for VRoid/(via VRM 0.0) Kimono`

Arm Patch だけをアバタールートに追加したい場合は、`Yoridori Modifiers/Add Component with VRoid Defaults/YM Arm Patch` から選択できます。

プリセットでは `Shoulder Fix`、`Forearm Fix`、`Thumb Fix` が有効になります。  
VRM 0.0 用プリセットは親指の初期角度が異なります。

`Kimono` では、名前に `body` と `skin` の両方を含むマテリアルを大文字小文字無視で探し、見つかれば Twist Target に設定します。見つからない場合は `Auto` になります。Twist Bone Count は `4` になり、袖の長さ調整用に `Elbow Scale` の長さ方向が少し短くなります。

## 設定

### Shoulder Fix

肩ボーンの見た目を補正します。  
肩の位置自体を大きく変えるものではなく、腕まわりの見え方を整える目的です。

### Forearm Fix

前腕の見た目骨にスケール補正と手首 twist 補正を適用します。  
半袖など腕が見える衣装では `Twist Bone Count` を増やすと安定しやすくなります。
`Elbow Scale` と `Wrist Scale` は X/Y/Z ごとに指定でき、Twist Bone Count が 0 の場合は両者の平均値が使われます。

### Thumb Fix

親指の初期姿勢を補正します。  
右手は内部で自動反転して適用されます。

### Constraint Mode

`VRChat Constraints` と `Unity Constraints` を選べます。  
VRChat 用途では `VRChat Constraints` を推奨します。

Advanced の `肘の形状を優先する` を ON にすると、Twist Bone は TwistAim 配下に作られます。OFF の場合は TwistAim を作らず、LowerArm 直下に Twist Bone を作ります。

### Build Order

Modular Avatar の前後どちらで処理するかを選べます。

- `After Modular Avatar`
  - MA で着せた衣装にも補正を入れやすい設定です。
- `Before Modular Avatar`
  - 生成された Constraint を MA 側の処理に渡したい場合に使います。

## 注意

Constraint の使用数が増えます。設定内容によっては30ほど増加します。 
VRChatでの使用数上限は高いため、基本引っかかることはないと思いますが、ご留意ください。 
補正の都合上、肘付近へしわ寄せが出る場合があります。

VRChat SDK 内のサンプル Idle モーションは、実機でのポーズと完全には一致しない可能性があります。  
Preview が動かない場合は、SDK 側のアセットパス変更が原因の可能性があります。

## ライセンス

このプロジェクトは MIT License で提供されています。詳細は [../LICENSE](../LICENSE) を参照してください。
