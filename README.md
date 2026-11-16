# TwoLaneSimulation - 4-Lane Bidirectional Traffic Simulation with Sentis AI

Mô phỏng giao thông thời gian thực trên Unity 6 (`6000.3.16f1`) gồm **4 làn đường song song, 2 nhóm làn chạy ngược chiều nhau**, tích hợp:
- **Trí tuệ nhân tạo (AI Sentis ONNX)** dự đoán hành vi và điều khiển liên tục.
- **Hệ thống vạch kẻ đường (Zebra Crossing)** không collider & Layer 2 (Ignore Raycast).
- **Hệ thống người đi bộ (PedestrianAgent)** hardcode độc lập đi qua lại 2 chiều.

---

## 🛣️ 1. Cấu hình Hạ tầng & Làn đường (Road Geometry)

* **Kích thước lòng đường**: Rộng **16.0m** ($X \in [-8.0\text{ m}, +8.0\text{ m}]$), Dài **500m** ($Z \in [0, 500\text{ m}]$).
* **Tim đường ($X = 0$)**: Hoàn toàn bằng phẳng, không có rào cản vật lý hay vạch kẻ chia cắt giữa hai chiều xe.
* **4 Làn xe được phân chia đối xứng**:
  1. **Làn 0 (Trái - Ngoài cùng)**: $X = -6.0\text{ m}$, xe chạy theo hướng **$-Z$** (Xoay $180^\circ$, `Car_Reverse`).
  2. **Làn 1 (Trái - Trong cùng)**: $X = -2.0\text{ m}$, xe chạy theo hướng **$-Z$** (Xoay $180^\circ$, `Car_Reverse`).
  3. **Làn 2 (Phải - Trong cùng)**: $X = +2.0\text{ m}$, xe chạy theo hướng **$+Z$** (Xoay $0^\circ$, `Car_Forward`).
  4. **Làn 3 (Phải - Ngoài cùng)**: $X = +6.0\text{ m}$, xe chạy theo hướng **$+Z$** (Xoay $0^\circ$, `Car_Forward`).
* **Rào chắn 2 bên đường (Guardrails)**: Đặt tại $X = \pm 8.15\text{ m}$ cao $1.2\text{ m}$ dọc tuyến đường 500m.

---

## 🧠 2. Tích hợp Mô hình AI (Unity Sentis Inference)

Dự án nạp mô hình mạng nơ-ron sâu đa nhiệm [`Assets/Models/traffic_aif_agent.onnx`](Assets/Models/traffic_aif_agent.onnx) thông qua **Unity Sentis 2.1.3**.

### 9 Đầu vào Quan sát (Input Tensor `[1, 9]`):
1. `normSpeed`: Vận tốc hiện tại của xe ($v / 30.0$, kẹp trong $[0.0, 1.0]$).
2. `normGap`: Cự ly tới vật cản phía trước ($d / 50.0$, kẹp trong $[0.0, 1.0]$).
3. `normRelSpeed`: Vận tốc tương đối với vật cản ($\Delta v / 15.0$, kẹp trong $[-1.0, 1.0]$).
4. `speedError`: Sai số vận tốc $((v_{\text{target}} - v) / v_{\text{target}}$, kẹp trong $[-1.0, 1.0]$).
5. `normRoadOffset`: Độ lệch vị trí so với tim phần đường riêng của xe ($[-1.0, 1.0]$).
6. `sideBalance`: Cân bằng cự ly hai bên sườn ($(distRight - distLeft) / 5.0$, kẹp trong $[-1.0, 1.0]$).
7. `isLeaderBraking`: Đèn phanh xe dẫn đầu ($1.0$ nếu đang phanh, $0.0$ nếu không).
8. `pedestrianDetected`: Nhận diện người đi bộ phía trước ($1.0$ nếu có, $0.0$ nếu không).
9. `trafficDensity`: Mật độ giao thông thời gian thực từ `TrafficFieldManager` ($[0.0, 1.0]$).

### 7 Đầu ra Chính sách (Output Tensor `[1, 7]`):
* **Tầng 1 - Quyết định hành vi bậc cao (High-Level DrivingMode - `argmax[0..4]`)**:
  - `0 - FreeFlow`: Chạy tự do, duy trì vận tốc ổn định.
  - `1 - CarFollowing`: Bám đuôi xe trước ở cự ly an toàn.
  - `2 - ActiveEvasion`: Lách né xe dừng hoặc người đi bộ sang khoảng trống bên cạnh.
  - `3 - SafeYielding`: Dừng hoặc giảm tốc nhường đường cho người qua đường.
  - `4 - EmergencyShield`: Phanh khẩn cấp & bảo hiểm chống va chạm.
* **Tầng 2 - Chấp hành điều khiển liên tục (Continuous Control)**:
  - `Output[5]`: Gia tốc chuẩn hóa $[-1.0, 1.0] \rightarrow a = \text{Output}[5] \times 8.0\text{ m/s}^2$.
  - `Output[6]`: Góc bẻ lái chuẩn hóa $[-1.0, 1.0] \rightarrow \text{steer} = \text{Output}[6] \times 0.45\text{ rad}$.

---

## 🚸 3. Vạch kẻ đường Zebra Crossing & Người đi bộ

* **Zebra Crossing**:
  - Bố trí tại 3 cụm nút giao: $Z = 120\text{m}, 250\text{m}, 380\text{m}$.
  - Mỗi cụm gồm 15 vạch trắng song song kích thước $0.5\text{m} \times 0.002\text{m} \times 4.0\text{m}$.
  - **Không có Collider**: Xe chạy qua hoàn toàn phẳng, không gây rung lắc vật lý.
  - **Layer 2 (`Ignore Raycast`)**: Cảm biến Raycast của xe bỏ qua hoàn toàn vạch kẻ này.
* **Người đi bộ (PedestrianAgent & PedestrianSpawner)**:
  - Hardcode độc lập, sang đường từ cả 2 bên (Trái $\to$ Phải và Phải $\to$ Trái).
  - Hệ thống 5 tia quét xe, tự dừng nhường khi có xe lao tới gần, né vòng xe chết máy và đổi đỏ khi va chạm.

---

## 🚗 4. Cơ chế Sinh xe & Quản lý Luồng giao thông (TrafficSpawner)

* **Tự động sinh 24-28 xe** trải đều trên 4 làn với thuật toán `ResolveOverlap` ngăn chặn hoàn toàn va chạm lúc xuất phát.
* **Tính cách tài xế (Hurry Factor)**: Phân hóa ngẫu nhiên $0.75\times \dots 1.35\times$ tốc độ và độ bám sát.
* **Cảnh báo va chạm**: Thân xe tự động chuyển sang màu đỏ rực khi có va chạm với xe khác hoặc rào chắn (`CarCollisionVisualizer`), đồng thời kích hoạt đèn phanh đỏ (`CarLightsController`).
