using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;

namespace MinecraftClone.Rendering
{
    // Только Unity-часть чанка: Mesh, материалы, коллайдеры, надписи на табличках.
    // Сами данные меша строит ChunkMeshGenerator (можно в фоновом потоке).
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class ChunkRenderer : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        // Основной физический коллайдер (для твердых блоков, по которым ходим)
        private MeshCollider solidCollider;

        // Триггерный коллайдер (невидимый объем для четкого выделения саженцев Raycast-ом)
        private MeshCollider triggerCollider;

        private Mesh chunkMesh;
        private Mesh solidCollisionMesh;
        private Mesh triggerCollisionMesh;

        private ulong _appliedSolidHash, _appliedPlantHash;
        private bool _solidApplied, _plantApplied;
        private bool _solidBound;    // solidCollisionMesh сейчас назначен коллайдеру
        private bool _solidDirty;    // содержимое collision-меша изменилось, коллайдер надо перезапечь

        // Номер сборки: меняется при каждой новой сборке и при очистке чанка.
        // По нему отбрасываются устаревшие результаты фоновых потоков.
        public int BuildTicket { get; private set; }

        // Включена ли физика (MeshCollider) у этого чанка
        public bool CollisionEnabled { get; private set; }

        // Для синхронных пересборок (ломание блоков и т.п.), только основной поток
        private static ChunkMeshGenerator _syncGen;
        private static readonly List<Material> _activeMats = new List<Material>();
        private static readonly List<List<int>> _subMeshes = new List<List<int>>();

        private readonly Dictionary<Vector3Int, SignLabel> _labels = new Dictionary<Vector3Int, SignLabel>();
        private readonly List<Vector3Int> _labelsToRemove = new List<Vector3Int>();

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            MeshCollider[] colliders = GetComponents<MeshCollider>();
            solidCollider = colliders.Length > 0 ? colliders[0] : gameObject.AddComponent<MeshCollider>();

            if (colliders.Length > 1)
            {
                triggerCollider = colliders[1];
            }
            else
            {
                triggerCollider = gameObject.AddComponent<MeshCollider>();
            }

            // Быстрее запекание: меш собран нами и не содержит дублей/мусорных треугольников
            solidCollider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;

            // Настройка триггерного коллайдера для Raycast
            triggerCollider.convex = true;
            triggerCollider.isTrigger = true;

            chunkMesh = new Mesh { name = "ChunkMesh" };
            solidCollisionMesh = new Mesh { name = "SolidCollisionMesh" };
            triggerCollisionMesh = new Mesh { name = "TriggerCollisionMesh" };

            meshFilter.mesh = chunkMesh;

