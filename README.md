# TwoLaneSimulation (4 Làn Đường, Vạch Zebra Crossing & Người Đi Bộ - Unity 6)

Dự án mô phỏng môi trường giao thông **4 làn đường song song (2 làn trái, 2 làn phải)** với **hai luồng phương tiện chạy ngược chiều nhau**, tích hợp hệ thống **vạch kẻ đường cho người đi bộ (Zebra Crossing)** và **Người đi bộ tự động (Pedestrian Agent & Spawner)** với thuật toán điều khiển hardcode độc lập.

---

## 1. Thiết kế Môi trường 4 Làn & Vạch kẻ Zebra Crossing

### Mặt đường (FourLaneRoad)
- **Cấu trúc 4 làn**: 2 làn bên trái và 2 làn bên phải nằm song song trên cùng một mặt phẳng.
- **Không có vạch phân làn ở giữa**: Tim đường ($X = 0$) phẳng, đồng nhất, không có vạch kẻ tim đường hay dải phân cách cứng.
- **Kích thước**:
  - **Chiều rộng**: $16.0\text{ m}$ (mỗi làn rộng $4.0\text{ m}$, trải từ $X = -8.0\text{ m}$ đến $X = +8.0\text{ m}$).
  - **Chiều dài**: $500\text{ m}$ ($Z \in [0, 500\text{ m}]$).
- **Rào chắn 2 bên mép đường**: Tọa độ $X = -8.15\text{ m}$ và $X = +8.15\text{ m}$.

### Vạch kẻ đường Zebra Crossing (An toàn tuyệt đối cho xe và Raycast)
- **Vị trí thiết lập**: Bố trí tại 3 cụm qua đường chính trên cung đường 500m:
  - Cụm 1: $Z = 120\text{ m}$
  - Cụm 2: $Z = 250\text{ m}$ (ngay trung tâm đoạn đường)
  - Cụm 3: $Z = 380\text{ m}$
- **Quy cách hình học**: Mỗi lối qua đường rộng $4.0\text{ m}$ theo trục $Z$, gồm 13 vạch sơn trắng ($0.6\text{ m} \times 4.0\text{ m}$) đặt cách đều nhau trải suốt toàn bộ chiều rộng 16m của mặt đường.
- **Đặc tính kỹ thuật bắt buộc**:
  - **KHÔNG CÓ COLLIDER**: Toàn bộ các vạch kẻ đường hoàn toàn không gắn `BoxCollider` hay `MeshCollider`. Xe cộ chạy qua 100% mượt mà, không va chạm, không nảy sóc, không làm thay đổi trọng tâm vật lý của xe.
  - **Layer 2 (`Ignore Raycast`)**: Thuộc Layer 2 mặc định của Unity, triệt tiêu hoàn toàn khả năng bị bắt hay phản hồi bởi bất kỳ tia `Physics.Raycast` nào từ cảm biến xe sau này.

---

## 2. Hệ thống Người đi bộ (Pedestrian Agent & Spawner)

Hệ thống điều khiển người đi bộ vận hành độc lập bằng **Hardcode logic** (không phụ thuộc vào bất kỳ hàm AI hay controller của xe):

### `PedestrianAgent`
- **Băng qua đường 2 chiều**:
  - Hướng Trái $\rightarrow$ Phải ($+X$): Xuất phát từ lề trái ($X = -9.0\text{ m}$), hướng mặt góc $90^\circ$, đích đến $X \ge +9.2\text{ m}$.
  - Hướng Phải $\rightarrow$ Trái ($-X$): Xuất phát từ lề phải ($X = +9.0\text{ m}$), hướng mặt góc $-90^\circ$, đích đến $X \le -9.2\text{ m}$.
- **Hệ thống Cảm biến Raycast phía trước**:
  - Bắn 5 tia quét góc quạt tầm xa $2.5\text{ m}$ phía trước mặt người đi bộ.
  - **Nếu xe đang chạy** (vận tốc $\ge 0.5\text{ m/s}$): Người đi bộ chủ động dừng bước nhường đường cho xe qua trước.
  - **Nếu xe dừng chắn đường** (vận tốc $< 0.5\text{ m/s}$): Người đi bộ kích hoạt chế độ đi vòng né qua đầu hoặc đuôi xe theo trục $Z$ để tiếp tục hành trình.
