# YM Outline Extender

Main TextureのCutoutによって生じる内部境界へ、元MaterialのOutline色と幅を基準に線を追加するビルド専用Modifierです。1つのコンポーネント内でMaterialごとに次の方式を選べます。

- **シェーダー方式**: lilToon Custom ShaderとSDF入りMain Textureを使用します。
- **描き込み方式**: 通常のColor + Alpha Main Textureへ線を焼き込み、元Shaderを維持します。

InspectorはMaterialごとに「Shader」「Draw」「Fold」のボタン（ShaderとDrawを連結し、Foldは独立）を並べた1つの一覧です。シェーダーと描き込みは排他選択で、両方OFFにするとカットアウトへの追加線を無効にできます。折り返しは独立して併用できます。既存の自動選択は後方互換性のためシェーダー方式だけに適用され、描き込みと折り返しは手動選択です。「太さ比率」と元MaterialのOutline Color / Widthは各方式で共通です。

YM Hair Look Kitの髪マテリアル結合が有効な場合、結合対象の代表Material以外はグレーアウトします。結合後には代表Materialの設定を使用し、結合を解除すると各Materialの保存済み設定へ戻ります。Info Boxは一覧の上に表示します。Inspectorの通常描画ではRenderer走査・Material検証・ポリゴン推計を繰り返さず、設定や依存Assetが変わったときに更新します。ポリゴン推計結果はInspectorを閉じても再利用し、実際のFold対象・対象Mesh・線幅・Alpha判定・透明部補完に使う入力が変わると再計算します。Fold対象が変わらない髪結合の代表Material変更や、陰影・輪郭線色など形状と無関係な変更では再計算しません。補完画像を共有する別MeshのUV配置は補完範囲に影響するため監視します。結果だけを最大32件保持し、生成MeshやTextureは保持しません。ドメインリロード時にキャッシュはクリアされます。

## シェーダー方式

標準フルlilToon Cutout / Multi Cutoutが対象です。Main Textureの実効Cutoff境界からMarching SquaresでユークリッドSDFを生成し、RGBは入力Textureを維持、Alphaへ `0.5 + signedDistance / 16` として±8基底テクセルを保存します。派生Materialだけを `_Cutoff = 0.5`、`_Color.a = 1` に変更し、元Assetは変更しません。

Custom Shaderは `Hidden/yoridrill/lilToonOutlineExtender/CutoutOutline` です。追加Texture Sampleは行いません。画面幅変換にはSDFの微分を使用せず、lilToonが計算済みの `fd.ddxMain` / `fd.ddyMain` と `_MainTex_TexelSize.zw` からMain UVのtexel footprintを求めます。

```hlsl
float2 dxTexel = fd.ddxMain * _MainTex_TexelSize.zw;
float2 dyTexel = fd.ddyMain * _MainTex_TexelSize.zw;
float trace = dot(dxTexel, dxTexel) + dot(dyTexel, dyTexel);
float determinant = dxTexel.x * dyTexel.y - dxTexel.y * dyTexel.x;
// trace / determinantからsMaxとsMinを解析的に計算
sMin = max(sMin, sMax / maxAnisotropy);
float texelsPerPixel = sqrt(sMax * sMin);
```

Main UV Jacobianの特異値を求め、`sMin >= sMax / 8`へ制限してから幾何平均を等方footprintとして使用します。SDF gradient方向を使った補正は行いません。`fwidth(screenDistance)` と `smoothstep` によるAA、保存範囲端の除外、シルエット付近のrange gateは維持します。VRChatでSafety/Fallback Shaderへ置換された場合、追加線は表示されませんが元Cutout境界は維持されます。

### Mesh Boundary Fold

シェーダー / 描き込みとは独立してMaterialを手動選択できます。選択したSubMeshのopen boundaryからMesh内部へ戻る短いreturn flapを同じSubMesh内へビルド時に追加し、最終Shader自身の通常Outline Passへ描画を任せます。FoldEndには生成後Triangleの実face normalを設定し、折り目では元Surfaceの再計算Normalとflap normalを平均して、派生Meshの元Surface頂点とRow0をsoft edge化します。対象外の頂点と元Assetは変更しません。Material SlotとDraw Callは増えません。Outlineが無効な最終Materialは安全にスキップします。