            Transform oldWater = transform.Find("WaterRenderer");
            if (oldWater != null)
            {
                DestroyImmediate(oldWater.gameObject);
            }
        }

        // ---------- Сборка ----------

        // Синхронная сборка на основном потоке (старт мира, правка блоков).
        public void RenderChunk(
            ChunkData chunkData,
            IBlockReader world,
            Material opaqueMaterial,
            Material cutoutMaterial,
            Material waterMaterial,
            Material lavaMaterial,
            bool collision)
        {
            if (chunkData == null) return;

            if (_syncGen == null) _syncGen = new ChunkMeshGenerator();
            _syncGen.Build(chunkData, world);

            BuildTicket++;   // более ранние фоновые результаты для этого чанка устарели
            ApplyFrom(_syncGen, opaqueMaterial, cutoutMaterial, waterMaterial, lavaMaterial, collision);
        }

        // Начало фоновой сборки: возвращает номер, с которым вернётся результат.
        public int BeginBuild()
        {
            return ++BuildTicket;
        }

        // Применение готовых данных меша (основной поток).
        public void ApplyFrom(
            ChunkMeshGenerator g,
            Material opaqueMaterial,
            Material cutoutMaterial,
            Material waterMaterial,
            Material lavaMaterial,
            bool collision)
        {
            SyncSignLabels(g.SignsNow);

            chunkMesh.Clear();
            chunkMesh.SetVertices(g.Vertices);

            _activeMats.Clear();
            _subMeshes.Clear();

            // порядок: непрозрачные, вырезные (листья, стекло, растения), вода, лава
            if (g.SolidTriangles.Count > 0)
            {
                _subMeshes.Add(g.SolidTriangles);
                _activeMats.Add(opaqueMaterial);
            }
            if (g.CutoutTriangles.Count > 0)
            {
                _subMeshes.Add(g.CutoutTriangles);
                _activeMats.Add(cutoutMaterial);
            }
            if (g.WaterTriangles.Count > 0)
            {
                _subMeshes.Add(g.WaterTriangles);
                _activeMats.Add(waterMaterial);
            }
            if (g.LavaTriangles.Count > 0)
            {
                _subMeshes.Add(g.LavaTriangles);
                _activeMats.Add(lavaMaterial);
            }

            chunkMesh.subMeshCount = _subMeshes.Count;
            for (int i = 0; i < _subMeshes.Count; i++)
            {
                chunkMesh.SetTriangles(_subMeshes[i], i);
            }

            chunkMesh.SetUVs(0, g.Uvs);
            chunkMesh.SetNormals(g.Normals);    // нормали и цвета уже посчитаны в генераторе
            chunkMesh.SetColors(g.Colors);
            chunkMesh.RecalculateBounds();

            meshRenderer.sharedMaterials = _activeMats.ToArray();

            // 1. Коллизия для физики ходьбы (без саженцев)
            CollisionEnabled = collision;
            if (solidCollider != null)
            {
                if (!_solidApplied || g.SolidHash != _appliedSolidHash)
                {
                    solidCollisionMesh.Clear();
                    solidCollisionMesh.SetVertices(g.Vertices);
                    solidCollisionMesh.SetTriangles(g.CollisionTriangles, 0);

                    _appliedSolidHash = g.SolidHash;
                    _solidApplied = true;
                    _solidDirty = true;
                }
                BindSolidCollider();
            }

            // 2. Невидимый хитбокс для Raycast (саженцы)
            if (triggerCollider != null && (!_plantApplied || g.PlantHash != _appliedPlantHash))
            {
                triggerCollisionMesh.Clear();
                if (g.PlantTriangles.Count > 0)
                {
                    triggerCollisionMesh.SetVertices(g.Vertices);
                    triggerCollisionMesh.SetTriangles(g.PlantTriangles, 0);

                    triggerCollider.sharedMesh = null;
                    triggerCollider.sharedMesh = triggerCollisionMesh;
                    triggerCollider.convex = true;
                }
                else
                {
                    triggerCollider.sharedMesh = null;
                }

                _appliedPlantHash = g.PlantHash;
                _plantApplied = true;
            }
        }

        // ---------- Физика только рядом с игроком ----------

        // Включить/выключить физику чанка. Запекание MeshCollider дорогое,
        // поэтому WorldManager включает его только у ближних чанков.
        public void SetCollisionEnabled(bool on)
        {
            if (CollisionEnabled == on) return;
            CollisionEnabled = on;
            if (solidCollider != null && _solidApplied) BindSolidCollider();
        }

        private void BindSolidCollider()
        {
            if (CollisionEnabled)
            {
                if (!_solidBound || _solidDirty)
                {
                    solidCollider.sharedMesh = null;
                    solidCollider.sharedMesh = solidCollisionMesh;   // здесь происходит запекание
                    _solidBound = true;
                    _solidDirty = false;
                }
            }
            else if (_solidBound)
            {
                solidCollider.sharedMesh = null;
                _solidBound = false;
            }
        }

        public void ClearMesh()
        {
            BuildTicket++;   // результаты фоновых сборок для этого чанка больше не нужны
            _solidApplied = false;
            _plantApplied = false;
            _solidBound = false;
            _solidDirty = false;
            CollisionEnabled = false;
            DestroyAllLabels();
            if (chunkMesh != null) chunkMesh.Clear();
            if (solidCollisionMesh != null) solidCollisionMesh.Clear();
            if (triggerCollisionMesh != null) triggerCollisionMesh.Clear();
            if (solidCollider != null) solidCollider.sharedMesh = null;
            if (triggerCollider != null) triggerCollider.sharedMesh = null;
        }

        // ---------- Таблички ----------

        // Надписи: создаём для новых табличек, убираем для исчезнувших или повёрнутых
        private void SyncSignLabels(Dictionary<Vector3Int, BlockType> signsNow)
        {
            if (Application.isBatchMode) return;   // на выделенном сервере текст не нужен

            _labelsToRemove.Clear();
            foreach (var kv in _labels)
                if (kv.Value == null || !signsNow.TryGetValue(kv.Key, out BlockType t) || t != kv.Value.Type)
                    _labelsToRemove.Add(kv.Key);

            foreach (var key in _labelsToRemove)
            {
                if (_labels[key] != null) Destroy(_labels[key].gameObject);
                _labels.Remove(key);
            }

            foreach (var kv in signsNow)
                if (!_labels.ContainsKey(kv.Key))
                    _labels[kv.Key] = SignLabel.Create(transform, kv.Key, kv.Value);
        }

        private void DestroyAllLabels()
        {
            foreach (var kv in _labels)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _labels.Clear();
        }
    }
}