
Shader "Hidden/SkinningModule-GUITextureClip"
{
    Properties {
        _MainTex ("Texture", Any) = "white" {}
        _Opacity ("Opacity", Float) = 1
        _VertexColorBlend ("VertexColorBlend", Float) = 0
        _ShowAlpha ("Show Alpha", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
    }

    CGINCLUDE
    #pragma vertex vert
    #pragma fragment frag
    #pragma target 3.0

    #include "UnityCG.cginc"

    struct appdata_t {
        float4 vertex : POSITION;
        fixed4 color : COLOR;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f {
        float4 vertex : SV_POSITION;
        fixed4 color : COLOR;
        float2 texcoord : TEXCOORD0;
        float2 clipUV : TEXCOORD1;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    sampler2D _MainTex;
    sampler2D _GUIClipTexture;

    uniform float4 _MainTex_ST;
    fixed4 _Color;
    uniform fixed _Opacity;
    uniform fixed _VertexColorBlend;
    uniform fixed _ShowAlpha;
    uniform float4x4 unity_GUIClipTextureMatrix;
    uniform bool _AdjustLinearForGamma;

    v2f vert (appdata_t v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.vertex = UnityObjectToClipPos(v.vertex);
        float3 eyePos = UnityObjectToViewPos(v.vertex);
        o.clipUV = mul(unity_GUIClipTextureMatrix, float4(eyePos.xy, 0, 1.0));
        o.color = v.color;
        o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
        return o;
    }

    fixed4 frag (v2f i) : SV_Target
    {
        // Atlas mipmaps mix neighboring sprites and can select different levels
        // across deformed triangles. Preview the base level in both RGB and alpha.
        fixed4 col = tex2Dlod(_MainTex, float4(i.texcoord, 0, 0));

        if (_ShowAlpha > 0.5)
        {
            // White blended onto the black workspace displays the source alpha.
            // Ignore texture RGB, weight colors, tint and preview opacity.
            col.rgb = 1;
            col.a *= tex2D(_GUIClipTexture, i.clipUV).a;
            return col;
        }

        col *= _Color;

        if (_AdjustLinearForGamma)
            col.rgb = LinearToGammaSpace(col.rgb);

        // Weight opacity changes the color overlay, never the sprite coverage.
        // Transparent texels can contain atlas padding RGB that must stay hidden.
        col.rgb = lerp(col.rgb, i.color.rgb, _VertexColorBlend);
        col.a *= tex2D(_GUIClipTexture, i.clipUV).a * _Opacity;
        return col;
    }
    ENDCG

    SubShader {

        Tags { "ForceSupported" = "True" }

        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha, One One
        Cull Off
        ZWrite Off
        ZTest Always

        Pass {
            CGPROGRAM
            ENDCG
        }
    }

    SubShader {

        Tags { "ForceSupported" = "True" }

        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass {
            CGPROGRAM
            ENDCG
        }
    }
}
