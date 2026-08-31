Shader "MRFlood/Magnification Lens Circular Clip"
{
    Properties
    {
        _MainTex ("Surface Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _LensCenterWorld ("Lens Center", Vector) = (0, 0, 0, 1)
        _LensUpWorld ("Lens Up", Vector) = (0, 1, 0, 0)
        _LensRadiusWorld ("Lens Radius", Float) = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry+10"
            "RenderType" = "Opaque"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _LensCenterWorld;
            float4 _LensUpWorld;
            float _LensRadiusWorld;

            struct AppData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float3 lensUp = normalize(_LensUpWorld.xyz);
                float3 offset = input.worldPosition - _LensCenterWorld.xyz;
                float3 planarOffset = offset - lensUp * dot(offset, lensUp);
                clip(_LensRadiusWorld - length(planarOffset));

                fixed4 color = tex2D(_MainTex, input.uv) * _Color;
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
