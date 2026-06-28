using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Loader minimale per file OBJ a runtime.
/// Supporta: vertici (v), normali (vn), UV (vt), facce triangolari e quad (f).
///
/// vertexTransform: se fornita (non-identity), viene applicata a ogni vertice
/// invece del flip Z di default. Usarla quando il caller gestisce già la
/// conversione di sistema di coordinate (es. allineamento a volume NIfTI).
/// </summary>
public static class SimpleOBJLoader
{
    public static GameObject Load(string path, Matrix4x4 vertexTransform = default)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[SimpleOBJLoader] File non trovato: {path}");
            return null;
        }

        bool useCustomTransform = (vertexTransform != default && vertexTransform != Matrix4x4.identity);

        string[] lines = File.ReadAllLines(path);

        var positions  = new List<Vector3>();
        var normals    = new List<Vector3>();
        var uvs        = new List<Vector2>();

        var meshVertices  = new List<Vector3>();
        var meshNormals   = new List<Vector3>();
        var meshUVs       = new List<Vector2>();
        var meshTriangles = new List<int>();

        var vertexCache = new Dictionary<string, int>();

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] tokens = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            switch (tokens[0])
            {
                case "v":
                    positions.Add(new Vector3(ParseF(tokens[1]), ParseF(tokens[2]), ParseF(tokens[3])));
                    break;

                case "vn":
                    normals.Add(new Vector3(ParseF(tokens[1]), ParseF(tokens[2]), ParseF(tokens[3])));
                    break;

                case "vt":
                    uvs.Add(new Vector2(ParseF(tokens[1]), tokens.Length > 2 ? ParseF(tokens[2]) : 0f));
                    break;

                case "f":
                    int[] faceIdx = new int[tokens.Length - 1];
                    for (int i = 1; i < tokens.Length; i++)
                        faceIdx[i - 1] = AddVertex(tokens[i], positions, normals, uvs,
                                                    meshVertices, meshNormals, meshUVs,
                                                    vertexCache, useCustomTransform, vertexTransform);

                    // Fan triangulation
                    for (int i = 1; i < faceIdx.Length - 1; i++)
                    {
                        meshTriangles.Add(faceIdx[0]);
                        meshTriangles.Add(faceIdx[i]);
                        meshTriangles.Add(faceIdx[i + 1]);
                    }
                    break;
            }
        }

        if (meshVertices.Count == 0)
        {
            Debug.LogWarning($"[SimpleOBJLoader] Nessun vertice trovato in: {path}");
            return null;
        }

        Mesh mesh = new Mesh();
        mesh.name = Path.GetFileNameWithoutExtension(path);
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(meshVertices);

        if (meshNormals.Count == meshVertices.Count)
            mesh.SetNormals(meshNormals);
        if (meshUVs.Count == meshVertices.Count)
            mesh.SetUVs(0, meshUVs);

        // Se la trasformazione inverte l'handedness (det < 0), le facce risultano
        // inside-out (winding clockwise in Unity). Invertire l'ordine v1/v2
        // di ogni triangolo riporta il winding al senso corretto.
        if (useCustomTransform && vertexTransform.determinant < 0)
        {
            for (int i = 0; i < meshTriangles.Count; i += 3)
            {
                int tmp = meshTriangles[i + 1];
                meshTriangles[i + 1] = meshTriangles[i + 2];
                meshTriangles[i + 2] = tmp;
            }
        }

        mesh.SetTriangles(meshTriangles, 0);

        if (meshNormals.Count != meshVertices.Count)
            mesh.RecalculateNormals();

        mesh.RecalculateBounds();

        GameObject go = new GameObject(mesh.name);
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = CreateFractureMaterial();

        return go;
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    static int AddVertex(
        string token,
        List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs,
        List<Vector3> meshVerts, List<Vector3> meshNorms, List<Vector2> meshUVs,
        Dictionary<string, int> cache,
        bool useCustomTransform, Matrix4x4 transform)
    {
        if (cache.TryGetValue(token, out int existing))
            return existing;

        string[] parts = token.Split('/');
        int posIdx  = ParseIdx(parts[0]) - 1;
        int uvIdx   = parts.Length > 1 && parts[1].Length > 0 ? ParseIdx(parts[1]) - 1 : -1;
        int normIdx = parts.Length > 2 && parts[2].Length > 0 ? ParseIdx(parts[2]) - 1 : -1;

        Vector3 rawPos = posIdx >= 0 && posIdx < positions.Count ? positions[posIdx] : Vector3.zero;

        Vector3 pos;
        if (useCustomTransform)
        {
            // Il caller fornisce la trasformazione completa (es. mm world → Unity world)
            // Non applichiamo il flip Z di default
            pos = transform.MultiplyPoint3x4(rawPos);
        }
        else
        {
            // Default: converti da right-hand (OBJ) a left-hand (Unity) invertendo Z
            pos = new Vector3(rawPos.x, rawPos.y, -rawPos.z);
        }

        meshVerts.Add(pos);

        if (normIdx >= 0 && normIdx < normals.Count)
        {
            Vector3 n = normals[normIdx];
            meshNorms.Add(useCustomTransform
                ? transform.MultiplyVector(n).normalized
                : new Vector3(n.x, n.y, -n.z));
        }

        if (uvIdx >= 0 && uvIdx < uvs.Count)
            meshUVs.Add(uvs[uvIdx]);

        int newIdx = meshVerts.Count - 1;
        cache[token] = newIdx;
        return newIdx;
    }

    // Materiale overlay per le fratture: shader custom con ZTest Always hardcoded,
    // Cull Off e alpha blending. Sempre visibile sopra la CT volumetrica.
    static Material CreateFractureMaterial()
    {
        Shader shader = Shader.Find("Custom/FractureOverlay");
        if (shader == null)
        {
            Debug.LogError("[SimpleOBJLoader] Shader 'Custom/FractureOverlay' non trovato! " +
                           "Assicurati che Assets/Shaders/FractureOverlay.shader esista nel progetto.");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        return new Material(shader);
    }

    static float ParseF(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static int   ParseIdx(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
