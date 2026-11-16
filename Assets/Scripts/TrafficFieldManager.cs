using UnityEngine;

namespace TwoLaneSimulation
{
    /// <summary>
    /// Quản lý bản đồ mật độ giao thông liên tục (Mean Field Traffic Density)
    /// Cung cấp thông số density nạp vào đầu vào số 9 của mô hình AI.
    /// </summary>
    public class TrafficFieldManager : MonoBehaviour
    {
        public static TrafficFieldManager Instance { get; private set; }

        [Header("Cấu hình Lưới Mật Độ")]
        public int cellCount = 50;
        public float roadLength = 500f;
        private float[] densityMap;

        void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            densityMap = new float[cellCount];
        }

        public void RegisterVehiclePosition(Vector3 worldPos)
        {
            float z = Mathf.Clamp(worldPos.z, 0, roadLength - 0.01f);
            int cellIndex = Mathf.FloorToInt((z / roadLength) * cellCount);
            if (densityMap != null && cellIndex >= 0 && cellIndex < densityMap.Length)
            {
                densityMap[cellIndex] += 1.0f;
            }
        }

        public float GetDensityAt(Vector3 worldPos)
        {
            if (densityMap == null) return 0f;
            float z = Mathf.Clamp(worldPos.z, 0, roadLength - 0.01f);
            int cellIndex = Mathf.FloorToInt((z / roadLength) * cellCount);
            if (cellIndex >= 0 && cellIndex < densityMap.Length)
            {
                return Mathf.Clamp01(densityMap[cellIndex] / 10.0f);
            }
            return 0f;
        }

        void LateUpdate()
        {
            if (densityMap == null) return;
            for (int i = 0; i < cellCount; i++)
            {
                densityMap[i] = Mathf.MoveTowards(densityMap[i], 0f, Time.deltaTime * 5f);
            }
        }
    }
}
