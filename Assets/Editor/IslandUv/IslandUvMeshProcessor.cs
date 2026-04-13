#if UNITY_EDITOR
using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

public static class IslandUvMeshProcessor
{
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

    private static Vector3 ComputeFaceNormal(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
        if (n.sqrMagnitude <= 1e-12f) return Vector3.up;
        return n.normalized;
    }

    private static Vector3 ComputeTriVertexNormal(Vector3 n0, Vector3 n1, Vector3 n2)
    {
        Vector3 n = n0 + n1 + n2;
        if (n.sqrMagnitude <= 1e-12f) return Vector3.up;
        return n.normalized;
    }

    private struct IslandBasis
    {
        public Vector3 origin;
        public Vector3 T;
        public Vector3 B;
        public Vector2 uvMin;
        public Vector2 uvMax;
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

    private static int GetOrCreateVertex(
        int originalV,
        int islandId,
        IslandBasis islandBasis,
    bool ignoredIsland,
        Vector3[] vertices,
        Vector3[] srcNormals,
        bool hasNormals,
        List<Vector3> newVertices,
        List<Vector3> newNormals,
    List<Vector4> newTextUV,
    Dictionary<(int v, int i), int> vertexMap,
    IslandUvSettings.Settings settings)
    {
        var key = (originalV, islandId);
        if (vertexMap.TryGetValue(key, out int existing))
            return existing;

        int newIndex = newVertices.Count;
        vertexMap[key] = newIndex;

        Vector3 vPos = vertices[originalV];
        newVertices.Add(vPos);

        if (hasNormals)
            newNormals.Add(srcNormals[originalV]);

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

        // IslandId is encoded into the SAME UV channel as islandUV, in the zw components as two bytes (0..1).
        // 16-bit little endian: id = lo + hi*256.
        // For ignored islands, we use sentinel 0xFFFF.
        ushort id16 = ignoredIsland ? (ushort)0xFFFF : (ushort)Mathf.Clamp(islandId, 0, 65534);
        float lo = (id16 & 0xFF) / 255.0f;
        float hi = ((id16 >> 8) & 0xFF) / 255.0f;

        newTextUV.Add(new Vector4(uv.x, uv.y, lo, hi));

        return newIndex;
    }

    public static void ProcessMesh(Mesh mesh, IslandUvSettings.Settings s)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (s == null) throw new ArgumentNullException(nameof(s));

        var vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0) throw new InvalidOperationException("Mesh has no vertices.");

        int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
        var subMeshTriangles = new int[subMeshCount][];
        int totalIndexCount = 0;
        for (int sm = 0; sm < subMeshCount; sm++)
        {
            var t = mesh.GetTriangles(sm);
            subMeshTriangles[sm] = t;
            if (t != null) totalIndexCount += t.Length;
        }
        if (totalIndexCount == 0) throw new InvalidOperationException("Mesh has no triangles.");

        // triangles data
        int triCount = totalIndexCount / 3;
        var tris = new Tri[triCount];

        Vector3[] srcNormals = mesh.normals;
        bool hasNormals = srcNormals != null && srcNormals.Length == vertices.Length;

        bool useVertexNormals = (s.normalSource == IslandUvSettings.NormalSource.Vertex);
        if (useVertexNormals && !hasNormals)
        {
            Debug.LogWarning($"Mesh '{mesh.name}' has no valid vertex normals. Falling back to face normals for IslandUV clustering.");
            useVertexNormals = false;
        }

