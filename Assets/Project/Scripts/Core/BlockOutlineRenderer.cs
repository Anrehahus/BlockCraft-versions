using UnityEngine;

namespace MinecraftClone.Core
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BlockOutlineRenderer : MonoBehaviour
    {
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;

        private void Awake()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();

            if (_meshRenderer.sharedMaterial == null)
            {
                Material lineMat = new Material(Shader.Find("Hidden/Internal-Colored"));
                lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                lineMat.SetInt("_ZWrite", 0);
                lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);

                // Задаем черный цвет для линий обводки (RGBA)
                lineMat.SetColor("_Color", Color.black);

                _meshRenderer.sharedMaterial = lineMat;
            }

            BuildOutlineMesh();
            Hide();
        }

        private void BuildOutlineMesh()
        {
            Mesh mesh = new Mesh();

            float offset = 0.001f;
            float min = -offset;
            float max = 1.0f + offset;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(min, min, min), new Vector3(max, min, min),
                new Vector3(max, min, max), new Vector3(min, min, max),
                new Vector3(min, max, min), new Vector3(max, max, min),
                new Vector3(max, max, max), new Vector3(min, max, max)
            };

            int[] indices = new int[]
            {
                0, 1, 1, 2, 2, 3, 3, 0, // Нижний контур
                4, 5, 5, 6, 6, 7, 7, 4, // Верхний контур
                0, 4, 1, 5, 2, 6, 3, 7  // Вертикальные стойки
            };

            mesh.vertices = vertices;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);

            _meshFilter.mesh = mesh;
        }

        public void Show(Vector3Int blockWorldPos)
        {
            transform.position = blockWorldPos;
            _meshRenderer.enabled = true;
        }

        public void Hide()
        {
            _meshRenderer.enabled = false;
        }
    }
}