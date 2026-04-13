#if UNITY_EDITOR
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

public static class IslandUvMeshProcessor
{
    private const ushort IgnoredIslandId = 0xFFFF;
    private const int MaxIslands = 0xFFFF; // 65535 valid ids (0..65534). 0xFFFF is reserved.

    private struct Edge : IEquatable<Edge>
    {
        public int a, b;
        public Edge(int a, int b)
        {
            this.a = Mathf.Min(a, b);
            this.b = Mathf.Max(a, b);
        }
        public bool Equals(Edge other) => a == other.a && b == other.b;
        public override bool Equals(object obj) => obj is Edge other && Equals(other);
        public override int GetHashCode() => a.GetHashCode() ^ (b.GetHashCode() * 397);
    }

    private struct Tri
    {
        public int i0, i1, i2;
        public Vector3 normal;
        public int subMesh;
    }

    private struct IslandBasis
    {
        public Vector3 origin;
        public Vector3 T;
        public Vector3 B;
        public Vector2 uvMin;
        public Vector2 uvMax;
    }

    private static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * Vector3.Dot(v, n);

    // IslandId is encoded into the SAME UV channel as islandUV, in the zw components as two bytes (0..1).
    // 16-bit little endian: id = lo + hi*256.
    private static Vector2 EncodeIslandId16ToZW(ushort id16)
    {
        float lo = (id16 & 0xFF) / 255.0f;
        float hi = ((id16 >> 8) & 0xFF) / 255.0f;
        return new Vector2(lo, hi);
    }

    private static Vector3 ComputeTriVertexNormal(Vector3 n0, Vector3 n1, Vector3 n2)
    {
        Vector3 n = n0 + n1 + n2;
        if (n.sqrMagnitude <= 1e-12f) return Vector3.up;
        return n.normalized;
    }

    private static void AddEdgeToMap(int a, int b, int triIndex, Dictionary<Edge, List<int>> edgeToTris)
    {
        var edge = new Edge(a, b);
        if (!edgeToTris.TryGetValue(edge, out var list))
        {
            list = new List<int>();
            edgeToTris[edge] = list;
        }
        list.Add(triIndex);
    }

