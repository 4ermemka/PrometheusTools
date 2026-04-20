using UnityEngine;

public class GrassRenderer : MonoBehaviour
{
    public Mesh grassMesh;
    public Material grassMaterial;
    public ComputeShader placementShader;

    [Header("Grass Settings")]
    public int grassCount = 10000;
    public Vector2 areaSize = new Vector2(50f, 50f);

    private ComputeBuffer grassBuffer;

    void Start()
    {
        if (grassMesh == null)
        {
            Debug.LogError("Grass Mesh is not assigned!");
            return;
        }
        if (grassMaterial == null)
        {
            Debug.LogError("Grass Material is not assigned!");
            return;
        }
        if (placementShader == null)
        {
            Debug.LogError("Placement Compute Shader is not assigned!");
            return;
        }

        InitializeBuffers();
        Debug.Log($"Grass buffer created with {grassCount} instances.");
    }

    void Update()
    {
        Vector3 center = transform.position;
        center.y = 0f; // Фиксируем Y = 0

        placementShader.SetVector("_AreaCenter", center);
        placementShader.SetVector("_AreaSize", new Vector2(areaSize.x, areaSize.y));
        placementShader.SetInt("_GrassCount", grassCount);

        UpdateGrassPlacement();

        // Bounds должны покрывать всю область генерации
        Bounds renderBounds = new Bounds(center, new Vector3(areaSize.x, 1f, areaSize.y));
        Graphics.DrawMeshInstancedProcedural(grassMesh, 0, grassMaterial, renderBounds, grassCount);
    }

    void InitializeBuffers()
    {
        grassBuffer = new ComputeBuffer(grassCount, sizeof(float) * 4);
        grassMaterial.SetBuffer("_GrassBuffer", grassBuffer);
    }

    void UpdateGrassPlacement()
    {
        int kernelHandle = placementShader.FindKernel("CSMain");
        placementShader.SetBuffer(kernelHandle, "_GrassBuffer", grassBuffer);
        placementShader.Dispatch(kernelHandle, Mathf.CeilToInt(grassCount / 64.0f), 1, 1);
    }

    void OnDestroy()
    {
        grassBuffer?.Release();
    }

    // Для визуализации области в редакторе
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
    }
}