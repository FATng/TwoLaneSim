using System.Collections.Generic;
using UnityEngine;

namespace TwoLaneSimulation
{
    /// <summary>
    /// Quản lý việc sinh người đi bộ qua đường (Pedestrian Spawner):
    /// - Tự động sinh người đi bộ định kỳ tại các vị trí vạch kẻ đường (Zebra Crossing) hoặc ngẫu nhiên dọc đường.
    /// - Người đi bộ có thể xuất phát từ CẢ 2 BÊN ĐƯỜNG (trái sang phải hoặc phải sang trái).
    /// - Hỗ trợ tùy chỉnh tốc độ, độ rộng mặt đường và màu sắc trang phục ngẫu nhiên.
    /// </summary>
    public class PedestrianSpawner : MonoBehaviour
    {
        [Header("Pedestrian Prefab")]
        [Tooltip("Prefab của người qua đường (gán từ Assets/Prefabs/Pedestrian.prefab)")]
        public GameObject pedestrianPrefab;

        [Header("Auto Spawn Settings")]
        [Tooltip("Bật/tắt tự động sinh người qua đường theo chu kỳ")]
        public bool autoSpawn = true;

        [Tooltip("Khoảng thời gian tối thiểu giữa 2 lần sinh (giây)")]
        public float minSpawnInterval = 5.0f;

        [Tooltip("Khoảng thời gian tối đa giữa 2 lần sinh (giây)")]
        public float maxSpawnInterval = 10.0f;

        [Tooltip("Giới hạn số lượng người qua đường đồng thời")]
        public int maxSimultaneousPedestrians = 10;

        [Header("Road & Position Parameters")]
        [Tooltip("Bề rộng mặt đường (m) - 4 làn: 16m")]
        public float roadWidth = 16f;

        [Tooltip("Tọa độ Z tối thiểu trên đường")]
        public float minSpawnZ = 35f;

        [Tooltip("Tọa độ Z tối đa trên đường")]
        public float maxSpawnZ = 460f;

        [Tooltip("Cao độ xuất phát (m)")]
        public float spawnHeightY = 0.9f;

        [Header("Speed Settings")]
        [Tooltip("Tốc độ đi bộ tối thiểu (m/s)")]
        public float minWalkingSpeed = 1.2f;

        [Tooltip("Tốc độ đi bộ tối đa (m/s)")]
        public float maxWalkingSpeed = 1.8f;

        [Header("Designated Zebra Crossings")]
        [Tooltip("Danh sách các vị trí Z của vạch kẻ đường Zebra Crossing")]
        public float[] designatedCrossings = new float[] { 120f, 250f, 380f };

        [Tooltip("Tỉ lệ ưu tiên sinh người tại các vạch kẻ cố định (0.0 đến 1.0)")]
        [Range(0f, 1f)]
        public float designatedCrossingBias = 0.75f;

        private float _nextSpawnTimer = 0f;
        private List<PedestrianAgent> _activePedestrians = new List<PedestrianAgent>();
        private Transform _pedestrianContainer;

        // Bảng màu trang phục người đi bộ
        private static readonly Color[] ClothingPalette = new Color[]
        {
            new Color(0.95f, 0.75f, 0.10f), // Vàng cam ấm
            new Color(0.20f, 0.55f, 0.90f), // Xanh dương
            new Color(0.15f, 0.75f, 0.45f), // Xanh lá cây
            new Color(0.85f, 0.30f, 0.20f), // Cam đỏ
            new Color(0.60f, 0.25f, 0.85f), // Tím thạch anh
            new Color(0.20f, 0.80f, 0.85f), // Xanh ngọc
            new Color(0.90f, 0.90f, 0.90f), // Trắng xám
            new Color(0.35f, 0.35f, 0.40f)  // Xám đậm
        };

        private void Awake()
        {
            GameObject container = new GameObject("PedestrianContainer");
            container.transform.SetParent(transform);
            _pedestrianContainer = container.transform;
        }

        private void Start()
        {
            ScheduleNextSpawn();
        }

