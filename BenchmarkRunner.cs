using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public class BenchmarkRunner : MonoBehaviour
{
    public int[] copyLevels = { 100, 500, 2000 };   // number of spheres in the scene
    public int[] aaLevels = { 0, 4 };               // MSAA off / 4x
    public int repeats = 5;
    public float warmupSeconds = 2f;
    public float measureSeconds = 10f;
    public int seed = 42;

    struct RunSpec { public int copies, aa, repeat; }

    string csvPath;

    void Start()
    {
        // keep the player running if the window loses focus (otherwise one frame can last tens of seconds)
        Application.runInBackground = true;

        // force the design values so that stale Inspector values cannot override them
        repeats = 5;
        warmupSeconds = 2f;
        measureSeconds = 10f;

        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        csvPath = Path.Combine(Application.persistentDataPath, "frametimes_" + stamp + ".csv");
        File.WriteAllText(csvPath, "run_id,order,copies,triangles,aa,repeat,frame_idx,frame_ms\n");
        WriteMetadata(Path.Combine(Application.persistentDataPath, "metadata_" + stamp + ".txt"));
        Debug.Log("Logging to: " + csvPath);
        StartCoroutine(RunAll());
    }

    IEnumerator RunAll()
    {
        QualitySettings.vSyncCount = 0;      // otherwise frame time is capped by the monitor
        Application.targetFrameRate = -1;

        Camera cam = Camera.main;
        cam.transform.position = new Vector3(0f, 0f, -15f);
        cam.transform.rotation = Quaternion.identity;

        var runs = new List<RunSpec>();
        foreach (int c in copyLevels)
            foreach (int a in aaLevels)
                for (int r = 1; r <= repeats; r++)
                    runs.Add(new RunSpec { copies = c, aa = a, repeat = r });

        // random but reproducible run order (reduces thermal / background-load bias)
        var rng = new System.Random(seed);
        for (int i = runs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            RunSpec tmp = runs[i]; runs[i] = runs[j]; runs[j] = tmp;
        }

        for (int k = 0; k < runs.Count; k++)
        {
            RunSpec spec = runs[k];
            QualitySettings.antiAliasing = spec.aa;

            int trisPerMesh;
            GameObject root = BuildCluster(spec.copies, out trisPerMesh);
            yield return null;

            float t = 0f;
            while (t < warmupSeconds) { t += Time.unscaledDeltaTime; yield return null; }

            var times = new List<float>(8192);
            t = 0f;
            while (t < measureSeconds)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                times.Add(dt * 1000f);
                yield return null;
            }

            WriteRun(k + 1, spec, spec.copies * trisPerMesh, times);
            Destroy(root);
            yield return null;
            Debug.Log("Run " + (k + 1) + "/" + runs.Count + " done (AA read-back: " + QualitySettings.antiAliasing + ")");
        }

        Debug.Log("Finished. CSV: " + csvPath);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    GameObject BuildCluster(int copies, out int trisPerMesh)
    {
        var root = new GameObject("Cluster");
        var rng = new System.Random(seed);   // same positions for every level (smaller levels are a prefix)
        trisPerMesh = 0;
        for (int i = 0; i < copies; i++)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(
                (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 10 - 5),
                (float)(rng.NextDouble() * 20));
            go.transform.localScale = Vector3.one * 0.8f;
            if (i == 0)
                trisPerMesh = (int)(go.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3);
        }
        return root;
    }

    void WriteRun(int order, RunSpec s, int triangles, List<float> times)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string runId = "c" + s.copies + "_aa" + s.aa + "_r" + s.repeat;
        var sb = new StringBuilder(times.Count * 40);
        for (int i = 0; i < times.Count; i++)
        {
            sb.Append(runId).Append(',').Append(order).Append(',').Append(s.copies).Append(',')
              .Append(triangles).Append(',').Append(s.aa).Append(',').Append(s.repeat).Append(',')
              .Append(i).Append(',').Append(times[i].ToString("F4", inv)).Append('\n');
        }
        File.AppendAllText(csvPath, sb.ToString());   // written after the run, so I/O does not disturb timing
    }

    void WriteMetadata(string path)
    {
        string pipeline = GraphicsSettings.currentRenderPipeline == null
            ? "Built-in" : GraphicsSettings.currentRenderPipeline.name;
        if (pipeline != "Built-in")
            Debug.LogWarning("Non built-in render pipeline detected: QualitySettings.antiAliasing may have no effect.");

        var sb = new StringBuilder();
        sb.AppendLine("Unity: " + Application.unityVersion);
        sb.AppendLine("Render pipeline: " + pipeline);
        sb.AppendLine("GPU: " + SystemInfo.graphicsDeviceName);
        sb.AppendLine("CPU: " + SystemInfo.processorType);
        sb.AppendLine("RAM MB: " + SystemInfo.systemMemorySize);
        sb.AppendLine("OS: " + SystemInfo.operatingSystem);
        sb.AppendLine("Resolution: " + Screen.width + "x" + Screen.height);
        sb.AppendLine("Design: copies=" + string.Join("/", copyLevels) + ", aa=" + string.Join("/", aaLevels) +
                      ", repeats=" + repeats + ", warmup=" + warmupSeconds + "s, measure=" + measureSeconds +
                      "s, seed=" + seed + ", vSync=off, runInBackground=on");
        File.WriteAllText(path, sb.ToString());
    }
}
