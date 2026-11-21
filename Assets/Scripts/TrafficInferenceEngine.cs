using System;
using UnityEngine;
using Unity.InferenceEngine;

namespace TwoLaneSimulation
{
    public class TrafficInferenceEngine : MonoBehaviour
    {
        [Header("Model Configuration")]
        [Tooltip("Gán asset .onnx ModelAsset từ Assets/Models")]
        public ModelAsset modelAsset;

        [Tooltip("Bộ xử lý thực thi suy luận (CPU hoặc GPUCompute)")]
        public BackendType backendType = BackendType.CPU;

        [Header("Input Normalization Constants")]
        public float maxSpeedNorm = 30f;
        public float maxGapNorm = 50f;
        public float maxRelSpeedNorm = 15f;

        [Header("Output Scaling")]
        public float maxPredictedAcceleration = 4.0f;

        private Model _runtimeModel;
        private Worker _worker;

        public bool IsModelReady => _worker != null;

        private void Start()
        {
            InitializeEngine();
        }

        public void InitializeEngine()
        {
            if (modelAsset == null)
            {
#if UNITY_EDITOR
                modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/traffic_aif_agent.onnx");
#endif
            }

            if (modelAsset == null)
            {
                Debug.LogWarning("[TrafficInferenceEngine] Không tìm thấy ModelAsset. Xe sẽ dùng fallback.");
                return;
            }

            try
            {
                _runtimeModel = ModelLoader.Load(modelAsset);
                _worker = new Worker(_runtimeModel, backendType);
                Debug.Log($"[TrafficInferenceEngine] Khởi tạo Inference Engine thành công ({backendType}).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrafficInferenceEngine] Không thể khởi tạo Inference Engine: {ex.Message}");
                _worker = null;
            }
        }

        public float PredictAcceleration(float speed, float gap, float relSpeed)
        {
            if (!IsModelReady) return 0f;

            float normSpeed = Mathf.Clamp01(speed / maxSpeedNorm);
            float normGap = Mathf.Clamp01(gap / maxGapNorm);
            float normRelSpeed = Mathf.Clamp(relSpeed / maxRelSpeedNorm, -1f, 1f);

            float[] rawInputs = new float[] { normSpeed, normGap, normRelSpeed };
            using var inputTensor = new Tensor<float>(new TensorShape(1, 3), rawInputs);

            try
            {
                _worker.Schedule(inputTensor);
                var outputTensor = _worker.PeekOutput() as Tensor<float>;
                if (outputTensor != null)
                {
                    float[] results = outputTensor.DownloadToArray();
                    if (results != null && results.Length > 0)
                    {
                        return Mathf.Clamp(results[0], -maxPredictedAcceleration * 2f, maxPredictedAcceleration);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrafficInferenceEngine] Lỗi suy luận: {ex.Message}");
            }

            return 0f;
        }

        private void OnDestroy()
        {
            _worker?.Dispose();
            _worker = null;
        }
    }
}
