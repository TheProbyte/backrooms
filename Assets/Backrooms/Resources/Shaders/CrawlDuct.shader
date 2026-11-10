// Inside of a crawl hole's duct (see CrawlHole). The duct overlaps the room behind the wall, so it is only drawn where
// the hole's stencil mask marks the screen (seen through the hole), or everywhere while the player is inside it.
// Lit by its two openings only: light falls off with depth from each end, so the middle of the crawl is dim. The
// room behind's lights never reach it.
// - Entrance: each point is shaded with the game's full lighting (Backrooms/Lit: real lights and their shadows, the
//   light grid, contact occlusion, SSAO) evaluated at the matching point of the opening, 2 cm out in the room. At the
//   lip that is exactly the room floor's shading, so there is no line where the duct starts.
// - Far end: the light measured just outside the partner hole (CrawlPortalSystem samples it from the light grid
//   every frame), in the directions each surface faces after the trip, so it matches the room the window shows.
// Ducts without a sample use the plain default light below for the far end.
Shader "Backrooms/CrawlDuct"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0
        [Toggle] _ObjectUV ("Sheet-metal mapping (1.1 m sections along the duct, one face across)", Float) = 0
        _EntryLight ("Default light at the entrance (unused while lit)", Color) = (0.5, 0.47, 0.4, 1)
        _ExitLight ("Default light at the far end", Color) = (0.5, 0.47, 0.4, 1)
        _Ambient ("Ambient", Float) = 0.02
        _Falloff ("Falloff depth (m)", Float) = 0.55
        _DuctLength ("Length (m)", Float) = 2.2
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil comparison", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-400" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            ZClip Off
            Stencil { Ref 64 ReadMask 64 Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DuctVertex
            #pragma fragment DuctFragment
            // The same lighting setup as Backrooms/Lit's forward pass (see the keyword notes there).
            #define _CLUSTER_LIGHT_LOOP 1
            #define _LIGHT_LAYERS 1
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // Screen-space AO is read at the opening's screen position (see DuctFragment). It is only right where that
            // position shows the room's floor in front of the lip: the floor, seen from outside. Elsewhere (the duct's walls
            // and ceiling, whose opening points show whatever is through the hole; or from inside) the duct has none.
            static bool g_DuctUseAO;
            void DuctAO(inout AmbientOcclusionFactor ao)
            {
                if (!g_DuctUseAO) { ao.directAmbientOcclusion = 1; ao.indirectAmbientOcclusion = 1; }
            }
            #define BACKROOMS_AO_HOOK(ao) DuctAO(ao)
            #include "Assets/Backrooms/Shaders/BackroomsLitForwardPass.hlsl"

            half4 _EntryLight, _ExitLight;
            float _ObjectUV;
            half _Ambient, _Falloff, _DuctLength;
            // Rows 2i / 2i+1: light arriving at this duct's mouth / at its partner's mouth, six axis directions (+x -x +y -y +z -z).
            TEXTURE2D(_DuctMouthLight);
            float _DuctMouth;        // which pair of rows (per renderer); < 0: use the default light at the far end
            float4 _DuctExitRot;     // cos, sin of the turn from this hole to its partner (yaw about +y)

            struct DuctAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct DuctVaryings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2; float3 normalWS : TEXCOORD3; float3 mouthWS : TEXCOORD4; half fogFactor : TEXCOORD5;
                float4 mouthScreen : TEXCOORD6; float3 normalOS : TEXCOORD7;
            };
            DuctVaryings DuctVertex(DuctAttributes i)
            {
                DuctVaryings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.positionOS = i.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.normalOS = i.normalOS;
                // The same point of the opening, just outside in the room (object +z runs into the wall).
                o.mouthWS = TransformObjectToWorld(float3(i.positionOS.xy, -0.02));
                o.mouthScreen = ComputeScreenPos(TransformWorldToHClip(o.mouthWS));
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half Mouth(float depth) { float k = 1.0 + max(depth, 0.0) / _Falloff; return 1.0 / (k * k); }
            // Light from the cached axis samples for a surface facing N (same weighting as PortalCachedIncident).
            half3 MouthLight(int row, half3 N)
            {
                half3 w = N * N;
                return LOAD_TEXTURE2D(_DuctMouthLight, int2(N.x >= 0 ? 0 : 1, row)).rgb * w.x +
                       LOAD_TEXTURE2D(_DuctMouthLight, int2(N.y >= 0 ? 2 : 3, row)).rgb * w.y +
                       LOAD_TEXTURE2D(_DuctMouthLight, int2(N.z >= 0 ? 4 : 5, row)).rgb * w.z;
            }
            half4 DuctFragment(DuctVaryings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float z = i.positionOS.z;                         // metres into the duct from the entrance
                half3 N = normalize(i.normalWS);
                float2 uv = i.uv;
                if (_ObjectUV > 0.5)
                {
                    // Sheet metal: u runs along the duct in 1.1 m sections (joints at the entrance, the middle and the
                    // window), v across each 0.8 m face; the end cap shows one face.
                    float3 p = i.positionOS, a = abs(i.normalOS);
                    uv = a.z > 0.5 ? float2(p.x, p.y) / 0.8 + 0.5 : float2(p.z / 1.1, (a.x > 0.5 ? p.y : p.x) / 0.8 + 0.5);
                }
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * _BaseColor.rgb;

                // Entrance: the room's own shading at the opening, as Backrooms/Lit computes it there.
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo; surface.smoothness = _Smoothness; surface.metallic = _Metallic; surface.occlusion = 1; surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);
                InputData data = (InputData)0;
                data.positionWS = i.mouthWS;
                data.positionCS = i.positionCS;
                data.normalWS = N;
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Screen position of that point of the opening: SSAO and the Forward+ light list are read there, so the
                // lip gets the room floor's own AO. (Inside the duct, when the opening is behind you, the duct's own.)
                float2 mouthUV = i.mouthScreen.xy / max(i.mouthScreen.w, 1e-5);
                bool onScreen = i.mouthScreen.w > 0 && all(mouthUV >= 0) && all(mouthUV <= 1);
                data.normalizedScreenSpaceUV = onScreen ? mouthUV : GetNormalizedScreenSpaceUV(i.positionCS);
                float3 eyeOS = TransformWorldToObject(GetCameraPositionWS());     // object +z runs into the wall
                g_DuctUseAO = onScreen && N.y > 0.5 && eyeOS.z < -0.05;
                data.shadowMask = half4(1, 1, 1, 1);
                data.bakedGI = SampleSH(N) + BackroomsLightGrid(i.mouthWS, N);
                half3 entry = BackroomsFragmentPBR(data, surface).rgb;

                // Far end: the partner room's light, looked up in the directions this surface faces after the trip.
                half3 exitLight;
                [branch] if (_DuctMouth >= 0)
                {
                    half3 Np = half3(N.x * _DuctExitRot.x + N.z * _DuctExitRot.y, N.y, -N.x * _DuctExitRot.y + N.z * _DuctExitRot.x);
                    exitLight = MouthLight((int)_DuctMouth * 2 + 1, Np) * (0.96 * (1 - _Metallic));   // diffuse share, as the BRDF
                }
                else exitLight = _ExitLight.rgb * lerp(0.8, 1.15, saturate(i.normalWS.y * 0.5 + 0.5));

                // Each end's light falls off into the duct; the faint ambient only fills the middle. At the entrance lip the
                // result is the room's own shading and nothing else (the room is dark in places, so even a faint extra
                // term shows there as a line).
                half inEntry = Mouth(z), inExit = Mouth(_DuctLength - z);
                half3 color = entry * inEntry + albedo * (exitLight * inExit * (1 - inEntry) + _Ambient * (1 - inEntry) * (1 - inExit));
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        // Used only while the player is inside (the material is then in the opaque queue), so depth-based effects see the duct.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back ZClip Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back ZClip Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
