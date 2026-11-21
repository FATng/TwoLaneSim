using UnityEngine;
using Unity.InferenceEngine;
using Random = UnityEngine.Random;

namespace TwoLaneSimulation
{
    public enum DrivingMode
    {
        FreeFlow,
        CarFollowing,
        ActiveEvasion,
        SafeYielding,
        EmergencyShield,
        OppositeOvertake,
        AbortLaneChange,
        CreepForward
    }

    [RequireComponent(typeof(Rigidbody))]
    public class AIFVehicleAgent : MonoBehaviour
    {
        [Header("Sentis ONNX Model")]
        [SerializeField] private ModelAsset onnxModelAsset;
        [SerializeField] public float targetSpeed = 15f;
        [SerializeField] public float maxAcceleration = 8f;

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

        [Header("Opposite Lane Overtake (Lấn làn ngược chiều vượt xe)")]
        [SerializeField] private bool isOvertakingOpposite = false;
        public bool IsOvertakingOpposite => isOvertakingOpposite;
        [SerializeField] private float homeLaneX = 2.0f;
        [SerializeField] private float oppositeClearDist = 60f;
        [SerializeField] private float oppositeClearGap = 1.0f;
        private float oppositeOvertakeCooldownTimer = 0f;

        [Header("Emergency Brake System")]
        [Tooltip("Khoảng cách tối thiểu can thiệp phanh khẩn cấp khi quá gần xe khác (m)")]
        public float minEmergencyBrakeDistance = 2.5f;
        [Tooltip("Gia tốc phanh khẩn cấp can thiệp dừng xe (m/s2)")]
        public float emergencyBrakeDecel = 24.0f;
        [SerializeField] private bool isEmergencyBraking = false;
        public bool IsEmergencyBraking => isEmergencyBraking;

        [Header("Motorcycle Agility Overrides (Xe máy linh hoạt hơn)")]
        [Tooltip("Gia tốc cực đại của xe máy (m/s2) - bốc và tăng tốc nhanh hơn ô tô")]
        public float motorcycleMaxAcceleration = 12.0f;
        [Tooltip("Góc đánh lái tối đa của xe máy (độ) - góc quay cơ động hơn ô tô")]
        public float motorcycleMaxHeadingAngle = 28.0f;

        [Header("Sensor Parameters")]
        public float sideScanDistance = 2.8f;
        public float vehicleSeparationSpeed = 1.2f;
        public float curbSeparationSpeed = 2.5f;
        public float crashBrakeDecel = 20.0f;

        private float vehicleWidth = 1.8f;
        private float vehicleLength = 4.0f;
        private float halfWidth = 0.9f;
        private float halfLength = 2.0f;
        public bool IsMotorcycle => (vehicleWidth < 1.0f);

        private bool isCollidingWithVehicle = false;
        private bool isCollidingWithBarrier = false;
        private Vector3 vehicleSeparationVector = Vector3.zero;
        private Vector3 barrierSeparationVector = Vector3.zero;
        private float vehicleCollisionTimer = 0f;
        private float barrierCollisionTimer = 0f;
        private float pedestrianCollisionPauseTimer = 0f;
        private float postPedestrianCollisionGraceTimer = 0f;

        [Header("Vehicle Collision Hardcode Stop")]
        [Tooltip("Thời gian dừng hoàn toàn của 2 xe khi đâm nhau để triệt tiêu toàn bộ quán tính và lực đẩy (giây). Mặc định 2s theo yêu cầu.")]
        public float vehicleCrashStopDuration = 2.0f;
        private float vehicleCrashPauseTimer = 0f;
        private float postVehicleCollisionGraceTimer = 0f;

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

            vehicleWidth = transform.lossyScale.x;
            vehicleLength = transform.lossyScale.z;
            halfWidth = vehicleWidth * 0.5f;
            halfLength = vehicleLength * 0.5f;