- **Xử lý va chạm vật lý**:
  - Khi có va chạm với xe, người đi bộ lập tức đổi sang màu **Đỏ rực** (`crashColor`), đứng yên trong vài giây, sau đó tự động tách khỏi đầu xe để tiếp tục di chuyển.
- **Hoạt họa bước chân**:
  - Tích hợp hiệu ứng bước đi nhấp nhô nhẹ (`stepBobAmplitude = 0.04m`) tự nhiên.
  - Tự động hủy (`destroyOnArrival`) sau khi đã hoàn thành việc qua đường an toàn.

### `PedestrianSpawner`
- Được gắn trên GameObject `TrafficSystem`.
- Tự động sinh người đi bộ định kỳ (chu kỳ $5 - 10$ giây).
- Tỉ lệ $50\%$ xuất phát từ lề trái và $50\%$ xuất phát từ lề phải.
- Ưu tiên $75\%$ xuất hiện tại các vạch kẻ Zebra Crossing ($Z = 120, 250, 380\text{ m}$).
- Trang phục ngẫu nhiên với bảng màu đa dạng.

---

## 3. Cấu trúc Thư mục Dự án

```text
TwoLaneSimulation/
├── Assets/
│   ├── Materials/
│   │   ├── RoadMaterial.mat            # Mặt đường nhựa asphalt
│   │   ├── GuardrailMaterial.mat       # Rào chắn kim loại
│   │   ├── ZebraCrossingMaterial.mat   # Vạch sơn trắng Zebra Crossing
│   │   ├── PedestrianMaterial.mat      # Vật liệu người đi bộ
│   │   ├── CarBody_Forward.mat         # Thân xe làn tiến (+Z, Xanh Cyan)
│   │   ├── CarBody_Reverse.mat         # Thân xe làn ngược (-Z, Cam Đỏ)
│   │   ├── CarBody_Default.mat         # Thân xe trung tính (Trắng bạc)
│   │   ├── TailLightMaterial.mat       # Đèn hậu đỏ phát quang
│   │   └── HeadLightMaterial.mat       # Đèn pha trước phát quang
│   ├── Prefabs/
│   │   ├── Car_Forward.prefab          # Prefab xe làn tiến (+Z)
│   │   ├── Car_Reverse.prefab          # Prefab xe làn ngược (-Z)
│   │   ├── Car_Neutral.prefab          # Prefab xe trung tính
│   │   └── Pedestrian.prefab           # Prefab người đi bộ (Collider + Agent)
│   ├── Scenes/
│   │   └── TwoLaneSimulationScene.unity# Scene đầy đủ 4 làn, 3 cụm Zebra, 28 xe & người qua đường
│   └── Scripts/
│       ├── PedestrianAgent.cs          # Logic hardcode điều khiển người đi bộ qua đường
│       ├── PedestrianSpawner.cs        # Trình sinh người qua đường tự động 2 chiều
│       └── Editor/
│           └── EnvironmentSetup.cs     # Tool Editor dựng Scene & Prefabs tự động
├── Packages/
│   └── manifest.json
├── ProjectSettings/
└── README.md
```

---

## 4. Kiểm tra trong Unity Editor

1. Mở Scene: [`Assets/Scenes/TwoLaneSimulationScene.unity`](file:///E:/Unity%20Prj/TwoLaneSimulation/Assets/Scenes/TwoLaneSimulationScene.unity).
2. Nhấn nút **Play** trong Unity Editor:
   - Các người đi bộ mẫu tại các vạch Zebra Crossing sẽ tự động bước qua đường từ cả 2 bên lề.
   - Khi gặp xe chắn ngang, người đi bộ sẽ tự động dừng bước chờ hoặc né vòng qua xe.
   - `PedestrianSpawner` sẽ tự động sinh thêm người đi bộ mới định kỳ.
3. Vạch kẻ đường Zebra Crossing hoàn toàn không có Collider và thuộc Layer `Ignore Raycast`.
