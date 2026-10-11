Shader "Hidden/Uniskin/SceneWeightsPreview"
{
    Properties { _MainTex ("Sprite", 2D) = "white" {} _Overlay ("Overlay", Float) = 0 _Tint ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float _Overlay; float4 _Tint;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Output { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Output vert(Input v) { Output o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            float4 frag(Output i) : SV_Target
            {
                float4 tex = tex2D(_MainTex, i.uv);
                return float4(lerp(tex.rgb * _Tint.rgb, i.color.rgb, _Overlay), tex.a * _Tint.a);
            }
            ENDCG
        }
    }
}
