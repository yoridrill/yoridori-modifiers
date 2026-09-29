Shader "Hidden/Yoridori Modifiers/Outline Extender Read Main Mip"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Mip;
            float4 frag(v2f_img i) : SV_Target
            {
                return tex2Dlod(_MainTex, float4(i.uv, 0, _Mip));
            }
            ENDCG
        }
    }
}
