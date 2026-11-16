using UnityEngine;
using Unity.InferenceEngine;
using Random = UnityEngine.Random;

namespace TwoLaneSimulation
{
    public enum DrivingMode
    {
        FreeFlow,        // Đường thông thoáng: duy trì vận tốc và đi thẳng ổn định
        CarFollowing,    // Bám đuôi xe trước: điều tiết cự ly và tốc độ bám đuôi an toàn
        ActiveEvasion,   // Chủ động lách né: vượt chướng ngại vật/người đi bộ sang khoảng trống
        SafeYielding,    // Dừng hoặc giảm tốc nhường đường cho người đi bộ
        EmergencyShield  // Lớp bảo hiểm phanh khẩn cấp & tách va chạm vật lý
    }

    /// <summary>
    /// Điều khiển phương tiện giao thông tự hành bằng mạng nơ-ron sâu đa nhiệm (Multi-Head Neural Network)
    /// Hỗ trợ hoàn hảo đường 4 làn xe chạy NGƯỢC CHIỀU NHAU (Làn trái -Z, Làn phải +Z).
    /// Suy luận thời gian thực qua Unity Sentis (.onnx model).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class AIFVehicleAgent : MonoBehaviour
    {
        [Header("Sentis ONNX Model")]
        [SerializeField] private ModelAsset onnxModelAsset;
        [SerializeField] private float targetSpeed = 15f;
        [SerializeField] private float maxAcceleration = 8f;

        [Header("Hierarchical Architecture (Kiến trúc phân tầng)")]
        [SerializeField] private DrivingMode currentDrivingMode = DrivingMode.FreeFlow;
        public DrivingMode CurrentDrivingMode => currentDrivingMode;
        [SerializeField] private float aiSteeringOutput = 0f;
        [SerializeField] private float aiAccelerationOutput = 0f;
        private float smoothedSteering = 0f;

        [Header("Driver Personality (Độ Vội)")]
        [Range(0.6f, 1.6f)]
        [Tooltip("Độ vội vàng của tài xế: quyết định tốc độ tối đa và cự ly bám đuôi an toàn")]
        public float hurryFactor = 1.0f;

        [Header("Direction & Road Boundaries (Hỗ trợ 2 chiều ngược nhau)")]
        [Tooltip("+1 nếu xe chạy về phía +Z (làn phải), -1 nếu xe chạy về phía -Z (làn trái)")]
        public int drivingDirection = 1;
        public float roadSectionCenter = 4.0f;
        public float minSafeX = 0.6f;
        public float maxSafeX = 7.4f;
        public float fixedHeightY = 0.5f;

        [Header("Heading Angle Constraint")]
        [Range(5f, 45f)]
        public float maxHeadingAngle = 18f;

        [Header("Runtime State")]
        [SerializeField] public float currentSpeed = 0f;
        public float CurrentSpeed => currentSpeed;
        [SerializeField] private float effectiveTargetSpeed = 15f;
        [SerializeField] private bool isFrontCarBraking = false;
        [SerializeField] private TurnSignal frontCarTurnSignal = TurnSignal.None;
        [SerializeField] private bool isOpenRoadAhead = false;

        [Header("Visual & Audio Controllers")]
        [SerializeField] private CarLightsController lightsController;
        [SerializeField] private CarCollisionVisualizer collisionVisualizer;

        [Header("Lane & Evasion Settings")]
        [Tooltip("Các làn đường được phép di chuyển")]
        public float[] allowedLanes = new float[] { 2.0f, 6.0f };
        [SerializeField] private float targetLaneX = 0f;
        [SerializeField] private bool isChangingLane = false;
        private float laneChangeCooldownTimer = 0f;

        [Header("Sensor Parameters")]
        public float sideScanDistance = 2.8f;
        public float vehicleSeparationSpeed = 1.2f;
        public float curbSeparationSpeed = 2.5f;
        public float crashBrakeDecel = 12.0f;

        private bool isCollidingWithVehicle = false;
        private bool isCollidingWithBarrier = false;
        private Vector3 vehicleSeparationVector = Vector3.zero;
        private Vector3 barrierSeparationVector = Vector3.zero;
        private float vehicleCollisionTimer = 0f;
        private float barrierCollisionTimer = 0f;
        private float pedestrianCollisionPauseTimer = 0f;
        private float postPedestrianCollisionGraceTimer = 0f;

        private Model runtimeModel;
        private Worker worker;
        private Rigidbody rb;
        private static int instanceCounter = 0;
        private int myId;
        private float spawnTime = 0f;
        private float baseYaw = 0f;

        void Awake()
        {
            myId = instanceCounter++;
            rb = GetComponent<Rigidbody>();
        }

        void Start()
        {
            spawnTime = Time.time;

            // Tự động nhận diện hướng chạy từ góc quay hoặc vị trí ban đầu
            float forwardZ = transform.forward.z;
            if (forwardZ < -0.2f || transform.position.x < 0f)
            {
                drivingDirection = -1; // Chạy ngược chiều -Z trên phần đường bên trái (X < 0)
                baseYaw = 180f;
                roadSectionCenter = -4.0f;
                minSafeX = -7.4f;
                maxSafeX = -0.6f;
                if (allowedLanes == null || allowedLanes.Length == 0 || allowedLanes[0] > 0)
                {
                    allowedLanes = new float[] { -6.0f, -2.0f };
                }
            }
            else
            {
                drivingDirection = 1; // Chạy xuôi chiều +Z trên phần đường bên phải (X > 0)
                baseYaw = 0f;
                roadSectionCenter = 4.0f;
                minSafeX = 0.6f;
                maxSafeX = 7.4f;
                if (allowedLanes == null || allowedLanes.Length == 0 || allowedLanes[0] < 0)
                {
                    allowedLanes = new float[] { 2.0f, 6.0f };
                }
            }

            if (rb != null)
            {
                rb.mass = 1500f;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
                Vector3 initPos = rb.position;
                initPos.y = fixedHeightY;
                rb.position = initPos;
                rb.rotation = Quaternion.Euler(0f, baseYaw, 0f);
            }

            if (lightsController == null) lightsController = GetComponent<CarLightsController>();
            if (collisionVisualizer == null) collisionVisualizer = GetComponent<CarCollisionVisualizer>();

            if (Mathf.Approximately(hurryFactor, 1.0f))
            {
                hurryFactor = Random.Range(0.75f, 1.35f);
            }
            effectiveTargetSpeed = targetSpeed * Mathf.Lerp(1.0f, hurryFactor, 0.6f);

            targetLaneX = transform.position.x;
            laneChangeCooldownTimer = Random.Range(2.0f, 5.0f);

            // Gán vật liệu cứng chống ma sát dính
            Collider[] allCols = GetComponentsInChildren<Collider>();
            PhysicsMaterial solidMat = new PhysicsMaterial("VehiclePhysicsMat")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (var c in allCols)
            {
                if (c != null) c.material = solidMat;
            }

            currentSpeed = 6.0f;

#if UNITY_EDITOR
            if (onnxModelAsset == null)
            {
                onnxModelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/traffic_aif_agent.onnx");
            }
#endif

            if (onnxModelAsset != null)
            {
                try
                {
                    runtimeModel = ModelLoader.Load(onnxModelAsset);
                    worker = new Worker(runtimeModel, BackendType.CPU);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[AIFVehicleAgent] Không thể khởi tạo worker Sentis: {ex.Message}");
                    worker = null;
                }
            }
        }

        public (DrivingMode mode, float accel, float steer) PredictAIPolicy(
            float speed,
            float gap,
            float relSpeed,
            float targetSpd,
            float roadOffset,
            float distLeft,
            float distRight,
            bool isLeaderBraking,
            bool isPedAhead)
        {
            if (worker == null)
            {
                float fallbackAccel = (gap < 4.0f) ? -4.0f : ((speed < targetSpd) ? 1.5f : 0f);
                return (DrivingMode.FreeFlow, fallbackAccel, 0f);
            }

            float normSpeed = Mathf.Clamp01(speed / 30f);
            float normGap = Mathf.Clamp01(gap / 50f);
            float normRelSpeed = Mathf.Clamp(relSpeed / 15f, -1f, 1f);
            float speedError = Mathf.Clamp((targetSpd - speed) / Mathf.Max(1f, targetSpd), -1f, 1f);
            float normOffset = Mathf.Clamp(roadOffset, -1f, 1f);
            float sideBalance = Mathf.Clamp((distRight - distLeft) / 5f, -1f, 1f);
            float leaderBrakeInput = isLeaderBraking ? 1.0f : 0.0f;
            float pedInput = isPedAhead ? 1.0f : 0.0f;
            float trafficDensity = (TrafficFieldManager.Instance != null) ? TrafficFieldManager.Instance.GetDensityAt(transform.position) : 0f;

            float[] rawInputs = new float[] {
                normSpeed, normGap, normRelSpeed, speedError, normOffset, sideBalance, leaderBrakeInput, pedInput, trafficDensity
            };
            using var inputTensor = new Tensor<float>(new TensorShape(1, 9), rawInputs);

            try
            {
                worker.Schedule(inputTensor);
                var outputTensor = worker.PeekOutput() as Tensor<float>;
                if (outputTensor != null)
                {
                    float[] results = outputTensor.DownloadToArray();
                    if (results != null && results.Length >= 7)
                    {
                        int bestModeIdx = 0;
                        float maxLogit = results[0];
                        for (int i = 1; i < 5; i++)
                        {
                            if (results[i] > maxLogit)
                            {
                                maxLogit = results[i];
                                bestModeIdx = i;
                            }
                        }
                        DrivingMode predictedMode = (DrivingMode)bestModeIdx;
                        float accel = Mathf.Clamp(results[5] * maxAcceleration, -8.0f, 4.0f);
                        float steer = Mathf.Clamp(results[6] * 0.45f, -0.45f, 0.45f);
                        return (predictedMode, accel, steer);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AIFVehicleAgent] Lỗi suy luận AI Sentis: {ex.Message}");
            }

            return (DrivingMode.FreeFlow, 0f, 0f);
        }

        void FixedUpdate()
        {
            if (rb == null) return;
            float dt = Time.fixedDeltaTime;

            if (pedestrianCollisionPauseTimer > 0f)
            {
                pedestrianCollisionPauseTimer -= dt;
                currentSpeed = 0f;
                rb.linearVelocity = Vector3.zero;
                if (lightsController != null) lightsController.SetBraking(true);
                if (pedestrianCollisionPauseTimer <= 0f) postPedestrianCollisionGraceTimer = 2.5f;
                return;
            }

            if (postPedestrianCollisionGraceTimer > 0f) postPedestrianCollisionGraceTimer -= dt;
            if (laneChangeCooldownTimer > 0f) laneChangeCooldownTimer -= dt;

            effectiveTargetSpeed = targetSpeed * Mathf.Lerp(1.0f, hurryFactor, 0.6f);

            // Độ lệch đường chuẩn hóa từ góc nhìn tài xế
            float halfSectionWidth = 3.4f;
            float rawOffset = (transform.position.x - roadSectionCenter) / halfSectionWidth;
            float normalizedRoadOffset = Mathf.Clamp(rawOffset * drivingDirection, -1f, 1f);

            if (TrafficFieldManager.Instance != null)
            {
                TrafficFieldManager.Instance.RegisterVehiclePosition(transform.position);
            }

            // ===================================================================================
            // HỆ THỐNG CẢM BIẾN QUANG HỌC TOÀN DIỆN (CHÂN THẬT THEO HƯỚNG XE CHẠY)
            // ===================================================================================
            float distanceToFront = 60f;
            isFrontCarBraking = false;
            frontCarTurnSignal = TurnSignal.None;
            GameObject frontVehicleObj = null;

            bool isPedestrianInPath = false;
            float nearestPedDist = 60f;
            float nearestPedRelLateral = 0f;
            int nearestPedDir = 0;

            Vector3 carCenter = new Vector3(transform.position.x, fixedHeightY, transform.position.z);
            Vector3 frontBumperCenter = carCenter + transform.forward * 2.05f;

            // 9 tia thẳng phía cản trước
            float[] frontOffsets = new float[] { 0f, -0.20f, 0.20f, -0.40f, 0.40f, -0.60f, 0.60f, -0.80f, 0.80f };
            for (int r = 0; r < frontOffsets.Length; r++)
            {
                Vector3 origin = frontBumperCenter + transform.right * frontOffsets[r];
                if (Physics.Raycast(origin, transform.forward, out RaycastHit hit, 50f))
                {
                    if (hit.collider.gameObject != gameObject)
                    {
                        bool isVeh = hit.collider.CompareTag("Vehicle") || hit.collider.GetComponentInParent<AIFVehicleAgent>() != null;
                        bool isPed = hit.collider.CompareTag("Pedestrian") || hit.collider.GetComponentInParent<PedestrianAgent>() != null;

                        // Tọa độ tương đối theo chiều ngang của xe
                        Vector3 hitLocal = transform.InverseTransformPoint(hit.point);

                        if (isVeh && Mathf.Abs(hitLocal.x) <= 1.05f && hit.distance < distanceToFront)
                        {
                            distanceToFront = hit.distance;
                            frontVehicleObj = hit.collider.gameObject;
                        }
                        else if (isPed && hit.distance < nearestPedDist)
                        {
                            isPedestrianInPath = true;
                            nearestPedDist = hit.distance;
                            nearestPedRelLateral = hitLocal.x;
                            var pAgent = hit.collider.GetComponentInParent<PedestrianAgent>();
                            if (pAgent != null) nearestPedDir = pAgent.crossingDirection;
                        }
                    }
                }
            }

            // 8 tia chéo đón đầu
            bool hasCarOnLeftFrontDiag = false;
            bool hasCarOnRightFrontDiag = false;

            float[] diagAngles = new float[] { -60f, -45f, -30f, -15f, 15f, 30f, 45f, 60f };
            for (int d = 0; d < diagAngles.Length; d++)
            {
                Vector3 diagDir = Quaternion.Euler(0f, diagAngles[d], 0f) * transform.forward;
                if (Physics.Raycast(frontBumperCenter, diagDir, out RaycastHit diagHit, 30f))
                {
                    if (diagHit.collider.gameObject != gameObject)
                    {
                        bool isPed = diagHit.collider.CompareTag("Pedestrian") || diagHit.collider.GetComponentInParent<PedestrianAgent>() != null;
                        bool isVeh = diagHit.collider.CompareTag("Vehicle") || diagHit.collider.GetComponentInParent<AIFVehicleAgent>() != null;

                        if (isPed)
                        {
                            Vector3 hitLocal = transform.InverseTransformPoint(diagHit.point);
                            if (hitLocal.z > 0.5f && hitLocal.z < 30f)
                            {
                                var pedAgent = diagHit.collider.GetComponentInParent<PedestrianAgent>();
                                int pedDir = (pedAgent != null) ? pedAgent.crossingDirection : (hitLocal.x > 0 ? -1 : 1);
                                float pedSpeed = (pedAgent != null) ? pedAgent.walkingSpeed : 1.4f;

                                float timeToMeet = hitLocal.z / Mathf.Max(2.0f, currentSpeed);
                                float predictedPedX = diagHit.point.x + (pedDir * pedSpeed * timeToMeet);
                                float predictedRelX = (predictedPedX - transform.position.x) * drivingDirection;

                                if (Mathf.Abs(predictedRelX) < 2.5f || Mathf.Abs(hitLocal.x) < 2.2f)
                                {
                                    isPedestrianInPath = true;
                                    if (hitLocal.z < nearestPedDist)
                                    {
                                        nearestPedDist = hitLocal.z;
                                        nearestPedRelLateral = hitLocal.x;
                                        nearestPedDir = pedDir;
                                    }
                                }
                            }
                        }
                        else if (isVeh && diagHit.distance < 8.0f)
                        {
                            if (diagAngles[d] < 0f) hasCarOnLeftFrontDiag = true;
                            else hasCarOnRightFrontDiag = true;
                        }
                    }
                }
            }

            // Quét sườn xe 2 bên
            float distToCarLeft = 50f;
            float distToCarRight = 50f;
            float halfWidth = 0.95f;
            float[] sideOffsets = new float[] { 1.8f, 0.6f, -0.6f, -1.8f };

            for (int s = 0; s < sideOffsets.Length; s++)
            {
                Vector3 leftOrigin = carCenter + transform.forward * sideOffsets[s] - transform.right * halfWidth;
                if (Physics.Raycast(leftOrigin, -transform.right, out RaycastHit hitL, sideScanDistance))
                {
                    if (hitL.collider.gameObject != gameObject && (hitL.collider.CompareTag("Vehicle") || hitL.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                    {
                        if (hitL.distance < distToCarLeft) distToCarLeft = hitL.distance;
                    }
                }

                Vector3 rightOrigin = carCenter + transform.forward * sideOffsets[s] + transform.right * halfWidth;
                if (Physics.Raycast(rightOrigin, transform.right, out RaycastHit hitR, sideScanDistance))
                {
                    if (hitR.collider.gameObject != gameObject && (hitR.collider.CompareTag("Vehicle") || hitR.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                    {
                        if (hitR.distance < distToCarRight) distToCarRight = hitR.distance;
                    }
                }
            }

            // Quét hành lang bên trái và phải (trong phạm vi phần đường an toàn của xe)
            float currentPosX = transform.position.x;
            float lateralCorridorOffset = 2.0f;
            float leftCorridorX = currentPosX - lateralCorridorOffset * drivingDirection;
            float rightCorridorX = currentPosX + lateralCorridorOffset * drivingDirection;

            bool isLeftCorridorInBounds = (leftCorridorX >= minSafeX + 0.35f && leftCorridorX <= maxSafeX - 0.35f);
            bool isRightCorridorInBounds = (rightCorridorX >= minSafeX + 0.35f && rightCorridorX <= maxSafeX - 0.35f);

            float distLeftLaneAhead = 50f;
            float distLeftLaneBehind = 50f;
            float distRightLaneAhead = 50f;
            float distRightLaneBehind = 50f;

            if (isLeftCorridorInBounds)
            {
                Vector3 leftOrigin = new Vector3(leftCorridorX, fixedHeightY, transform.position.z);
                if (Physics.Raycast(leftOrigin, transform.forward, out RaycastHit hitLAhead, 35f))
                {
                    if (hitLAhead.collider.gameObject != gameObject && (hitLAhead.collider.CompareTag("Vehicle") || hitLAhead.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                        distLeftLaneAhead = hitLAhead.distance;
                }
                if (Physics.Raycast(leftOrigin, -transform.forward, out RaycastHit hitLBehind, 22f))
                {
                    if (hitLBehind.collider.gameObject != gameObject && (hitLBehind.collider.CompareTag("Vehicle") || hitLBehind.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                        distLeftLaneBehind = hitLBehind.distance;
                }
            }

            if (isRightCorridorInBounds)
            {
                Vector3 rightOrigin = new Vector3(rightCorridorX, fixedHeightY, transform.position.z);
                if (Physics.Raycast(rightOrigin, transform.forward, out RaycastHit hitRAhead, 35f))
                {
                    if (hitRAhead.collider.gameObject != gameObject && (hitRAhead.collider.CompareTag("Vehicle") || hitRAhead.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                        distRightLaneAhead = hitRAhead.distance;
                }
                if (Physics.Raycast(rightOrigin, -transform.forward, out RaycastHit hitRBehind, 22f))
                {
                    if (hitRBehind.collider.gameObject != gameObject && (hitRBehind.collider.CompareTag("Vehicle") || hitRBehind.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                        distRightLaneBehind = hitRBehind.distance;
                }
            }

            // Vận tốc và trạng thái xe phía trước
            float leaderSpeed = 0f;
            bool isLeaderStopped = false;

            if (frontVehicleObj != null)
            {
                AIFVehicleAgent leaderAgent = frontVehicleObj.GetComponentInParent<AIFVehicleAgent>();
                if (leaderAgent != null) leaderSpeed = leaderAgent.currentSpeed;
                else
                {
                    Rigidbody lRb = frontVehicleObj.GetComponentInParent<Rigidbody>();
                    if (lRb != null) leaderSpeed = lRb.linearVelocity.magnitude;
                }

                CarLightsController frontLights = frontVehicleObj.GetComponentInParent<CarLightsController>();
                if (frontLights != null)
                {
                    isFrontCarBraking = frontLights.IsBraking;
                    frontCarTurnSignal = frontLights.CurrentSignal;
                }

                if (leaderSpeed < 2.0f) isLeaderStopped = true;
            }

            isOpenRoadAhead = (distanceToFront >= 16f && !isPedestrianInPath);

            // ===================================================================================
            // 2. PHÂN TÍCH VẬT CẢN & LỰA CHỌN LÀCH NÉ AN TOÀN TRONG PHẦN ĐƯỜNG RIÊNG
            // ===================================================================================
            bool hasPedestrianObstacle = (isPedestrianInPath && nearestPedDist < 26.0f);
            bool hasStoppedVehicleObstacle = (frontVehicleObj != null && isLeaderStopped && distanceToFront < 18.0f);
            bool hasSlowVehicleObstacle = (frontVehicleObj != null && !isLeaderStopped && distanceToFront < 14.0f && leaderSpeed < effectiveTargetSpeed * 0.70f);
            bool hasObstacleAhead = hasPedestrianObstacle || hasStoppedVehicleObstacle || hasSlowVehicleObstacle;

            bool canGoLeft = isLeftCorridorInBounds && (distToCarLeft >= 0.50f) && !hasCarOnLeftFrontDiag && (distLeftLaneAhead > 7.0f) && (distLeftLaneBehind > 5.0f);
            bool canGoRight = isRightCorridorInBounds && (distToCarRight >= 0.50f) && !hasCarOnRightFrontDiag && (distRightLaneAhead > 7.0f) && (distRightLaneBehind > 5.0f);

            if (hasObstacleAhead && !isChangingLane && laneChangeCooldownTimer <= 0f)
            {
                if (hasPedestrianObstacle)
                {
                    // Khi người đi bộ ở quá gần: dừng lại nhường đường, giữ hướng đi thẳng
                    targetLaneX = currentPosX;
                }
                else
                {
                    // Lách né xe dừng / chạy chậm sang khoảng trống an toàn
                    if (canGoLeft && canGoRight)
                    {
                        float chosenShift = (distLeftLaneAhead >= distRightLaneAhead) ? -lateralCorridorOffset : lateralCorridorOffset;
                        targetLaneX = Mathf.Clamp(currentPosX + chosenShift * drivingDirection, minSafeX + 0.4f, maxSafeX - 0.4f);
                        isChangingLane = true;
                        laneChangeCooldownTimer = 2.0f;
                    }
                    else if (canGoLeft)
                    {
                        targetLaneX = Mathf.Clamp(currentPosX - lateralCorridorOffset * drivingDirection, minSafeX + 0.4f, maxSafeX - 0.4f);
                        isChangingLane = true;
                        laneChangeCooldownTimer = 2.0f;
                    }
                    else if (canGoRight)
                    {
                        targetLaneX = Mathf.Clamp(currentPosX + lateralCorridorOffset * drivingDirection, minSafeX + 0.4f, maxSafeX - 0.4f);
                        isChangingLane = true;
                        laneChangeCooldownTimer = 2.0f;
                    }
                }
            }
            else if (!isChangingLane)
            {
                targetLaneX = currentPosX;
            }

            if (isChangingLane)
            {
                if (Mathf.Abs(currentPosX - targetLaneX) < 0.25f)
                {
                    isChangingLane = false;
                    targetLaneX = currentPosX;
                    if (lightsController != null) lightsController.SetTurnSignal(TurnSignal.None);
                }
                else if (lightsController != null)
                {
                    float lateralDiff = (targetLaneX - currentPosX) * drivingDirection;
                    lightsController.SetTurnSignal(lateralDiff < 0 ? TurnSignal.Left : TurnSignal.Right);
                }
            }

            // ===================================================================================
            // 3. SUY LUẬN AI TỪ MÔ HÌNH SENTIS (.ONNX)
            // ===================================================================================
            float effectiveGap = hasPedestrianObstacle ? nearestPedDist : (frontVehicleObj != null ? distanceToFront : 50f);
            float relSpeed = hasPedestrianObstacle ? -currentSpeed : (frontVehicleObj != null ? (leaderSpeed - currentSpeed) : 0f);

            var (aiSelectedMode, predictedAccel, predictedSteer) = PredictAIPolicy(
                currentSpeed,
                effectiveGap,
                relSpeed,
                effectiveTargetSpeed,
                normalizedRoadOffset,
                distToCarLeft,
                distToCarRight,
                isFrontCarBraking,
                hasPedestrianObstacle
            );

            currentDrivingMode = aiSelectedMode;
            aiAccelerationOutput = predictedAccel;
            aiSteeringOutput = predictedSteer;

            float openRoadBoost = (isOpenRoadAhead && hurryFactor > 1.0f) ? (1.0f + (hurryFactor - 1.0f) * 0.7f) : 1.0f;
            float effectiveMaxAccel = maxAcceleration * Mathf.Sqrt(Mathf.Clamp(hurryFactor, 0.6f, 1.6f)) * openRoadBoost;
            float accel = predictedAccel * Mathf.Sqrt(Mathf.Clamp(hurryFactor, 0.7f, 1.4f));

            if (currentSpeed >= effectiveTargetSpeed) accel = Mathf.Min(accel, 0f);

            // TẦNG 3: BẢO HIỂM AN TOÀN TUYỆT ĐỐI (SAFETY SUPERVISOR)
            if (hasPedestrianObstacle && !isChangingLane && nearestPedDist < 4.0f)
            {
                currentDrivingMode = DrivingMode.SafeYielding;
                accel = -crashBrakeDecel;
                if (lightsController != null) lightsController.SetBraking(true);
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 20.0f * dt);
            }
            else if (frontVehicleObj != null && distanceToFront < 3.2f)
            {
                currentDrivingMode = DrivingMode.EmergencyShield;
                accel = -crashBrakeDecel;
                if (lightsController != null) lightsController.SetBraking(true);

                if (distanceToFront < 2.0f)
                {
                    currentSpeed = Mathf.Min(currentSpeed, leaderSpeed);
                    if (currentSpeed > 0.2f && leaderSpeed <= 0.1f)
                    {
                        currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 25.0f * dt);
                    }
                }
                else if (currentSpeed > leaderSpeed)
                {
                    currentSpeed = Mathf.MoveTowards(currentSpeed, leaderSpeed, 18.0f * dt);
                }
            }

            float throttle = Mathf.Clamp(accel / Mathf.Max(1f, effectiveMaxAccel), -1f, 1f);
            currentSpeed = Mathf.Clamp(currentSpeed + accel * dt, 0f, effectiveTargetSpeed * 1.15f);

            // ===================================================================================
            // 4. ĐIỀU KHIỂN ĐÁNH LÁI KHÔNG GIAN LIÊN TỤC & TÁCH NHAU RA
            // ===================================================================================
            float lateralOffset = (targetLaneX - currentPosX) * drivingDirection;
            float steering = 0f;
            float parallelSeparationLateral = 0f;

            if (distToCarRight < 0.50f)
            {
                float urgency = (0.50f - distToCarRight) / 0.50f;
                steering = -urgency * 0.45f;
                parallelSeparationLateral = -urgency * 1.5f;
            }
            else if (distToCarLeft < 0.50f)
            {
                float urgency = (0.50f - distToCarLeft) / 0.50f;
                steering = urgency * 0.45f;
                parallelSeparationLateral = urgency * 1.5f;
            }
            else if (isChangingLane)
            {
                steering = Mathf.Clamp(lateralOffset * 0.80f + aiSteeringOutput * 0.40f, -0.65f, 0.65f);
            }
            else
            {
                steering = Mathf.Clamp(aiSteeringOutput * 0.35f, -0.20f, 0.20f);
                if (Mathf.Abs(steering) < 0.02f) steering = 0f;
            }

            smoothedSteering = Mathf.Lerp(smoothedSteering, steering, 12.0f * dt);
            steering = smoothedSteering;

            // Xoay hướng xe quanh góc chuẩn baseYaw (0 độ hoặc 180 độ)
            float currentYaw = transform.eulerAngles.y;
            float desiredYaw = baseYaw + Mathf.Clamp(steering * maxHeadingAngle, -maxHeadingAngle, maxHeadingAngle);
            float newYaw = Mathf.MoveTowardsAngle(currentYaw, desiredYaw, 45f * dt);

            Quaternion safeRot = Quaternion.Euler(0f, newYaw, 0f);
            transform.rotation = safeRot;
            if (rb != null)
            {
                rb.rotation = safeRot;
                rb.angularVelocity = Vector3.zero;
            }

            // Vận tốc chuyển động
            Vector3 targetVelocity = transform.forward * currentSpeed;
            targetVelocity.y = 0f;

            if (Mathf.Abs(parallelSeparationLateral) > 0.01f)
            {
                targetVelocity += transform.right * parallelSeparationLateral;
            }

            // Áp dụng lực tách va chạm
            if (vehicleCollisionTimer > 0f) vehicleCollisionTimer -= dt;
            else { isCollidingWithVehicle = false; vehicleSeparationVector = Vector3.zero; }

            if (barrierCollisionTimer > 0f) barrierCollisionTimer -= dt;
            else { isCollidingWithBarrier = false; barrierSeparationVector = Vector3.zero; }

            if (isCollidingWithVehicle)
            {
                targetVelocity += vehicleSeparationVector * vehicleSeparationSpeed;
            }
            if (isCollidingWithBarrier)
            {
                targetVelocity += barrierSeparationVector * curbSeparationSpeed;
            }

            rb.linearVelocity = targetVelocity;

            // Khống chế cao độ Y và giới hạn biên an toàn phần đường
            Vector3 curPos = rb.position;
            curPos.y = fixedHeightY;
            curPos.x = Mathf.Clamp(curPos.x, minSafeX, maxSafeX);
            rb.position = curPos;

            // Hệ thống đèn
            if (lightsController != null)
            {
                bool isBrakingNow = (throttle < -0.05f) || (distanceToFront < 6f && currentSpeed > 0.5f) || (collisionVisualizer != null && collisionVisualizer.IsInCrashState);
                lightsController.SetBraking(isBrakingNow);
            }

            // Vòng lặp tuần hoàn đoạn đường 500m
            if (drivingDirection == 1 && transform.position.z >= 490f)
            {
                transform.position = new Vector3(targetLaneX, fixedHeightY, 10f);
                currentSpeed = 6f;
                rb.linearVelocity = transform.forward * 6f;
            }
            else if (drivingDirection == -1 && transform.position.z <= 10f)
            {
                transform.position = new Vector3(targetLaneX, fixedHeightY, 490f);
                currentSpeed = 6f;
                rb.linearVelocity = transform.forward * 6f;
            }
        }

        void LateUpdate()
        {
            // Bảo hiểm góc quay luôn trong phạm vi cho phép quanh baseYaw
            float yaw = transform.eulerAngles.y;
            float relAngle = Mathf.DeltaAngle(baseYaw, yaw);
            if (Mathf.Abs(relAngle) > maxHeadingAngle)
            {
                float clampedAngle = Mathf.Clamp(relAngle, -maxHeadingAngle, maxHeadingAngle);
                Quaternion safeRot = Quaternion.Euler(0f, baseYaw + clampedAngle, 0f);
                transform.rotation = safeRot;
                if (rb != null)
                {
                    rb.rotation = safeRot;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        private void OnCollisionEnter(Collision collision) => ProcessCollision(collision);
        private void OnCollisionStay(Collision collision) => ProcessCollision(collision);
        private void OnCollisionExit(Collision collision)
        {
            GameObject other = collision.gameObject;
            if (other.CompareTag("Vehicle") || other.GetComponentInParent<AIFVehicleAgent>() != null)
            {
                isCollidingWithVehicle = false;
                vehicleSeparationVector = Vector3.zero;
            }
            if (other.name.ToLower().Contains("guardrail") || other.name.ToLower().Contains("barrier"))
            {
                isCollidingWithBarrier = false;
                barrierSeparationVector = Vector3.zero;
            }
        }

        private void ProcessCollision(Collision collision)
        {
            if (Time.time - spawnTime < 1.0f) return;
            GameObject other = collision.gameObject;

            if (other.CompareTag("Pedestrian") || other.GetComponentInParent<PedestrianAgent>() != null)
            {
                pedestrianCollisionPauseTimer = 3.5f;
                currentSpeed = 0f;
                if (rb != null) rb.linearVelocity = Vector3.zero;
                if (lightsController != null) lightsController.SetBraking(true);
                if (collisionVisualizer != null) collisionVisualizer.TriggerCrash(3.5f);

                IgnoreCollisionWithObject(other, 6.0f);
                var pedAgent = other.GetComponentInParent<PedestrianAgent>();
                if (pedAgent != null)
                {
                    pedAgent.TriggerCrash(3.5f, gameObject);
                    pedAgent.IgnoreCollisionWithVehicle(gameObject, 6.0f);
                }
                return;
            }

            if (other.CompareTag("Vehicle") || other.GetComponentInParent<AIFVehicleAgent>() != null)
            {
                isCollidingWithVehicle = true;
                vehicleCollisionTimer = 0.2f;
                if (collisionVisualizer != null) collisionVisualizer.TriggerCrash(3.0f);

                Vector3 diff = transform.position - other.transform.position;
                diff.y = 0f;
                vehicleSeparationVector = diff.normalized;
                return;
            }

            string name = other.name.ToLower();
            if (name.Contains("road")) return;

            if (name.Contains("guardrail") || name.Contains("barrier") || name.Contains("wall"))
            {
                isCollidingWithBarrier = true;
                barrierCollisionTimer = 0.25f;
                if (collisionVisualizer != null) collisionVisualizer.TriggerCrash(3.0f);

                float curbRepelDir = (transform.position.x > roadSectionCenter) ? -1f : 1f;
                barrierSeparationVector = new Vector3(curbRepelDir, 0f, 0f);
            }
        }

        public void IgnoreCollisionWithObject(GameObject otherObj, float duration)
        {
            if (otherObj == null) return;
            StartCoroutine(IgnoreCollisionRoutine(otherObj, duration));
        }

        private System.Collections.IEnumerator IgnoreCollisionRoutine(GameObject otherObj, float duration)
        {
            if (otherObj == null) yield break;
            Collider[] myCols = GetComponentsInChildren<Collider>();
            Collider[] otherCols = otherObj.GetComponentsInChildren<Collider>();

            foreach (var m in myCols)
                foreach (var o in otherCols)
                    if (m != null && o != null) Physics.IgnoreCollision(m, o, true);

            yield return new WaitForSeconds(duration);

            if (otherObj != null)
            {
                foreach (var m in myCols)
                    foreach (var o in otherCols)
                        if (m != null && o != null) Physics.IgnoreCollision(m, o, false);
            }
        }

        void OnDestroy()
        {
            worker?.Dispose();
            worker = null;
        }
    }
}