return flapは元Surfaceと共有境界を逆向きにたどるTriangle windingで生成します。折り目Normalは開放端の外側（スカートの裾なら下方向）を向き、横から見ても膨張後のflap背面が通常Outlineで描かれます。新規Componentの標準設定は角度5°・長さ2W・陰影帯幅0.5Wです。数値設定はInspectorに表示しません。保存済みの数値は維持します。lilToon、legacy MToon、MToon10の既存Outlineを利用し、Shader変換や頂点カラーへのNormal書き込みは行いません。

折り目Normalによる通常描画の陰影変化を局所化するため、開放端から内側に元のSurface上の頂点列を追加します。境界Edgeの隣接Triangleでは平行なinset線、境界頂点だけを共有するTriangleでは小さなcorner切断を使い、内側の頂点には元のNormalを補間します。共有Edgeの切断位置はUV seamやMaterial境界をまたいで伝播し、隣接TriangleのT-junctionを防ぎます。UV・Color・Tangent・Bone Weight・BlendShapeは元Triangleから補間します。細いTriangleでは帯幅を縮めて内側の領域を確保します。陰影帯幅0で従来の頂点列なしの折り返しと比較できます。PreviewのON/OFFで元の陰影と比較できます。MToonのScreen Coordinates幅はObject Space近似によるFold形状になるため、距離ごとの見え方を確認してください。

メッシュ境界のInfo Boxには現在の設定による `対象のポリゴン数: 〇〇→〇〇 (+○)` を表示します。選択Materialを含む対象Mesh全体のTriangle数を数え、折り返し・陰影帯・隣接SubMeshの共有辺を保つ分割も増加分に含めます。設定変更中は計算をまとめて更新し、元Meshは変更しません。

開放端の両端の入り抜きは初期状態で有効で、Inspectorには表示しません。選択された開放端を物理的な位置でつなぎ、元Meshに3頂点以上ある連続列を対象にします。Main Textureの実効Cutoffと交差する位置にも頂点を追加し、透明部分にある元の端点ではなく、実際に見える列の両端でreturn flapの奥行きをゼロにして一点に絞ります。Cutoff位置はGPUのテクセル中心に合わせたAlpha補間で求め、共有辺の切断も隣接Triangleへ伝播します。

通常面・Row0・FoldEndの端点Normalを境界に沿って列の内側へ向け、Outline膨張後も端点が一致するようにします。端の断面を残さず、隣接頂点との補間で輪郭線が入り抜きのように細くなります。潰れた端のTriangleは生成せず、Info Boxの増加数にも反映します。1本の辺の途中に不透明な区間がある場合は、両端だけで線全体が消えないよう中央の頂点も追加します。変更後のNormalに合わせてTangentを直交化し、lilToonのOutline Vector計算を維持します。

カットアウトで途切れない閉ループ・分岐した境界・元Meshに2頂点しかない列は除外します。陰影帯やCutoff位置への頂点追加後も、この除外判定は元Meshを基準に維持します。UV / Material分割の継ぎ目は、選択された開放端が続いていれば端点にしません。

### 開放端付近の透明部分を補完

折り返し対象Materialの開放端とCutout境界がわずかに離れている場合、Main Textureの不透明部分を開放端まで延長できます。折り返しが有効な場合に自動で補完します。個別のON/OFFと距離の数値設定はInspectorに表示しません。補完する距離の既定値は1Wです。以前保存された補完OFFの値も、折り返しが有効なら自動補完へ移行します。Wは最終MaterialのOutline Widthに「太さ比率」を反映した値です。近くの意図的な切れ込みも補完される場合があるため、Previewで比較してください。

元Meshの各開放Edgeに沿ってサンプリングし、隣接Triangle上で内側へ指定距離以内に不透明部分が見つかった場所だけ補完します。UVからMesh上の距離への変換を使い、Texture解像度・UV密度・Scale / Offset・Repeat / Clampを反映します。補完色は同じ元Triangleの不透明側から引き継ぎ、追加部分のAlphaを1にします。元の不透明テクセルと離れた穴は維持します。UVの外側には2テクセル分の補間用余白を加え、別のUV領域に属するテクセルは変更しません。