        private void Update()
        {
            _activePedestrians.RemoveAll(p => p == null || p.HasCompletedCrossing);

            if (!autoSpawn) return;

            _nextSpawnTimer -= Time.deltaTime;
            if (_nextSpawnTimer <= 0f)
            {
                if (_activePedestrians.Count < maxSimultaneousPedestrians)
                {
                    SpawnPedestrian();
                }
                ScheduleNextSpawn();
            }
        }

        private void ScheduleNextSpawn()
        {
            _nextSpawnTimer = Random.Range(minSpawnInterval, maxSpawnInterval);
        }

        /// <summary>
        /// Sinh ngẫu nhiên một người đi bộ từ một trong hai bên đường
        /// </summary>
        public GameObject SpawnPedestrian()
        {
            if (pedestrianPrefab == null)
            {
                Debug.LogWarning("[PedestrianSpawner] Chưa gán pedestrianPrefab!");
                return null;
            }

            float spawnZ;
            if (designatedCrossings != null && designatedCrossings.Length > 0 && Random.value < designatedCrossingBias)
            {
                int index = Random.Range(0, designatedCrossings.Length);
                spawnZ = designatedCrossings[index] + Random.Range(-1.0f, 1.0f);
            }
            else
            {
                spawnZ = Random.Range(minSpawnZ, maxSpawnZ);
            }

            // Hướng đi: ngẫu nhiên từ trái sang phải (50%) hoặc từ phải sang trái (50%)
            bool fromLeftToRight = (Random.value > 0.5f);
            float speed = Random.Range(minWalkingSpeed, maxWalkingSpeed);

            return SpawnPedestrianAt(spawnZ, fromLeftToRight, speed);
        }

        /// <summary>
        /// Sinh người đi bộ tại vị trí Z chỉ định với hướng và tốc độ tùy chọn
        /// </summary>
        public GameObject SpawnPedestrianAt(float zPosition, bool fromLeftToRight = true, float walkingSpeed = 1.4f)
        {
            if (pedestrianPrefab == null)
            {
                Debug.LogWarning("[PedestrianSpawner] Chưa gán pedestrianPrefab!");
                return null;
            }

            float halfRoad = roadWidth * 0.5f;
            int direction = fromLeftToRight ? 1 : -1;
            // Xuất phát cách mép đường 1.0m
            float startX = fromLeftToRight ? (-halfRoad - 1.0f) : (halfRoad + 1.0f);

            Vector3 spawnPos = new Vector3(startX, spawnHeightY, zPosition);
            Quaternion spawnRot = Quaternion.Euler(0f, fromLeftToRight ? 90f : -90f, 0f);

            GameObject pedObj = Instantiate(pedestrianPrefab, spawnPos, spawnRot, _pedestrianContainer);
            pedObj.name = $"Pedestrian_{_activePedestrians.Count + 1}_{(fromLeftToRight ? "L2R" : "R2L")}_{zPosition:F0}m";

            PedestrianAgent agent = pedObj.GetComponent<PedestrianAgent>();
            if (agent == null)
            {
                agent = pedObj.AddComponent<PedestrianAgent>();
            }

            agent.Initialize(spawnPos, direction, walkingSpeed, roadWidth);

            Color randomClothing = ClothingPalette[Random.Range(0, ClothingPalette.Length)];
            agent.SetRandomColor(randomClothing);

            _activePedestrians.Add(agent);

            return pedObj;
        }

        public void ClearAllPedestrians()
        {
            foreach (var ped in _activePedestrians)
            {
                if (ped != null) Destroy(ped.gameObject);
            }
            _activePedestrians.Clear();
        }

        private void OnDrawGizmosSelected()
        {
            if (designatedCrossings == null) return;
            Gizmos.color = Color.yellow;
            foreach (var z in designatedCrossings)
            {
                Vector3 center = new Vector3(0f, 0.05f, z);
                Gizmos.DrawWireCube(center, new Vector3(roadWidth, 0.1f, 4.0f));
            }
        }
    }
}
