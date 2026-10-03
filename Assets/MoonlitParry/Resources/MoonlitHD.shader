// Unlit, premultiplied-alpha sprite/mesh shader for the HD art: supports a white hit flash (_Flash)
// and works with SpriteRenderer (vertex colour = SpriteRenderer.color) as well as MeshRenderer (ribbons, terrain).
// Written without a LightMode tag so both the Built-in pipeline and URP (2D and Universal renderers) draw it.
Shader "MoonlitParry/HD"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Flash ("Flash", Range(0, 1)) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float _Flash;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord);
                c.rgb = lerp(c.rgb, fixed3(1, 1, 1), _Flash);
                c *= i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