        // Build a global triangle list, preserving source submesh
        int triCursor = 0;
        for (int sm = 0; sm < subMeshCount; sm++)
        {
            var triArr = subMeshTriangles[sm];
            if (triArr == null || triArr.Length == 0) continue;
            if ((triArr.Length % 3) != 0)
                throw new InvalidOperationException($"Mesh '{mesh.name}' submesh {sm} triangle index array length is not a multiple of 3.");

            for (int idx = 0; idx < triArr.Length; idx += 3)
            {
                int i0 = triArr[idx];
                int i1 = triArr[idx + 1];
                int i2 = triArr[idx + 2];
                Vector3 v0 = vertices[i0];
                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];

                Vector3 normal;
                if (useVertexNormals)
                {
                    normal = ComputeTriVertexNormal(srcNormals[i0], srcNormals[i1], srcNormals[i2]);
                }
                else
                {
                    // Face normal with degenerate warning
                    Vector3 faceN = Vector3.Cross(v1 - v0, v2 - v0);
                    if (faceN.sqrMagnitude <= 1e-12f)
                    {
                        Debug.LogWarning($"Mesh '{mesh.name}' contains degenerate triangle {triCursor} (submesh {sm}) with near-zero area. Assigning default normal.");
                        normal = Vector3.up;
                    }
                    else
                    {
                        normal = faceN.normalized;
                    }
                }

                tris[triCursor] = new Tri
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

        if (triCursor != triCount)
            throw new InvalidOperationException($"Internal error: triangle count mismatch for mesh '{mesh.name}'.");

        // 2) Edge -> Triangle mapping
        var edgeToTris = new Dictionary<Edge, List<int>>();
        for (int i = 0; i < triCount; i++)
        {
            var tri = tris[i];
            AddEdgeToMap(tri.i0, tri.i1, i, edgeToTris);
            AddEdgeToMap(tri.i1, tri.i2, i, edgeToTris);
            AddEdgeToMap(tri.i2, tri.i0, i, edgeToTris);
        }

        // 3) Triangle neighbor list
        var triNeighbors = new List<List<int>>();
        for (int i = 0; i < triCount; i++) triNeighbors.Add(new List<int>(3));

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
                    if (!s.allowAcrossSubMeshes && tris[ta].subMesh != tris[tb].subMesh)
                        continue;

                    triNeighbors[ta].Add(tb);
                    triNeighbors[tb].Add(ta);
                }
            }
        }

        if (nonManifoldEdgeCount > 0)
        {
            Debug.LogWarning(
                $"Mesh '{mesh.name}' contains {nonManifoldEdgeCount} non-manifold edges (max triangles on one edge: {maxTrisOnAnEdge}). " +
                "These edges were treated as boundaries for IslandUV clustering. Consider cleaning the mesh topology if results look fragmented.");
        }

        // 4) Cluster triangles into islands
        float cosThreshold = Mathf.Cos(s.thresholdDeg * Mathf.Deg2Rad);
        var triIsland = new int[triCount];
        Array.Fill(triIsland, -1);

        int islandCount = 0;
        var queue = new Queue<int>();

        bool useIslandRef = (s.propagation == IslandUvSettings.Propagation.Island);
        var islandRefNormal = new List<Vector3>(128);
        var islandRefCount = new List<int>(128);

        for (int i = 0; i < triCount; i++)
        {
            if (triIsland[i] != -1) continue; // Already assigned

            int iid = islandCount++;
            triIsland[i] = iid;

            if (useIslandRef)
            {
                islandRefNormal.Add(tris[i].normal);
                islandRefCount.Add(1);
            }

            queue.Clear();
            queue.Enqueue(i);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                var currentNormal = tris[current].normal;

                var neighbors = triNeighbors[current];
                foreach (var nbIndex in neighbors)
                {
                    if (triIsland[nbIndex] != -1) continue; // Already assigned
                    Vector3 refN = useIslandRef ? islandRefNormal[iid] : currentNormal;
                    float d = Vector3.Dot(refN, tris[nbIndex].normal);
                    if (d >= cosThreshold)
                    {
                        triIsland[nbIndex] = iid;

                        if (useIslandRef)
                        {
                            // Running mean on unit sphere: sum then normalize.
                            int c = islandRefCount[iid] + 1;
                            islandRefCount[iid] = c;
                            Vector3 sum = islandRefNormal[iid] * (c - 1) + tris[nbIndex].normal;
                            islandRefNormal[iid] = sum.sqrMagnitude <= 1e-12f ? Vector3.up : sum.normalized;
                        }

                        queue.Enqueue(nbIndex);
                    }
                }
            }
        }

        // islandId is stored as 16-bit, with 0xFFFF reserved for ignored islands.
        // So valid island ids are 0..65534, i.e. at most 65535 islands.
        // If we exceed this, we skip processing to avoid producing invalid/ambiguous ids.
        const int MaxIslands = 0xFFFF; // 65535 valid ids (0..65534). 0xFFFF is reserved.
        if (islandCount > MaxIslands)
        {
            Debug.LogWarning(
                $"[IslandUV] Mesh '{mesh.name}' produced {islandCount} islands, which exceeds the 16-bit islandId limit ({MaxIslands}). " +
                "Skipping IslandUV processing for this mesh. Consider increasing Threshold (deg) or enabling 'Allow Across SubMeshes' to reduce island count.");
            return;
        }

        // 5) Calculate island basis
        var islandBasis = new IslandBasis[islandCount];
        var islandNormSum = new Vector3[islandCount];
        var islandVerSum = new Vector3[islandCount];
        var islandVerCount = new int[islandCount];

        // Small island metrics
        var islandTriCount = new int[islandCount];
        var islandArea = new float[islandCount];
        float totalArea = 0f;

        // accumulate normal and vertex positions
        for (int i = 0; i < triCount; i++)
        {
            int iid = triIsland[i];
            var tri = tris[i];
            islandNormSum[iid] += tri.normal;
            islandVerSum[iid] += vertices[tri.i0] + vertices[tri.i1] + vertices[tri.i2];
            islandVerCount[iid] += 3;

            islandTriCount[iid] += 1;

            // Geometric area (world/object space units^2)
            Vector3 v0 = vertices[tri.i0];
            Vector3 v1 = vertices[tri.i1];
            Vector3 v2 = vertices[tri.i2];
            float a = Vector3.Cross(v1 - v0, v2 - v0).magnitude * 0.5f;
            islandArea[iid] += a;
            totalArea += a;
        }

        // Decide which islands are ignored
        var ignoredIsland = new bool[islandCount];
        if (s.ignoreSmall)
        {
            if (s.smallIsland == IslandUvSettings.SmallIsland.TriCount)
            {
                int minTris = Mathf.Max(0, s.minIslandTris);
                for (int i = 0; i < islandCount; i++)
                    ignoredIsland[i] = islandTriCount[i] < minTris;
            }
            else
            {
                if (totalArea <= 1e-12f)
                {
                    Debug.LogWarning($"Mesh '{mesh.name}' total area is near zero; skipping AreaRatio small-island filtering.");
                }
                else
                {
                    float minRatio = Mathf.Clamp01(s.minIslandAreaRatio);
                    for (int i = 0; i < islandCount; i++)
                        ignoredIsland[i] = (islandArea[i] / totalArea) < minRatio;
                }
            }
        }

        for (int i = 0; i < islandCount; i++)
        {
            var normal = islandNormSum[i].sqrMagnitude < 1e-12f ? Vector3.up : islandNormSum[i].normalized;
            var origin = islandVerCount[i] > 0 ? islandVerSum[i] / islandVerCount[i] : Vector3.zero;

            // UV orientation (default behavior): align U direction to model-local +Z as much as possible,
            // then fix the sign so different islands don't randomly flip (helps keep text readable).
            // T = projected forward axis on island plane; B = cross(normal, T) (right-handed).
            Vector3 forward = Vector3.forward;

            Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * Vector3.Dot(v, n);

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

            islandBasis[i] = new IslandBasis
            {
                origin = origin,
                T = tangent,
                B = bitangent,
                uvMin = new Vector2(float.MaxValue, float.MaxValue),
                uvMax = new Vector2(float.MinValue, float.MinValue)
            };
        }

        // Calculate uvMin and uvMax
        for (int i = 0; i < triCount; i++)
        {
            int iid = triIsland[i];
            int[] ids = { tris[i].i0, tris[i].i1, tris[i].i2 };
            for (int j = 0; j < 3; j++)
            {
                Vector3 p = vertices[ids[j]] - islandBasis[iid].origin;
                Vector2 uv = new(Vector3.Dot(p, islandBasis[iid].T), Vector3.Dot(p, islandBasis[iid].B));
                islandBasis[iid].uvMin = Vector2.Min(islandBasis[iid].uvMin, uv);
                islandBasis[iid].uvMax = Vector2.Max(islandBasis[iid].uvMax, uv);
            }
        }

        // 6) Calculate final UVs
        var newVertices = new List<Vector3>(vertices.Length);
        var newNormals = new List<Vector3>(vertices.Length);
        var newTextUV = new List<Vector4>(vertices.Length);

        // srcNormals / hasNormals are computed earlier (used both for clustering normals and for output normal copying).

        var vertexMap = new Dictionary<(int v, int i), int>();

        var newTrianglesBySubMesh = new List<int>[subMeshCount];
        for (int sm = 0; sm < subMeshCount; sm++)
            newTrianglesBySubMesh[sm] = new List<int>(subMeshTriangles[sm]?.Length ?? 0);

        for (int i = 0; i < triCount; i++)
        {
            int iid = triIsland[i];
            var basis = islandBasis[iid];

            int i0 = tris[i].i0;
            int i1 = tris[i].i1;
            int i2 = tris[i].i2;

            bool isIgnored = ignoredIsland[iid];

            int o0 = GetOrCreateVertex(i0, iid, basis, isIgnored, vertices, srcNormals, hasNormals, newVertices, newNormals, newTextUV, vertexMap, s);
            int o1 = GetOrCreateVertex(i1, iid, basis, isIgnored, vertices, srcNormals, hasNormals, newVertices, newNormals, newTextUV, vertexMap, s);
            int o2 = GetOrCreateVertex(i2, iid, basis, isIgnored, vertices, srcNormals, hasNormals, newVertices, newNormals, newTextUV, vertexMap, s);

            newTrianglesBySubMesh[tris[i].subMesh].Add(o0);
            newTrianglesBySubMesh[tris[i].subMesh].Add(o1);
            newTrianglesBySubMesh[tris[i].subMesh].Add(o2);
        }

        // 7) Assign back to mesh

        // If the processed mesh exceeds 16-bit index limits, switch to 32-bit indices BEFORE setting triangles.
        // (Splitting vertices per island can increase vertex count significantly.)
        if (newVertices.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(newVertices);
        if (hasNormals) mesh.SetNormals(newNormals);
        else mesh.RecalculateNormals();

        mesh.subMeshCount = subMeshCount;
        for (int sm = 0; sm < subMeshCount; sm++)
            mesh.SetTriangles(newTrianglesBySubMesh[sm], sm);

        mesh.SetUVs(s.targetUvChannel, newTextUV);
    }
}

#endif