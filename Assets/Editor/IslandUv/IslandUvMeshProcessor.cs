#if UNITY_EDITOR
using UnityEngine;
using System;
using System.Collections.Generic;

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
        bool normalizeUv,
        Vector3[] vertices,
        Vector3[] srcNormals,
        bool hasNormals,
        List<Vector3> newVertices,
        List<Vector3> newNormals,
        List<Vector2> newTextUV,
        Dictionary<(int v, int i), int> vertexMap,
        IslandUvImportConfig.Settings settings)
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

        // 第一步：计算投影坐标
        Vector3 p = vPos - islandBasis.origin;
        Vector2 uv = new Vector2(Vector3.Dot(p, islandBasis.T), Vector3.Dot(p, islandBasis.B));

        // 第二步：应用归一化（如果启用）
        // 注意：此时 uvMin/uvMax 已在前面步骤计算完成
        if (normalizeUv)
        {
            Vector2 size = islandBasis.uvMax - islandBasis.uvMin;
            uv = uv - islandBasis.uvMin;  // 平移到原点
            if (Mathf.Abs(size.x) > 1e-6f) uv.x /= size.x;  // 归一化到 [0,1]
            if (Mathf.Abs(size.y) > 1e-6f) uv.y /= size.y;
        }

        newTextUV.Add(uv);

        return newIndex;
    }

    public static void ProcessMesh(Mesh mesh, IslandUvImportConfig.Settings s)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        if (s == null) throw new ArgumentNullException(nameof(s));

        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        if (vertices == null || vertices.Length == 0) throw new InvalidOperationException("Mesh has no vertices.");
        if (triangles == null || triangles.Length == 0) throw new InvalidOperationException("Mesh has no triangles.");

        // triangles data
        int triCount = triangles.Length / 3;
        var tris = new Tri[triCount];

        // 1) Calculate triangles
        for (int i = 0; i < triCount; i++)
        {
            int index = i * 3;
            int i0 = triangles[index];
            int i1 = triangles[index + 1];
            int i2 = triangles[index + 2];
            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);

            // Check for degenerate triangles and warn user
            if (normal.sqrMagnitude <= 1e-12f)
            {
                Debug.LogWarning($"Mesh '{mesh.name}' contains degenerate triangle {i} with near-zero area. Assigning default normal.");
                normal = Vector3.up;
            }
            else
            {
                normal.Normalize();
            }

            tris[i] = new Tri
            {
                i0 = i0,
                i1 = i1,
                i2 = i2,
                normal = normal
            };
        }

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
        foreach (var kvp in edgeToTris)
        {
            var triangleIndices = kvp.Value;
            if (triangleIndices.Count < 2) continue; // Boundary edge
            // Add neighbors
            for (int i = 0; i < triangleIndices.Count; i++)
            {
                for (int j = i + 1; j < triangleIndices.Count; j++)
                {
                    triNeighbors[triangleIndices[i]].Add(triangleIndices[j]);
                    triNeighbors[triangleIndices[j]].Add(triangleIndices[i]);
                }
            }
        }

        // 4) Cluster triangles into islands
        float cosThreshold = Mathf.Cos(s.normalAngleThresholdDeg * Mathf.Deg2Rad);
        var triIsland = new int[triCount];
        Array.Fill(triIsland, -1);

        int islandCount = 0;
        var queue = new Queue<int>();

        for (int i = 0; i < triCount; i++)
        {
            if (triIsland[i] != -1) continue; // Already assigned

            int iid = islandCount++;
            triIsland[i] = iid;
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
                    float d = Vector3.Dot(currentNormal, tris[nbIndex].normal);
                    if (d >= cosThreshold)
                    {
                        triIsland[nbIndex] = iid;
                        queue.Enqueue(nbIndex);
                    }
                }
            }
        }

        // 5) Calculate island basis
        var islandBasis = new IslandBasis[islandCount];
        var islandNormSum = new Vector3[islandCount];
        var islandVerSum = new Vector3[islandCount];
        var islandVerCount = new int[islandCount];

        // accumulate normal and vertex positions
        for (int i = 0; i < triCount; i++)
        {
            int iid = triIsland[i];
            var tri = tris[i];
            islandNormSum[iid] += tri.normal;
            islandVerSum[iid] += vertices[tri.i0] + vertices[tri.i1] + vertices[tri.i2];
            islandVerCount[iid] += 3;
        }

        for (int i = 0; i < islandCount; i++)
        {
            var normal = islandNormSum[i].sqrMagnitude < 1e-12f ? Vector3.up : islandNormSum[i].normalized;
            var origin = islandVerCount[i] > 0 ? islandVerSum[i] / islandVerCount[i] : Vector3.zero;

            Vector3 refAxis = (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.999f) ? Vector3.up : Vector3.right;
            var tangent = Vector3.Cross(refAxis, normal).normalized;
            if (tangent.sqrMagnitude < 1e-12f) tangent = Vector3.right;
            var bitangent = Vector3.Cross(normal, tangent).normalized;

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
        var newTriangles = new int[triangles.Length];
        var newTextUV = new List<Vector2>(vertices.Length);

        Vector3[] srcNormals = mesh.normals;
        bool hasNormals = srcNormals != null && srcNormals.Length == vertices.Length;

        var vertexMap = new Dictionary<(int v, int i), int>();

        for (int i = 0; i < triCount; i++)
        {
            int iid = triIsland[i];
            var basis = islandBasis[iid];

            int i0 = tris[i].i0;
            int i1 = tris[i].i1;
            int i2 = tris[i].i2;

            int o0 = GetOrCreateVertex(i0, iid, basis, s.normalizeUv, vertices, srcNormals, hasNormals, newVertices, newNormals, newTextUV, vertexMap, s);
            int o1 = GetOrCreateVertex(i1, iid, basis, s.normalizeUv, vertices, srcNormals, hasNormals, newVertices, newNormals, newTextUV, vertexMap, s);
            int o2 = GetOrCreateVertex(i2, iid, basis, s.normalizeUv, vertices, srcNormals, hasNormals, newVertices, newNormals, newTextUV, vertexMap, s);

            newTriangles[i * 3] = o0;
            newTriangles[i * 3 + 1] = o1;
            newTriangles[i * 3 + 2] = o2;
        }

        // 7) Assign back to mesh
        mesh.SetVertices(newVertices);
        if (hasNormals) mesh.SetNormals(newNormals);
        else mesh.RecalculateNormals();
        mesh.SetTriangles(newTriangles, 0);
        mesh.SetUVs(s.targetUvChannel, newTextUV);
    }
}

#endif