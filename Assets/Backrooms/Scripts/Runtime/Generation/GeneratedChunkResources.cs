using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Owns only meshes created for this chunk, never imported/shared prop assets.</summary>
    [ExecuteAlways]
    public sealed class GeneratedChunkResources : MonoBehaviour
    {
        [SerializeField, HideInInspector] List<Mesh> _meshes = new List<Mesh>();
        public int MeshCount => _meshes.Count;
        public Mesh Own(Mesh mesh) { _meshes.Add(mesh); return mesh; }
        /// <summary>A mesh this chunk no longer draws (its walls were rebuilt): destroyed now.</summary>
        public void Drop(Mesh mesh)
        {
            if (!mesh || !_meshes.Remove(mesh)) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
        void OnDestroy() => Release();
        public void Release()
        {
            foreach (var mesh in _meshes)
                if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            _meshes.Clear();
        }
    }
}
