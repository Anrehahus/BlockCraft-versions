using UnityEngine;
using UnityEngine.UI;

public class DirtGridGenerator : MonoBehaviour
{
    [Header("Настройки текстур")]
    [SerializeField] private Sprite[] dirtSprites;
    [SerializeField] private Vector2 blockSize = new Vector2(256, 256);

    void Start()
    {
        GenerateGrid();
    }

    [ContextMenu("Generate Grid")]
    public void GenerateGrid()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        RectTransform parentRect = GetComponent<RectTransform>();

        // Берём ширину и высоту родителя (с учётом отрицательных отступов)
        float width = Mathf.Abs(parentRect.rect.width);
        float height = Mathf.Abs(parentRect.rect.height);

        // Если размеры ещё не посчитаны, берём ширину экрана + запас
        if (width <= 0) width = Screen.width + 512;
        if (height <= 0) height = Screen.height + 512;

        int columns = Mathf.CeilToInt(width / blockSize.x) + 2;
        int rows = Mathf.CeilToInt(height / blockSize.y) + 2;
        int totalBlocks = columns * rows;

        if (TryGetComponent<GridLayoutGroup>(out var grid))
        {
            grid.cellSize = blockSize;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
        }

        for (int i = 0; i < totalBlocks; i++)
        {
            GameObject block = new GameObject("DirtBlock_" + i, typeof(RectTransform), typeof(Image));
            block.transform.SetParent(transform, false);

            Image img = block.GetComponent<Image>();
            img.sprite = dirtSprites[Random.Range(0, dirtSprites.Length)];
            img.color = new Color(0.45f, 0.45f, 0.45f, 1f);
        }
    }
}