    public static void ProcessMesh(Mesh mesh, IslandUvSettings.Settings s)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (s == null) throw new ArgumentNullException(nameof(s));
        new Session(mesh, s).Run();
    }

    private sealed class Session
    {
        private readonly Mesh _mesh;
        private readonly IslandUvSettings.Settings _s;

        private Vector3[] _vertices;
        private Vector3[] _srcNormals;
        private bool _hasNormals;

        private int _subMeshCount;
        private int[][] _subMeshTriangles;
        private int _triCount;
        private Tri[] _tris;
        private List<List<int>> _triNeighbors;
        private int[] _triIsland;
        private int _islandCount;

        private IslandBasis[] _islandBasis;
        private bool[] _ignoredIsland;

        private List<Vector3> _newVertices;
        private List<Vector3> _newNormals;
        private List<Vector4> _newTextUV;
        private Dictionary<(int v, int i), int> _vertexMap;
        private List<int>[] _newTrianglesBySubMesh;

        public Session(Mesh mesh, IslandUvSettings.Settings s)
        {
            _mesh = mesh;
            _s = s;
        }

        public void Run()
        {
            PrepareInput();
            BuildTriangles();
            BuildAdjacency();
            ClusterIslands();

            // islandId is stored as 16-bit, with 0xFFFF reserved for ignored islands.
            // So valid island ids are 0..65534, i.e. at most 65535 islands.
            // If we exceed this, we skip processing to avoid producing invalid/ambiguous ids.
            if (_islandCount > MaxIslands)
            {
                Debug.LogWarning(
                    $"[IslandUV] Mesh '{_mesh.name}' produced {_islandCount} islands, which exceeds the 16-bit islandId limit ({MaxIslands}). " +
                    "Skipping IslandUV processing for this mesh. Consider increasing Threshold (deg) or enabling 'Allow Across SubMeshes' to reduce island count.");
                return;
            }

            BuildIslandBasisAndIgnored();
            BakeOutputMeshData();
            ApplyToMesh();
        }

        private void PrepareInput()
        {
            _vertices = _mesh.vertices;
            if (_vertices == null || _vertices.Length == 0)
                throw new InvalidOperationException("Mesh has no vertices.");

            _subMeshCount = Mathf.Max(1, _mesh.subMeshCount);
            _subMeshTriangles = new int[_subMeshCount][];
            int totalIndexCount = 0;
            for (int sm = 0; sm < _subMeshCount; sm++)
            {
                var t = _mesh.GetTriangles(sm);
                _subMeshTriangles[sm] = t;
                if (t != null) totalIndexCount += t.Length;
            }
            if (totalIndexCount == 0)
                throw new InvalidOperationException("Mesh has no triangles.");

            _triCount = totalIndexCount / 3;
            _tris = new Tri[_triCount];

            _srcNormals = _mesh.normals;
            _hasNormals = _srcNormals != null && _srcNormals.Length == _vertices.Length;

            // Normal source: vertex normals first (preferred), fall back to face normals if missing.
            if (!_hasNormals)
            {
                Debug.LogWarning($"Mesh '{_mesh.name}' has no valid vertex normals. Falling back to face normals for IslandUV clustering.");
            }
        }

        private void BuildTriangles()
        {
            int triCursor = 0;

            for (int sm = 0; sm < _subMeshCount; sm++)
            {
                var triArr = _subMeshTriangles[sm];
                if (triArr == null || triArr.Length == 0) continue;
                if ((triArr.Length % 3) != 0)
                    throw new InvalidOperationException($"Mesh '{_mesh.name}' submesh {sm} triangle index array length is not a multiple of 3.");

                for (int idx = 0; idx < triArr.Length; idx += 3)
                {
                    int i0 = triArr[idx];
                    int i1 = triArr[idx + 1];
                    int i2 = triArr[idx + 2];
                    Vector3 v0 = _vertices[i0];
                    Vector3 v1 = _vertices[i1];
                    Vector3 v2 = _vertices[i2];

                    Vector3 normal;
                    if (_hasNormals)
                    {
                        normal = ComputeTriVertexNormal(_srcNormals[i0], _srcNormals[i1], _srcNormals[i2]);
                    }
                    else
                    {
                        // Face normal with degenerate warning
                        Vector3 faceN = Vector3.Cross(v1 - v0, v2 - v0);
                        if (faceN.sqrMagnitude <= 1e-12f)
                        {
                            Debug.LogWarning($"Mesh '{_mesh.name}' contains degenerate triangle {triCursor} (submesh {sm}) with near-zero area. Assigning default normal.");
                            normal = Vector3.up;
                        }
                        else
                        {
                            normal = faceN.normalized;
                        }
                    }

                    _tris[triCursor] = new Tri
                    {
                        i0 = i0,
                        i1 = i1,
                        i2 = i2,
                        normal = normal,
                        subMesh = sm
                    };

                    triCursor++;
                }
            }

            if (triCursor != _triCount)
                throw new InvalidOperationException($"Internal error: triangle count mismatch for mesh '{_mesh.name}'.");
        }

        private void BuildAdjacency()
        {
            var edgeToTris = new Dictionary<Edge, List<int>>();
            for (int i = 0; i < _triCount; i++)
            {
                var tri = _tris[i];
                AddEdgeToMap(tri.i0, tri.i1, i, edgeToTris);
                AddEdgeToMap(tri.i1, tri.i2, i, edgeToTris);
                AddEdgeToMap(tri.i2, tri.i0, i, edgeToTris);
            }

            _triNeighbors = new List<List<int>>(_triCount);
            for (int i = 0; i < _triCount; i++) _triNeighbors.Add(new List<int>(3));

            int nonManifoldEdgeCount = 0;
            int maxTrisOnAnEdge = 0;

            foreach (var kvp in edgeToTris)
            {
                var triangleIndices = kvp.Value;
                if (triangleIndices.Count < 2) continue; // Boundary edge

                // Non-manifold edge: more than 2 triangles share the same edge.
                // Treat as boundary to avoid island growth "jumping" across unrelated surfaces.
                if (triangleIndices.Count > 2)
                {
                    nonManifoldEdgeCount++;
                    if (triangleIndices.Count > maxTrisOnAnEdge) maxTrisOnAnEdge = triangleIndices.Count;
                    continue;
                }

                // Add neighbors
                for (int i = 0; i < triangleIndices.Count; i++)
                {
                    for (int j = i + 1; j < triangleIndices.Count; j++)
                    {
                        int ta = triangleIndices[i];
                        int tb = triangleIndices[j];

                        // Optionally prevent adjacency across submesh boundaries.
                        if (!_s.allowAcrossSubMeshes && _tris[ta].subMesh != _tris[tb].subMesh)
                            continue;

                        _triNeighbors[ta].Add(tb);
                        _triNeighbors[tb].Add(ta);
                    }
                }
            }

            if (nonManifoldEdgeCount > 0)
            {
                Debug.LogWarning(
                    $"Mesh '{_mesh.name}' contains {nonManifoldEdgeCount} non-manifold edges (max triangles on one edge: {maxTrisOnAnEdge}). " +
                    "These edges were treated as boundaries for IslandUV clustering. Consider cleaning the mesh topology if results look fragmented.");
            }
        }

        private void ClusterIslands()
        {
            float cosThreshold = Mathf.Cos(_s.thresholdDeg * Mathf.Deg2Rad);
            _triIsland = new int[_triCount];
            Array.Fill(_triIsland, -1);

            _islandCount = 0;
            var queue = new Queue<int>();

            bool useIslandRef = (_s.propagation == IslandUvSettings.Propagation.Island);
            var islandRefNormal = new List<Vector3>(128);
            var islandRefCount = new List<int>(128);

            for (int i = 0; i < _triCount; i++)
            {
                if (_triIsland[i] != -1) continue; // Already assigned

                int iid = _islandCount++;
                _triIsland[i] = iid;

                if (useIslandRef)
                {
                    islandRefNormal.Add(_tris[i].normal);
                    islandRefCount.Add(1);
                }

                queue.Clear();
                queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    var currentNormal = _tris[current].normal;

                    var neighbors = _triNeighbors[current];
                    foreach (var nbIndex in neighbors)
                    {
                        if (_triIsland[nbIndex] != -1) continue; // Already assigned
                        Vector3 refN = useIslandRef ? islandRefNormal[iid] : currentNormal;
                        float d = Vector3.Dot(refN, _tris[nbIndex].normal);
                        if (d >= cosThreshold)
                        {
                            _triIsland[nbIndex] = iid;

                            if (useIslandRef)
                            {
                                // Running mean on unit sphere: sum then normalize.
                                int c = islandRefCount[iid] + 1;
                                islandRefCount[iid] = c;
                                Vector3 sum = islandRefNormal[iid] * (c - 1) + _tris[nbIndex].normal;
                                islandRefNormal[iid] = sum.sqrMagnitude <= 1e-12f ? Vector3.up : sum.normalized;
                            }

                            queue.Enqueue(nbIndex);
                        }
                    }
                }
            }
        }

        private void BuildIslandBasisAndIgnored()
        {
            _islandBasis = new IslandBasis[_islandCount];
            var islandNormSum = new Vector3[_islandCount];
            var islandVerSum = new Vector3[_islandCount];
            var islandVerCount = new int[_islandCount];

            // Small island metrics
            var islandTriCount = new int[_islandCount];
            var islandArea = new float[_islandCount];
            float totalArea = 0f;

            for (int i = 0; i < _triCount; i++)
            {
                int iid = _triIsland[i];
                var tri = _tris[i];

                islandNormSum[iid] += tri.normal;
                islandVerSum[iid] += _vertices[tri.i0] + _vertices[tri.i1] + _vertices[tri.i2];
                islandVerCount[iid] += 3;

                islandTriCount[iid] += 1;

                Vector3 v0 = _vertices[tri.i0];
                Vector3 v1 = _vertices[tri.i1];
                Vector3 v2 = _vertices[tri.i2];
                float a = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
                islandArea[iid] += a;
                totalArea += a;
            }

            _ignoredIsland = new bool[_islandCount];
            if (_s.ignoreSmall)
            {
                if (_s.smallIsland == IslandUvSettings.SmallIsland.TriCount)
                {
                    int minTris = Mathf.Max(0, _s.minIslandTris);
                    for (int i = 0; i < _islandCount; i++)
                        _ignoredIsland[i] = islandTriCount[i] < minTris;
                }
                else
                {
                    if (totalArea <= 1e-12f)
                    {
                        Debug.LogWarning($"Mesh '{_mesh.name}' total area is near zero; skipping AreaRatio small-island filtering.");
                    }
                    else
                    {
                        float minRatio = Mathf.Clamp01(_s.minIslandAreaRatio);
                        for (int i = 0; i < _islandCount; i++)
                            _ignoredIsland[i] = (islandArea[i] / totalArea) < minRatio;
                    }
                }
            }

            for (int i = 0; i < _islandCount; i++)
            {
                var normal = islandNormSum[i].sqrMagnitude < 1e-12f ? Vector3.up : islandNormSum[i].normalized;
                var origin = islandVerCount[i] > 0 ? islandVerSum[i] / islandVerCount[i] : Vector3.zero;

                // UV orientation (default behavior): align U direction to model-local +Z as much as possible,
                // then fix the sign so different islands don't randomly flip (helps keep text readable).
                // T = projected forward axis on island plane; B = cross(normal, T) (right-handed).
                Vector3 forward = Vector3.forward;

                Vector3 tangent = ProjectOnPlane(forward, normal);
                if (tangent.sqrMagnitude <= 1e-12f)
                {
                    // forward is parallel to normal: try an alternate axis.
                    tangent = ProjectOnPlane(Vector3.right, normal);
                    if (tangent.sqrMagnitude <= 1e-12f)
                        tangent = ProjectOnPlane(Vector3.up, normal);
                }
                tangent = tangent.sqrMagnitude <= 1e-12f ? Vector3.right : tangent.normalized;

                // Sign fix: keep tangent pointing roughly towards +Z.
                if (Vector3.Dot(tangent, forward) < 0f)
                    tangent = -tangent;

                var bitangent = Vector3.Cross(normal, tangent);
                bitangent = bitangent.sqrMagnitude <= 1e-12f ? Vector3.up : bitangent.normalized;

                // Ensure a stable right-handed basis.
                tangent = Vector3.Cross(bitangent, normal);
                tangent = tangent.sqrMagnitude <= 1e-12f ? Vector3.right : tangent.normalized;

                _islandBasis[i] = new IslandBasis
                {
                    origin = origin,
                    T = tangent,
                    B = bitangent,
                    uvMin = new Vector2(float.MaxValue, float.MaxValue),
                    uvMax = new Vector2(float.MinValue, float.MinValue)
                };
            }

            // Calculate uvMin and uvMax
            for (int i = 0; i < _triCount; i++)
            {
                int iid = _triIsland[i];
                UpdateIslandBounds(iid, _tris[i].i0);
                UpdateIslandBounds(iid, _tris[i].i1);
                UpdateIslandBounds(iid, _tris[i].i2);
            }
        }

        private void UpdateIslandBounds(int islandId, int vertexIndex)
        {
            Vector3 p = _vertices[vertexIndex] - _islandBasis[islandId].origin;
            Vector2 uv = new(Vector3.Dot(p, _islandBasis[islandId].T), Vector3.Dot(p, _islandBasis[islandId].B));
            _islandBasis[islandId].uvMin = Vector2.Min(_islandBasis[islandId].uvMin, uv);
            _islandBasis[islandId].uvMax = Vector2.Max(_islandBasis[islandId].uvMax, uv);
        }

        private void BakeOutputMeshData()
        {
            _newVertices = new List<Vector3>(_vertices.Length);
            _newNormals = new List<Vector3>(_vertices.Length);
            _newTextUV = new List<Vector4>(_vertices.Length);
            _vertexMap = new Dictionary<(int v, int i), int>();

            _newTrianglesBySubMesh = new List<int>[_subMeshCount];
            for (int sm = 0; sm < _subMeshCount; sm++)
                _newTrianglesBySubMesh[sm] = new List<int>(_subMeshTriangles[sm]?.Length ?? 0);

            for (int i = 0; i < _triCount; i++)
            {
                int iid = _triIsland[i];
                var basis = _islandBasis[iid];
                bool isIgnored = _ignoredIsland[iid];

                int i0 = _tris[i].i0;
                int i1 = _tris[i].i1;
                int i2 = _tris[i].i2;

                int o0 = GetOrCreateVertex(i0, iid, basis, isIgnored);
                int o1 = GetOrCreateVertex(i1, iid, basis, isIgnored);
                int o2 = GetOrCreateVertex(i2, iid, basis, isIgnored);

                _newTrianglesBySubMesh[_tris[i].subMesh].Add(o0);
                _newTrianglesBySubMesh[_tris[i].subMesh].Add(o1);
                _newTrianglesBySubMesh[_tris[i].subMesh].Add(o2);
            }
        }

        private int GetOrCreateVertex(int originalV, int islandId, IslandBasis islandBasis, bool ignoredIsland)
        {
            var key = (originalV, islandId);
            if (_vertexMap.TryGetValue(key, out int existing))
                return existing;

            int newIndex = _newVertices.Count;
            _vertexMap[key] = newIndex;

            Vector3 vPos = _vertices[originalV];
            _newVertices.Add(vPos);

            if (_hasNormals)
                _newNormals.Add(_srcNormals[originalV]);

            // Project to island plane (T/B)
            Vector3 p = vPos - islandBasis.origin;
            Vector2 uv = new Vector2(Vector3.Dot(p, islandBasis.T), Vector3.Dot(p, islandBasis.B));

            // Normalize to [0,1] within this island bounds.
            Vector2 size = islandBasis.uvMax - islandBasis.uvMin;
            uv = uv - islandBasis.uvMin;
            if (Mathf.Abs(size.x) > 1e-6f) uv.x /= size.x;
            if (Mathf.Abs(size.y) > 1e-6f) uv.y /= size.y;

            // Ignored islands: write fixed UV (0,0)
            if (ignoredIsland) uv = Vector2.zero;

            // For ignored islands, we use sentinel 0xFFFF.
            ushort id16 = ignoredIsland ? IgnoredIslandId : (ushort)Mathf.Clamp(islandId, 0, 65534);
            Vector2 zw = EncodeIslandId16ToZW(id16);
            _newTextUV.Add(new Vector4(uv.x, uv.y, zw.x, zw.y));

            return newIndex;
        }

        private void ApplyToMesh()
        {
            // If the processed mesh exceeds 16-bit index limits, switch to 32-bit indices BEFORE setting triangles.
            // (Splitting vertices per island can increase vertex count significantly.)
            if (_newVertices.Count > 65535)
                _mesh.indexFormat = IndexFormat.UInt32;

            _mesh.SetVertices(_newVertices);
            if (_hasNormals) _mesh.SetNormals(_newNormals);
            else _mesh.RecalculateNormals();

            _mesh.subMeshCount = _subMeshCount;
            for (int sm = 0; sm < _subMeshCount; sm++)
                _mesh.SetTriangles(_newTrianglesBySubMesh[sm], sm);

            _mesh.SetUVs(_s.targetUvChannel, _newTextUV);
        }
    }
}

#endif