補完は折り返し・端点の入り抜き・シェーダー / 描き込み方式の判定より先に行います。Main Textureと同じ画像・UV変換を使っているShade TextureやlilToonのOutline Textureも派生画像へ置き換えます。MToonのShade TextureとlilToonのShadow Color TextureはShaderがMain UVを共有するため、そのUVに合わせて同期します。ShaderやMaterialの設定は維持し、lilToon・legacy MToon・MToon10で利用できます。元Assetは変更しません。派生画像にはmipmapを再生成し、通常のColor + AlphaとしてYM Texture Registryへ登録します。Previewの補完画像は再利用し、終了時に破棄します。Info Boxのポリゴン数は補完後の折り返し形状を数えます。

## 描き込み方式

lilToon、VRM 1.0 MToon10、legacy `VRM/MToon` をサポートします。通常のCutout用Main Texture、Cutoff、Outline Color / Widthを公開する他のShaderも手動選択できます。BakeのためにlilToonへ変換せず、最終MaterialのShaderと設定を維持した派生Materialを作ります。

最終Textureは通常のColor + Alphaです。元Cutoutの不透明側はRGB / Alphaを変更せず、透明側だけをOutline Colorへ置き換えます。外周Alphaは0.5 texelの遷移幅で元Materialの実効Cutoffを50% coverageとして横切り、完全被覆部分はAlpha 1になります。SDFは境界距離を得る一時データであり、成果物Alphaへ保存しません。そのためFallback ShaderでもMain TextureのCutoutが使われる限り、追加線が残る場合があります。

幅は各RendererのPosition、UV0、Main Texture解像度、Scale / Offsetから求めます。各UV三角形について `UV → avatar local space` Jacobian (`dP/du`, `dP/dv`) を計算し、SDFから得たTexture-space境界法線方向へ1 texel進んだときのObject Space距離で、元OutlineのObject Space幅を割ります。BakeではAAや元の不透明領域との重なりを考慮してObject Space幅へ2倍の補正を適用し、重複UVの細い側を選択した後も最低1 texelを保証します。U/Vで異なる伸びには方向別に対応します。

同じtexelを複数surfaceが共有する場合、必要texel幅の最小値を採用します。これは重複UVの一部で過剰に太い線が発生することを避けるための意図した仕様です。三角形は半テクセル許容で隣接領域を重ね、共有境界でも細い側を優先します。UV islandをまたぐ平滑化は行いません。

## 処理順とTexture

`Mesh Trimmer → TexTransTool Transforming → MToon to lilToon → Hair Look Kit → Outline Extender → TexTransTool Optimizing → YM Texture Finalizer → AAO / Quest Tools`

最終RendererのMaterialとUVを読み、NDMF Object RegistryでInspector上の元Materialから派生Materialを追跡します。シェーダー方式は `SignedDistanceField`、描き込み方式は `ColorWithAlpha` としてYM Texture Registryへ登録します。各処理内では圧縮せず、Finalizerが現在も参照される生成TextureだけをPC / Android向けに一度圧縮します。

シェーダー方式のAAO Shader Informationは既存のOptional Integrationを使用します。描き込み方式は元Shaderを維持するため専用Shader Informationを必要としません。TTT / AAOは必須依存ではありません。

## Preview

Previewは元アバターを複製し、必要な場合だけ `YM MToon to lilToon → YM Outline Extender` の順で処理します。Hair Look Kitは含めません。Shader対象とBake対象を同じPreviewへ同時に反映します。Shader SDFとBake結果は別Cacheで管理し、Bake側は色や幅を変更しても入力Texture・Cutoff・UVが同じ一時distance fieldを再利用します。

## 制限

- シェーダー方式はLite / Fur / Tessellation / Optional / 他製品のCustom lilToon Shaderを安全にスキップします。
- Alpha Mask、Main 2nd/3rdによるAlpha変更、Dissolve、Dither、Parallax、UDIM discard、AudioLinkによるAlpha変更は対応外です。
- AnimatorでMaterial、Texture、Alpha設定を変更するRendererは保守的にスキップします。
- Bakeではカメラ依存のlilToon `_OutlineFixWidth` を再現しません。MToonのScreen Coordinates幅も既存Converterと同じ数値基準をObject Space近似として使用します。
- Bake対応外Shaderは理由付きでInspector表示し、ビルド時にも再検証して安全にスキップします。
