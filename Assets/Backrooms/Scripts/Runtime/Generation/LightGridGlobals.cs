using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Backrooms/Lit reads the light grid's global textures and occupancy buffer. Unity skips draws whose
    /// shader buffer is unbound or released, so whenever no grid is running (edit mode, other scenes,
    /// room-layer mode) small empty stand-ins are bound and the grid is switched off in the shader.
    /// </summary>
    public static class LightGridGlobals
    {
        static ComputeBuffer s_occ, s_lights, s_tiles, s_tileLights;
        static Texture3D s_tex3D;
        static Texture2D s_tex2D;

        static readonly int GA = Shader.PropertyToID("_LGridA"), GB = Shader.PropertyToID("_LGridB"), GBounce = Shader.PropertyToID("_LGridBounce"),
            GOcc = Shader.PropertyToID("_LGridOcc"), GSize = Shader.PropertyToID("_LGridSize"), GParams = Shader.PropertyToID("_LGridParams");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void BindFallbacks()
        {
            if (s_occ == null || !s_occ.IsValid()) { s_occ = new ComputeBuffer(LightGrid.OccN * LightGrid.OccN * 2, 4); s_occ.SetData(new uint[LightGrid.OccN * LightGrid.OccN * 2]); }
            if (!s_tex3D)
            {
                s_tex3D = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false) { hideFlags = HideFlags.HideAndDontSave, name = "LightGridEmpty3D" };
                s_tex3D.SetPixel(0, 0, 0, Color.clear); s_tex3D.Apply(false, true);
            }
            if (!s_tex2D)
            {
                s_tex2D = new Texture2D(1, 1, TextureFormat.RGBAHalf, false) { hideFlags = HideFlags.HideAndDontSave, name = "LightGridEmpty2D" };
                s_tex2D.SetPixel(0, 0, Color.clear); s_tex2D.Apply(false, true);
            }
            if (s_lights == null || !s_lights.IsValid()) { s_lights = new ComputeBuffer(1, LightGridMath.GridLight.Stride); s_lights.SetData(new LightGridMath.GridLight[1]); }
            if (s_tiles == null || !s_tiles.IsValid()) { s_tiles = new ComputeBuffer(1, 8); s_tiles.SetData(new uint[2]); }
            if (s_tileLights == null || !s_tileLights.IsValid()) { s_tileLights = new ComputeBuffer(1, 4); s_tileLights.SetData(new uint[1]); }
            Shader.SetGlobalBuffer(Shader.PropertyToID("_LGridLights"), s_lights);
            Shader.SetGlobalBuffer(Shader.PropertyToID("_LGridTiles"), s_tiles);
            Shader.SetGlobalBuffer(Shader.PropertyToID("_LGridTileLights"), s_tileLights);
            Shader.SetGlobalVector(Shader.PropertyToID("_LGridFar"), Vector4.zero);
            Shader.SetGlobalBuffer(GOcc, s_occ);
            Shader.SetGlobalTexture(GA, s_tex3D);
            Shader.SetGlobalTexture(GB, s_tex3D);
            Shader.SetGlobalTexture(GBounce, s_tex2D);
            Shader.SetGlobalVector(GSize, new Vector4(LightGrid.GX, LightGrid.GY, LightGrid.GZ, LightGrid.OccN));
            Shader.SetGlobalVector(GParams, Vector4.zero);
        }
    }
}
