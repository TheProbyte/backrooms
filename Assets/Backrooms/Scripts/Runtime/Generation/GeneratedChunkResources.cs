using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Owns only meshes created for this chunk, never imported/shared prop assets.</summary>
    [ExecuteAlways]
    public sealed class GeneratedChunkResources : MonoBehaviour
    {
        readonly List<Mesh> _meshes = new List<Mesh>();
        public int MeshCount => _meshes.Count;
        public Mesh Own(Mesh mesh) { _meshes.Add(mesh); return mesh; }
        void OnDestroy() => Release();
        public void Release()
        {
            foreach (var mesh in _meshes)
                if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            _meshes.Clear();
        }
    }
}