            float forwardZ = transform.forward.z;
            if (forwardZ < -0.2f || transform.position.x < 0f)
            {
                drivingDirection = -1;
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
                drivingDirection = 1;
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
                if (rb.mass <= 1f) rb.mass = IsMotorcycle ? 300f : 1500f;
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
            homeLaneX = (Mathf.Abs(transform.position.x) > 4.0f) ? 6.0f * drivingDirection : 2.0f * drivingDirection;

            if (IsMotorcycle)
            {
                maxAcceleration = motorcycleMaxAcceleration;
                maxHeadingAngle = motorcycleMaxHeadingAngle;
                laneChangeCooldownTimer = Random.Range(0.8f, 2.0f);
                oppositeOvertakeCooldownTimer = Random.Range(1.2f, 2.5f);
            }
            else
            {
                laneChangeCooldownTimer = Random.Range(2.0f, 5.0f);
                oppositeOvertakeCooldownTimer = Random.Range(2.5f, 5.5f);
            }

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
                string modelPath = IsMotorcycle ? "Assets/Models/traffic_motorcycle_agent.onnx" : "Assets/Models/traffic_aif_agent.onnx";
                onnxModelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
                if (onnxModelAsset == null && IsMotorcycle)
                {
                    onnxModelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/traffic_aif_agent.onnx");
                }
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
            bool isPedAhead,
            float oppClearGap,
            bool isOvertakingOpp,
            bool isChanging,
            float adjClearGap)
        {
            if (worker == null)
            {
                float fallbackAccel = (gap < 4.0f) ? -4.0f : ((speed < targetSpd) ? 1.5f : 0f);
                DrivingMode fallbackMode = (gap < 4.0f) ? DrivingMode.CarFollowing : DrivingMode.FreeFlow;
                return (fallbackMode, fallbackAccel, 0f);
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
            float normOppClearGap = Mathf.Clamp01(oppClearGap);
            float overtakingInput = isOvertakingOpp ? 1.0f : 0.0f;
            float changingLaneInput = isChanging ? 1.0f : 0.0f;
            float adjacentClearInput = Mathf.Clamp01(adjClearGap);

            float[] rawInputs = new float[] {
                normSpeed, normGap, normRelSpeed, speedError, normOffset, sideBalance,
                leaderBrakeInput, pedInput, trafficDensity, normOppClearGap, overtakingInput,
                changingLaneInput, adjacentClearInput
            };
            using var inputTensor = new Tensor<float>(new TensorShape(1, 13), rawInputs);

            try
            {
                worker.Schedule(inputTensor);
                var outputTensor = worker.PeekOutput() as Tensor<float>;
                if (outputTensor != null)
                {
                    float[] results = outputTensor.DownloadToArray();
                    if (results != null && results.Length >= 10)
                    {
                        int bestModeIdx = 0;
                        float maxLogit = results[0];
                        for (int i = 1; i < 8; i++)
                        {
                            if (results[i] > maxLogit)
                            {
                                maxLogit = results[i];
                                bestModeIdx = i;
                            }
                        }
                        DrivingMode predictedMode = (DrivingMode)bestModeIdx;
                        float maxSteerMultiplier = IsMotorcycle ? 0.70f : 0.45f;
                        float maxAccelLimit = IsMotorcycle ? motorcycleMaxAcceleration : 4.0f;
                        float minAccelLimit = IsMotorcycle ? -10.0f : -8.0f;

                        float accel = Mathf.Clamp(results[8] * maxAcceleration, minAccelLimit, maxAccelLimit);
                        float steer = Mathf.Clamp(results[9] * maxSteerMultiplier, -maxSteerMultiplier, maxSteerMultiplier);
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

            if (vehicleCrashPauseTimer > 0f)
            {
                vehicleCrashPauseTimer -= dt;
                if (vehicleCrashPauseTimer <= 0f)
                {
                    postVehicleCollisionGraceTimer = 2.5f;
                }
                else
                {
                    currentSpeed = 0f;
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        Vector3 pos = rb.position;
                        pos.y = fixedHeightY;
                        rb.position = pos;
                    }
                    if (lightsController != null) lightsController.SetBraking(true);
                    return;
                }
            }

            if (postPedestrianCollisionGraceTimer > 0f) postPedestrianCollisionGraceTimer -= dt;
            if (postVehicleCollisionGraceTimer > 0f) postVehicleCollisionGraceTimer -= dt;
            if (laneChangeCooldownTimer > 0f) laneChangeCooldownTimer -= dt;
            if (oppositeOvertakeCooldownTimer > 0f) oppositeOvertakeCooldownTimer -= dt;

            effectiveTargetSpeed = targetSpeed * Mathf.Lerp(1.0f, hurryFactor, 0.6f);

            float halfSectionWidth = 3.4f;
            float rawOffset = (transform.position.x - roadSectionCenter) / halfSectionWidth;
            float normalizedRoadOffset = Mathf.Clamp(rawOffset * drivingDirection, -1f, 1f);

            if (TrafficFieldManager.Instance != null)
            {
                TrafficFieldManager.Instance.RegisterVehiclePosition(transform.position);
            }

            float distanceToFront = 60f;
            isFrontCarBraking = false;
            frontCarTurnSignal = TurnSignal.None;
            GameObject frontVehicleObj = null;

            bool isPedestrianInPath = false;
            float nearestPedDist = 60f;
            float nearestPedRelLateral = 0f;
            int nearestPedDir = 0;

            Vector3 carCenter = new Vector3(transform.position.x, fixedHeightY, transform.position.z);
            Vector3 frontBumperCenter = carCenter + transform.forward * (halfLength + 0.05f);

            float bumperSpread = halfWidth * 0.85f;
            float[] frontOffsets = new float[] {
                0f,
                -bumperSpread * 0.25f, bumperSpread * 0.25f,
                -bumperSpread * 0.50f, bumperSpread * 0.50f,
                -bumperSpread * 0.75f, bumperSpread * 0.75f,
                -bumperSpread, bumperSpread
            };
            float inCorridorThreshold = halfWidth + 0.25f;

            for (int r = 0; r < frontOffsets.Length; r++)
            {
                Vector3 origin = frontBumperCenter + transform.right * frontOffsets[r];
                if (Physics.Raycast(origin, transform.forward, out RaycastHit hit, 50f))
                {
                    if (hit.collider.gameObject != gameObject && hit.collider.transform.root != transform.root)
                    {
                        bool isVeh = hit.collider.CompareTag("Vehicle") || hit.collider.GetComponentInParent<AIFVehicleAgent>() != null;
                        bool isPed = hit.collider.CompareTag("Pedestrian") || hit.collider.GetComponentInParent<PedestrianAgent>() != null;

                        Vector3 hitLocal = transform.InverseTransformPoint(hit.point);

                        if (isVeh)
                        {
                            AIFVehicleAgent hitAgent = hit.collider.GetComponentInParent<AIFVehicleAgent>();
                            int otherDir = (hitAgent != null) ? hitAgent.drivingDirection : (Vector3.Dot(transform.forward, hit.transform.forward) > 0 ? drivingDirection : -drivingDirection);
                            bool isSameDir = (otherDir == drivingDirection);

                            if (isSameDir && Mathf.Abs(hitLocal.x) <= inCorridorThreshold && hit.distance < distanceToFront)
                            {
                                distanceToFront = hit.distance;
                                frontVehicleObj = hit.collider.gameObject;
                            }
                            else if (!isSameDir)
                            {
                                bool isIntrudingOurSide = (hit.point.x * drivingDirection) >= 0.25f;

                                if (isOvertakingOpposite)
                                {
                                    if (hit.distance < oppositeClearDist) oppositeClearDist = hit.distance;
                                    if (Mathf.Abs(hitLocal.x) <= (halfWidth + 0.35f) && hit.distance < distanceToFront)
                                    {
                                        distanceToFront = hit.distance;
                                        frontVehicleObj = hit.collider.gameObject;
                                    }
                                }
                                else if (isIntrudingOurSide && Mathf.Abs(hitLocal.x) <= inCorridorThreshold && hit.distance < distanceToFront)
                                {
                                    distanceToFront = hit.distance;
                                    frontVehicleObj = hit.collider.gameObject;
                                }
                            }
                        }
                        else if (isPed && hit.distance < nearestPedDist)
                        {
                            var pAgent = hit.collider.GetComponentInParent<PedestrianAgent>();
                            int pedDir = (pAgent != null) ? pAgent.crossingDirection : (hitLocal.x > 0 ? -1 : 1);
                            bool isMovingAway = (hitLocal.x * pedDir) > 1.2f;

                            if (!isMovingAway)
                            {
                                isPedestrianInPath = true;
                                nearestPedDist = hit.distance;
                                nearestPedRelLateral = hitLocal.x;
                                if (pAgent != null) nearestPedDir = pAgent.crossingDirection;
                            }
                        }
                    }
                }
            }

            if (Physics.BoxCast(frontBumperCenter, new Vector3(halfWidth * 0.95f, 0.45f, 0.05f), transform.forward, out RaycastHit boxHit, transform.rotation, 50f))
            {
                if (boxHit.collider.gameObject != gameObject && boxHit.collider.transform.root != transform.root)
                {
                    bool isVeh = boxHit.collider.CompareTag("Vehicle") || boxHit.collider.GetComponentInParent<AIFVehicleAgent>() != null;
                    if (isVeh)
                    {
                        AIFVehicleAgent hitAgent = boxHit.collider.GetComponentInParent<AIFVehicleAgent>();
                        int otherDir = (hitAgent != null) ? hitAgent.drivingDirection : (Vector3.Dot(transform.forward, boxHit.transform.forward) > 0 ? drivingDirection : -drivingDirection);
                        bool isSameDir = (otherDir == drivingDirection);
                        Vector3 hitLocal = transform.InverseTransformPoint(boxHit.point);

                        if (isSameDir && Mathf.Abs(hitLocal.x) <= inCorridorThreshold && boxHit.distance < distanceToFront)
                        {
                            distanceToFront = boxHit.distance;
                            frontVehicleObj = boxHit.collider.gameObject;
                        }
                        else if (!isSameDir && isOvertakingOpposite && boxHit.distance < distanceToFront)
                        {
                            distanceToFront = boxHit.distance;
                            frontVehicleObj = boxHit.collider.gameObject;
                        }
                    }
                }
            }

            bool hasCarOnLeftFrontDiag = false;
            bool hasCarOnRightFrontDiag = false;

            float[] diagAngles = new float[] { -60f, -45f, -30f, -15f, 15f, 30f, 45f, 60f };
            for (int d = 0; d < diagAngles.Length; d++)
            {
                Vector3 diagDir = Quaternion.Euler(0f, diagAngles[d], 0f) * transform.forward;
                if (Physics.Raycast(frontBumperCenter, diagDir, out RaycastHit diagHit, 30f))
                {
                    if (diagHit.collider.gameObject != gameObject && diagHit.collider.transform.root != transform.root)
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

                                bool isMovingAway = (hitLocal.x * pedDir) > 1.2f;

                                if (!isMovingAway && (Mathf.Abs(predictedRelX) < 2.0f || Mathf.Abs(hitLocal.x) < 1.6f))
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
                            AIFVehicleAgent hitAgent = diagHit.collider.GetComponentInParent<AIFVehicleAgent>();
                            int otherDir = (hitAgent != null) ? hitAgent.drivingDirection : (Vector3.Dot(transform.forward, diagHit.transform.forward) > 0 ? drivingDirection : -drivingDirection);
                            bool isSameDir = (otherDir == drivingDirection);

                            bool isIntrudingOurSide = (diagHit.point.x * drivingDirection) >= 0.25f;

                            if (isSameDir || isOvertakingOpposite || isIntrudingOurSide)
                            {
                                if (diagAngles[d] < 0f) hasCarOnLeftFrontDiag = true;
                                else hasCarOnRightFrontDiag = true;
                            }
                        }
                    }
                }
            }

            float distToCarLeft = 50f;
            float distToCarRight = 50f;
            float sideHalfWidth = halfWidth + 0.05f;
            float[] sideOffsets = new float[] { halfLength * 0.85f, halfLength * 0.30f, -halfLength * 0.30f, -halfLength * 0.85f };

            for (int s = 0; s < sideOffsets.Length; s++)
            {
                Vector3 leftOrigin = carCenter + transform.forward * sideOffsets[s] - transform.right * sideHalfWidth;
                if (Physics.Raycast(leftOrigin, -transform.right, out RaycastHit hitL, sideScanDistance))
                {
                    if (hitL.collider.gameObject != gameObject && hitL.collider.transform.root != transform.root && (hitL.collider.CompareTag("Vehicle") || hitL.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                    {
                        AIFVehicleAgent agentL = hitL.collider.GetComponentInParent<AIFVehicleAgent>();
                        int dirL = (agentL != null) ? agentL.drivingDirection : (Vector3.Dot(transform.forward, hitL.transform.forward) > 0 ? drivingDirection : -drivingDirection);
                        bool isSameDirL = (dirL == drivingDirection);

                        bool isIntrudingL = (hitL.point.x * drivingDirection) >= 0.25f;
                        if (isSameDirL || isOvertakingOpposite || isIntrudingL)
                        {
                            if (hitL.distance < distToCarLeft) distToCarLeft = hitL.distance;
                        }
                    }
                }

                Vector3 rightOrigin = carCenter + transform.forward * sideOffsets[s] + transform.right * sideHalfWidth;
                if (Physics.Raycast(rightOrigin, transform.right, out RaycastHit hitR, sideScanDistance))
                {
                    if (hitR.collider.gameObject != gameObject && hitR.collider.transform.root != transform.root && (hitR.collider.CompareTag("Vehicle") || hitR.collider.GetComponentInParent<AIFVehicleAgent>() != null))
                    {
                        AIFVehicleAgent agentR = hitR.collider.GetComponentInParent<AIFVehicleAgent>();
                        int dirR = (agentR != null) ? agentR.drivingDirection : (Vector3.Dot(transform.forward, hitR.transform.forward) > 0 ? drivingDirection : -drivingDirection);
                        bool isSameDirR = (dirR == drivingDirection);

                        bool isIntrudingR = (hitR.point.x * drivingDirection) <= -0.25f;
                        if (isSameDirR || isOvertakingOpposite || isIntrudingR)
                        {
                            if (hitR.distance < distToCarRight) distToCarRight = hitR.distance;
                        }
                    }
                }
            }

            float currentPosX = transform.position.x;
            float lateralCorridorOffset = 4.0f;
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

            float oppositeLaneX = -2.0f * drivingDirection;
            float oppDist = 60f;
            float[] oppScanOffsets = new float[] { 0f, -0.40f, 0.40f };
            for (int o = 0; o < oppScanOffsets.Length; o++)
            {
                Vector3 oppOrigin = new Vector3(oppositeLaneX + oppScanOffsets[o], fixedHeightY, transform.position.z);
                if (Physics.Raycast(oppOrigin, transform.forward, out RaycastHit hitOpp, 60f))
                {
                    if (hitOpp.collider.gameObject != gameObject)
                    {
                        if (hitOpp.distance < oppDist) oppDist = hitOpp.distance;
                    }
                }
            }
            oppositeClearDist = oppDist;
            oppositeClearGap = Mathf.Clamp01(oppDist / 50.0f);

            float homeLaneAheadDist = 60f;
            Vector3 homeOrigin = new Vector3(homeLaneX, fixedHeightY, transform.position.z + transform.forward.z * 2.2f);
            if (Physics.Raycast(homeOrigin, transform.forward, out RaycastHit hitHome, 45f))
            {
                if (hitHome.collider.gameObject != gameObject && hitHome.collider.transform.root != transform.root)
                {
                    AIFVehicleAgent homeAgent = hitHome.collider.GetComponentInParent<AIFVehicleAgent>();
                    int homeDir = (homeAgent != null) ? homeAgent.drivingDirection : (Vector3.Dot(transform.forward, hitHome.transform.forward) > 0 ? drivingDirection : -drivingDirection);
                    if (homeDir == drivingDirection)
                    {
                        homeLaneAheadDist = hitHome.distance;
                    }
                }
            }

            bool isPhysicallyOnOppositeSide = (currentPosX * drivingDirection) < -0.6f;
            if (isPhysicallyOnOppositeSide && !isChangingLane) isOvertakingOpposite = true;

            bool hasPedestrianObstacle = (isPedestrianInPath && nearestPedDist < 24.0f);
            bool hasStoppedVehicleObstacle = (frontVehicleObj != null && isLeaderStopped && distanceToFront < 18.0f);
            bool hasSlowVehicleObstacle = (frontVehicleObj != null && !isLeaderStopped && distanceToFront < 14.0f && leaderSpeed < effectiveTargetSpeed * 0.70f);
            bool hasObstacleAhead = hasPedestrianObstacle || hasStoppedVehicleObstacle || hasSlowVehicleObstacle;

            float effectiveGap = hasPedestrianObstacle ? nearestPedDist : (frontVehicleObj != null ? distanceToFront : 50f);
            float relSpeed = hasPedestrianObstacle ? -currentSpeed : (frontVehicleObj != null ? (leaderSpeed - currentSpeed) : 0f);

            bool canGoLeft = isLeftCorridorInBounds && (distToCarLeft >= 0.50f) && !hasCarOnLeftFrontDiag && (distLeftLaneAhead > 7.0f) && (distLeftLaneBehind > 5.0f);
            bool canGoRight = isRightCorridorInBounds && (distToCarRight >= 0.50f) && !hasCarOnRightFrontDiag && (distRightLaneAhead > 7.0f) && (distRightLaneBehind > 5.0f);

            float chosenShift = 0f;
            float adjClearDist = 0f;
            if (canGoRight && canGoLeft)
            {
                adjClearDist = Mathf.Max(distRightLaneAhead, distLeftLaneAhead);
                chosenShift = (distRightLaneAhead >= distLeftLaneAhead) ? lateralCorridorOffset : -lateralCorridorOffset;
            }
            else if (canGoRight)
            {
                adjClearDist = distRightLaneAhead;
                chosenShift = lateralCorridorOffset;
            }
            else if (canGoLeft)
            {
                adjClearDist = distLeftLaneAhead;
                chosenShift = -lateralCorridorOffset;
            }
            float adjacentClearGap = Mathf.Clamp01(adjClearDist / 50.0f);

            var (aiSelectedMode, predictedAccel, predictedSteer) = PredictAIPolicy(
                currentSpeed,
                effectiveGap,
                relSpeed,
                effectiveTargetSpeed,
                normalizedRoadOffset,
                distToCarLeft,
                distToCarRight,
                isFrontCarBraking,
                hasPedestrianObstacle,
                oppositeClearGap,
                isOvertakingOpposite,
                isChangingLane,
                adjacentClearGap
            );

            currentDrivingMode = aiSelectedMode;
            aiAccelerationOutput = predictedAccel;
            aiSteeringOutput = predictedSteer;

            if (aiSelectedMode == DrivingMode.AbortLaneChange)
            {
                currentDrivingMode = DrivingMode.AbortLaneChange;
                isChangingLane = false;
                targetLaneX = homeLaneX;
                laneChangeCooldownTimer = 2.5f;
                if (lightsController != null) lightsController.SetTurnSignal(TurnSignal.None);
            }
            else if (aiSelectedMode == DrivingMode.CreepForward)
            {
                currentDrivingMode = DrivingMode.CreepForward;
                float creepTargetSpeed = Mathf.Clamp(effectiveTargetSpeed * 0.20f, 1.5f, 2.8f);
                if (currentSpeed < creepTargetSpeed)
                {
                    currentSpeed = Mathf.MoveTowards(currentSpeed, creepTargetSpeed, 3.0f * dt);
                }
                predictedAccel = Mathf.Max(predictedAccel, 0.45f);
            }
            else if (aiSelectedMode == DrivingMode.ActiveEvasion && !isChangingLane && laneChangeCooldownTimer <= 0f)
            {
                if (Mathf.Abs(chosenShift) > 0.1f)
                {
                    targetLaneX = Mathf.Clamp(currentPosX + chosenShift * drivingDirection, minSafeX + 0.4f, maxSafeX - 0.4f);
                    isChangingLane = true;
                    laneChangeCooldownTimer = 2.0f;
                }
            }

            bool canOvertakeOpposite = oppositeOvertakeCooldownTimer <= 0f
                && oppositeClearDist >= 35.0f
                && distToCarLeft >= 0.55f
                && !hasCarOnLeftFrontDiag
                && !hasPedestrianObstacle;

            if (isOvertakingOpposite)
            {
                bool hasHazardAheadInOpposite = (oppositeClearDist < 30.0f) || (distanceToFront < 28.0f) || isPedestrianInPath;
                bool hasPassedTrafficJam = (homeLaneAheadDist > 16.0f) && (distanceToFront > 18.0f);

                if (hasHazardAheadInOpposite || hasPassedTrafficJam || aiSelectedMode == DrivingMode.AbortLaneChange)
                {
                    targetLaneX = homeLaneX;
                    isChangingLane = true;
                    if (lightsController != null) lightsController.SetTurnSignal(TurnSignal.Right);

                    if ((currentPosX * drivingDirection) > 0.4f)
                    {
                        isOvertakingOpposite = false;
                        if (Mathf.Abs(currentPosX - homeLaneX) < 0.45f)
                        {
                            isChangingLane = false;
                            targetLaneX = homeLaneX;
                            oppositeOvertakeCooldownTimer = Random.Range(3.5f, 6.0f);
                            if (lightsController != null) lightsController.SetTurnSignal(TurnSignal.None);
                        }
                    }
                }
                else
                {
                    targetLaneX = oppositeLaneX;
                    currentDrivingMode = DrivingMode.OppositeOvertake;
                }
            }
            else
            {
                if (aiSelectedMode == DrivingMode.OppositeOvertake && canOvertakeOpposite)
                {
                    isOvertakingOpposite = true;
                    isChangingLane = true;
                    targetLaneX = oppositeLaneX;
                    laneChangeCooldownTimer = 1.5f;
                    currentDrivingMode = DrivingMode.OppositeOvertake;
                    if (lightsController != null) lightsController.SetTurnSignal(TurnSignal.Left);
                }
                else if (hasObstacleAhead && !isChangingLane && laneChangeCooldownTimer <= 0f)
                {
                    if (hasPedestrianObstacle)
                    {
                        targetLaneX = currentPosX;
                    }
                    else if (Mathf.Abs(chosenShift) > 0.1f)
                    {
                        targetLaneX = Mathf.Clamp(currentPosX + chosenShift * drivingDirection, minSafeX + 0.4f, maxSafeX - 0.4f);
                        isChangingLane = true;
                        laneChangeCooldownTimer = 2.0f;
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
            }

            float openRoadBoost = (isOpenRoadAhead && hurryFactor > 1.0f) ? (1.0f + (hurryFactor - 1.0f) * 0.7f) : 1.0f;
            float effectiveMaxAccel = maxAcceleration * Mathf.Sqrt(Mathf.Clamp(hurryFactor, 0.6f, 1.6f)) * openRoadBoost;
            float accel = predictedAccel * Mathf.Sqrt(Mathf.Clamp(hurryFactor, 0.7f, 1.4f));
            float parallelSeparationLateral = 0f;

            if (isOvertakingOpposite && oppositeClearDist > 30f)
            {
                accel = Mathf.Max(accel, 1.5f);
            }

            if (currentSpeed >= effectiveTargetSpeed) accel = Mathf.Min(accel, 0f);

            bool vehicleTooCloseAhead = false;
            float dynamicEmergencyDist = minEmergencyBrakeDistance;

            if (frontVehicleObj != null)
            {
                float safetyBuffer = IsMotorcycle ? 1.5f : 2.2f;
                dynamicEmergencyDist = Mathf.Max(minEmergencyBrakeDistance, (currentSpeed * currentSpeed) / (2f * emergencyBrakeDecel) + safetyBuffer);

                float closingSpeed = currentSpeed - leaderSpeed;
                bool isTTCImminent = (closingSpeed > 0.6f) && ((distanceToFront / closingSpeed) < 1.0f);

                if (distanceToFront <= dynamicEmergencyDist || isTTCImminent || distanceToFront < 1.8f)
                {
                    vehicleTooCloseAhead = true;
                }
            }

            bool vehicleCuttingInAhead = false;
            if ((hasCarOnLeftFrontDiag && distToCarLeft < 0.65f) || (hasCarOnRightFrontDiag && distToCarRight < 0.65f))
            {
                vehicleCuttingInAhead = true;
            }

            bool physicalProximityDanger = false;
            Collider[] bumperHits = Physics.OverlapBox(frontBumperCenter + transform.forward * 0.35f, new Vector3(halfWidth + 0.05f, 0.4f, 0.35f), transform.rotation);
            for (int i = 0; i < bumperHits.Length; i++)
            {
                if (bumperHits[i].gameObject != gameObject && bumperHits[i].transform.root != transform.root)
                {
                    if (bumperHits[i].CompareTag("Vehicle") || bumperHits[i].GetComponentInParent<AIFVehicleAgent>() != null)
                    {
                        physicalProximityDanger = true;
                        break;
                    }
                }
            }

            bool mustEmergencyBrake = vehicleTooCloseAhead || vehicleCuttingInAhead || physicalProximityDanger;

            if (isOvertakingOpposite && (oppositeClearDist < 18.0f || (isPhysicallyOnOppositeSide && distanceToFront < 16.0f)))
            {
                currentDrivingMode = DrivingMode.AbortLaneChange;
                targetLaneX = homeLaneX;
                isChangingLane = true;
                aiSteeringOutput = (homeLaneX - currentPosX) * drivingDirection * 1.5f;

                float returnLateral = Mathf.Sign((homeLaneX - currentPosX) * drivingDirection) * 2.5f;
                parallelSeparationLateral += returnLateral;

                if (lightsController != null)
                {
                    lightsController.SetBraking(true);
                    lightsController.SetTurnSignal(TurnSignal.Right);
                }

                if (distanceToFront < 1.5f || oppositeClearDist < 1.5f || mustEmergencyBrake)
                {
                    isEmergencyBraking = true;
                    currentDrivingMode = DrivingMode.EmergencyShield;
                    accel = -emergencyBrakeDecel;
                    currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, emergencyBrakeDecel * dt);
                    if (distanceToFront < 0.8f)
                    {
                        currentSpeed = 0f;
                        if (rb != null) rb.linearVelocity = Vector3.zero;
                    }
                }
                else
                {
                    float escapeSpeed = Mathf.Clamp(currentSpeed, 3.5f, 6.0f);
                    currentSpeed = Mathf.MoveTowards(currentSpeed, escapeSpeed, 6.0f * dt);
                    accel = 0.5f;
                }
            }
            else if (hasPedestrianObstacle && !isChangingLane && nearestPedDist < 3.5f)
            {
                isEmergencyBraking = true;
                currentDrivingMode = DrivingMode.SafeYielding;
                accel = -emergencyBrakeDecel;
                if (lightsController != null) lightsController.SetBraking(true);
                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, 25.0f * dt);
                if (nearestPedDist < 1.0f)
                {
                    currentSpeed = 0f;
                    if (rb != null) rb.linearVelocity = Vector3.zero;
                }
            }
            else if (mustEmergencyBrake)
            {
                isEmergencyBraking = true;
                currentDrivingMode = DrivingMode.EmergencyShield;
                accel = -emergencyBrakeDecel;

                currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, emergencyBrakeDecel * dt);

                if (distanceToFront < 0.9f || physicalProximityDanger)
                {
                    currentSpeed = 0f;
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                    }
                }

                if (lightsController != null)
                {
                    lightsController.SetBraking(true);
                }
            }
            else if (frontVehicleObj != null && distanceToFront < 16.0f)
            {
                isEmergencyBraking = false;
                currentDrivingMode = DrivingMode.CarFollowing;

                if (currentSpeed > leaderSpeed)
                {
                    accel = Mathf.Min(accel, -4.0f);
                    currentSpeed = Mathf.MoveTowards(currentSpeed, leaderSpeed, 12.0f * dt);
                }
                else if (distanceToFront >= 3.5f && leaderSpeed > 0.5f)
                {
                    float followTarget = Mathf.Min(leaderSpeed, effectiveTargetSpeed);
                    currentSpeed = Mathf.MoveTowards(currentSpeed, followTarget, 3.0f * dt);
                    accel = Mathf.Clamp(accel, -1.0f, 1.2f);
                }
                else
                {
                    currentSpeed = Mathf.MoveTowards(currentSpeed, leaderSpeed, 10.0f * dt);
                    accel = Mathf.Min(accel, -2.5f);
                }

                if (currentSpeed < 0.2f && leaderSpeed < 0.2f)
                {
                    currentSpeed = 0f;
                    if (rb != null) rb.linearVelocity = Vector3.zero;
                    if (lightsController != null) lightsController.SetBraking(true);
                }
            }
            else
            {
                isEmergencyBraking = false;
            }

            if (isOpenRoadAhead && currentSpeed < 1.5f && !hasPedestrianObstacle && (frontVehicleObj == null || distanceToFront > 10f))
            {
                accel = Mathf.Max(accel, 1.8f);
            }

            if (postVehicleCollisionGraceTimer > 2.2f && (frontVehicleObj == null || distanceToFront > dynamicEmergencyDist + 1.0f))
            {
                if (currentSpeed < 2.5f) currentSpeed = 2.5f;
            }

            float throttle = Mathf.Clamp(accel / Mathf.Max(1f, effectiveMaxAccel), -1f, 1f);
            currentSpeed = Mathf.Clamp(currentSpeed + accel * dt, 0f, effectiveTargetSpeed * 1.15f);

            float lateralOffset = (targetLaneX - currentPosX) * drivingDirection;
            float steering = 0f;

            if (distToCarRight < 0.50f)
            {
                float urgency = (0.50f - distToCarRight) / 0.50f;
                steering = -urgency * 0.45f;
                parallelSeparationLateral += -urgency * 1.5f;
            }
            else if (distToCarLeft < 0.50f)
            {
                float urgency = (0.50f - distToCarLeft) / 0.50f;
                steering = urgency * 0.45f;
                parallelSeparationLateral += urgency * 1.5f;
            }
            else if (isOvertakingOpposite || isChangingLane)
            {
                float maxSteerLimit = isOvertakingOpposite ? (IsMotorcycle ? 0.95f : 0.80f) : (IsMotorcycle ? 0.85f : 0.65f);
                float steerGain = IsMotorcycle ? 0.55f : 0.40f;
                steering = Mathf.Clamp(lateralOffset * 0.80f + aiSteeringOutput * steerGain, -maxSteerLimit, maxSteerLimit);
            }
            else if (currentDrivingMode == DrivingMode.AbortLaneChange)
            {
                steering = Mathf.Clamp(aiSteeringOutput * 0.70f + lateralOffset * 0.50f, -0.60f, 0.60f);
            }
            else
            {
                float steerGain = IsMotorcycle ? 0.50f : 0.35f;
                steering = Mathf.Clamp(aiSteeringOutput * steerGain + lateralOffset * 0.20f, -0.30f, 0.30f);
                if (Mathf.Abs(steering) < 0.02f) steering = 0f;
            }

            float steerSmoothSpeed = IsMotorcycle ? 18.0f : 12.0f;
            smoothedSteering = Mathf.Lerp(smoothedSteering, steering, steerSmoothSpeed * dt);
            steering = smoothedSteering;

            float currentYaw = transform.eulerAngles.y;
            float desiredYaw = baseYaw + Mathf.Clamp(steering * maxHeadingAngle, -maxHeadingAngle, maxHeadingAngle);
            float turnSpeed = IsMotorcycle ? 65f : 45f;
            float newYaw = Mathf.MoveTowardsAngle(currentYaw, desiredYaw, turnSpeed * dt);

            Quaternion safeRot = Quaternion.Euler(0f, newYaw, 0f);
            transform.rotation = safeRot;
            if (rb != null)
            {
                rb.rotation = safeRot;
                rb.angularVelocity = Vector3.zero;
            }

            Vector3 targetVelocity = transform.forward * currentSpeed;
            targetVelocity.y = 0f;

            if (Mathf.Abs(parallelSeparationLateral) > 0.01f)
            {
                targetVelocity += transform.right * parallelSeparationLateral;
            }

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

            Vector3 curPos = rb.position;
            curPos.y = fixedHeightY;

            float effectiveMinX = minSafeX;
            float effectiveMaxX = maxSafeX;
            if (drivingDirection == 1)
            {
                if (isOvertakingOpposite || curPos.x < minSafeX) effectiveMinX = -3.6f;
            }
            else
            {
                if (isOvertakingOpposite || curPos.x > maxSafeX) effectiveMaxX = 3.6f;
            }
            curPos.x = Mathf.Clamp(curPos.x, effectiveMinX, effectiveMaxX);
            rb.position = curPos;

            if (lightsController != null)
            {
                bool isBrakingNow = isEmergencyBraking || (throttle < -0.05f) || (distanceToFront < 6f && currentSpeed > 0.5f) || (collisionVisualizer != null && collisionVisualizer.IsInCrashState);
                lightsController.SetBraking(isBrakingNow);
            }

            if (drivingDirection == 1 && transform.position.z >= 490f)
            {
                isOvertakingOpposite = false;
                targetLaneX = homeLaneX;
                transform.position = new Vector3(homeLaneX, fixedHeightY, 10f);
                currentSpeed = 6f;
                rb.linearVelocity = transform.forward * 6f;
            }
            else if (drivingDirection == -1 && transform.position.z <= 10f)
            {
                isOvertakingOpposite = false;
                targetLaneX = homeLaneX;
                transform.position = new Vector3(homeLaneX, fixedHeightY, 490f);
                currentSpeed = 6f;
                rb.linearVelocity = transform.forward * 6f;
            }
        }

        void LateUpdate()
        {
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
                pedestrianCollisionPauseTimer = 1.2f;
                currentSpeed = 0f;
                if (rb != null) rb.linearVelocity = Vector3.zero;
                if (lightsController != null) lightsController.SetBraking(true);
                if (collisionVisualizer != null) collisionVisualizer.TriggerCrash(1.5f);

                IgnoreCollisionWithObject(other, 6.0f);
                var pedAgent = other.GetComponentInParent<PedestrianAgent>();
                if (pedAgent != null)
                {
                    pedAgent.TriggerCrash(1.2f, gameObject);
                    pedAgent.IgnoreCollisionWithVehicle(gameObject, 6.0f);
                }
                return;
            }

            if (other.CompareTag("Vehicle") || other.GetComponentInParent<AIFVehicleAgent>() != null)
            {
                isCollidingWithVehicle = false;
                vehicleSeparationVector = Vector3.zero;

                var otherAgent = other.GetComponentInParent<AIFVehicleAgent>();

                Vector3 separateDir = Vector3.zero;
                float penDepth = 0f;

                if (collision.contactCount > 0)
                {
                    ContactPoint contact = collision.GetContact(0);
                    separateDir = contact.normal;
                    separateDir.y = 0f;
                    if (contact.separation < 0f) penDepth = -contact.separation;
                }

                if (separateDir.sqrMagnitude < 0.01f)
                {
                    Vector3 diff = transform.position - other.transform.position;
                    diff.y = 0f;
                    separateDir = (diff.sqrMagnitude > 0.01f) ? diff.normalized : -transform.forward;
                }
                separateDir.Normalize();

                float bouncePerCar = 0.25f;
                float totalShift = (penDepth * 0.5f) + bouncePerCar;
                Vector3 pushOut = separateDir * totalShift;

                transform.position += pushOut;
                if (rb != null)
                {
                    Vector3 p = transform.position;
                    p.y = fixedHeightY;
                    p.x = Mathf.Clamp(p.x, minSafeX - 1.2f, maxSafeX + 1.2f);
                    rb.position = p;
                }

                if (otherAgent != null)
                {
                    otherAgent.transform.position -= pushOut;
                    if (otherAgent.rb != null)
                    {
                        Vector3 op = otherAgent.transform.position;
                        op.y = otherAgent.fixedHeightY;
                        op.x = Mathf.Clamp(op.x, otherAgent.minSafeX - 1.2f, otherAgent.maxSafeX + 1.2f);
                        otherAgent.rb.position = op;
                    }

                    if (isOvertakingOpposite)
                    {
                        targetLaneX = homeLaneX;
                        isChangingLane = true;
                        oppositeOvertakeCooldownTimer = 4.0f;
                    }
                    if (otherAgent.isOvertakingOpposite)
                    {
                        otherAgent.targetLaneX = otherAgent.homeLaneX;
                        otherAgent.isChangingLane = true;
                        otherAgent.oppositeOvertakeCooldownTimer = 4.0f;
                    }
                }

                if (postVehicleCollisionGraceTimer > 0f) return;

                float crashDuration = (vehicleCrashStopDuration > 0f) ? vehicleCrashStopDuration : 2.0f;

                TriggerVehicleCrash(crashDuration);

                if (otherAgent != null)
                {
                    otherAgent.TriggerVehicleCrash(crashDuration);
                }

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

        public void TriggerVehicleCrash(float duration = 2.0f)
        {
            vehicleCrashPauseTimer = (duration > 0f) ? duration : 2.0f;
            currentSpeed = 0f;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Vector3 p = rb.position;
                p.y = fixedHeightY;
                rb.position = p;
                rb.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
            isCollidingWithVehicle = false;
            vehicleSeparationVector = Vector3.zero;
            if (lightsController != null) lightsController.SetBraking(true);
            if (collisionVisualizer != null) collisionVisualizer.TriggerCrash(duration > 0f ? duration : 2.0f);
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
