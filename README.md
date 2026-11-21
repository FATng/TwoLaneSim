# TwoLaneSimulation - Hệ Thống Mô Phỏng Giao Thông 4 Làn Hai Chiều Tích Hợp Trí Tuệ Nhân Tạo Sentis

Hệ thống mô phỏng giao thông thời gian thực trên nền tảng Unity 6 với cấu hình đường 4 làn song song chạy theo hai chiều ngược nhau. Dự án tích hợp các mô hình mạng nơ-ron sâu đa nhánh (Multi-Head Neural Network) được huấn luyện độc lập qua PyTorch và nhúng trực tiếp vào Unity thông qua bộ máy suy luận thời gian thực (Inference Engine) Unity Sentis.

---

## 1. Tổng Quan Kiến Trúc Mô Hình Trí Tuệ Nhân Tạo

Dự án sử dụng hai mô hình mạng nơ-ron sâu chuyên biệt được lưu trữ dưới định dạng ONNX (Open Neural Network Exchange):
* Mô hình dành cho ô tô: `Assets/Models/traffic_aif_agent.onnx`
* Mô hình dành cho xe máy: `Assets/Models/traffic_motorcycle_agent.onnx`

### 1.1. Cấu Trúc Mạng Nơ-ron Đa Nhánh

Cả hai mô hình đều kế thừa kiến trúc mạng nơ-ron sâu phân tầng gồm ba thành phần chính:
* Khối tầng ẩn dùng chung: Tiếp nhận véc-tơ đầu vào 13 chiều, xử lý qua hai tầng tuyến tính liên tiếp kích thước 96 nút mạng cùng hàm kích hoạt phi tuyến ReLU.
* Nhánh phân loại quyết định hành vi: Tiếp nhận đặc trưng từ khối tầng ẩn, đưa ra điểm số xác suất cho 8 trạng thái hành vi lái xe bậc cao thông qua hàm tuyến tính 96 về 8.
* Nhánh điều khiển liên tục: Đưa ra 2 giá trị điều khiển vật lý gồm gia tốc và góc đánh lái chuẩn hóa trong khoảng từ -1.0 đến +1.0 thông qua hàm Tanh.

Tổng véc-tơ đầu ra của mạng gồm 10 giá trị, ghép nối giữa 8 điểm số hành vi và 2 giá trị điều khiển liên tục.

### 1.2. 13 Biến Quan Sát Đầu Vào

Mỗi phương tiện thu thập trạng thái môi trường xung quanh tại mỗi bước thời gian để tạo thành véc-tơ đầu vào kích thước 13 phần tử:
1. `normSpeed`: Vận tốc hiện tại của xe, chuẩn hóa bằng cách chia cho 30.0 và giới hạn trong đoạn 0.0 đến 1.0.
2. `normGap`: Cự ly tới vật cản gần nhất phía trước, chuẩn hóa bằng cách chia cho 50.0 và giới hạn trong đoạn 0.0 đến 1.0.
3. `normRelSpeed`: Vận tốc tương đối giữa xe đang xét và xe phía trước, chuẩn hóa bằng cách chia cho 15.0 và giới hạn trong đoạn -1.0 đến 1.0.
4. `speedError`: Sai số giữa vận tốc mục tiêu và vận tốc thực tế, chuẩn hóa trong đoạn -1.0 đến 1.0.
5. `normRoadOffset`: Độ lệch vị trí phương tiện so với tim phần đường quy định của chiều chạy, giá trị từ -1.0 đến 1.0.
6. `sideBalance`: Cân bằng cự ly hai bên sườn xe, tính bằng hiệu khoảng trống bên phải trừ bên trái chia cho 5.0, giá trị từ -1.0 đến 1.0.
7. `isLeaderBraking`: Trạng thái bật đèn phanh của xe phía trước (1.0 nếu đang phanh, 0.0 nếu bình thường).
8. `pedestrianDetected`: Nhận diện người đi bộ cắt ngang hành lang di chuyển phía trước (1.0 nếu có, 0.0 nếu an toàn).
9. `trafficDensity`: Mật độ phương tiện cục bộ tại vị trí xe, trích xuất từ hệ thống quản lý trường mật độ (0.0 đến 1.0).
10. `oppositeClearGap`: Cự ly thông thoáng của làn ngược chiều kế bên tim đường, chuẩn hóa trong đoạn 0.0 đến 1.0.
11. `isOvertakingOpposite`: Trạng thái xe đang thực hiện hành vi mượn làn ngược chiều (1.0 nếu đang trên làn ngược chiều, 0.0 nếu đang ở làn nhà).
12. `isChangingLane`: Trạng thái xe đang trong quá trình chuyển dịch ngang giữa các làn đường (1.0 nếu đang chuyển làn, 0.0 nếu bám làn ổn định).
13. `adjacentClearGap`: Khoảng trống an toàn trên làn kế bên mà xe dự định chuyển sang (0.0 đến 1.0).

### 1.3. 10 Giá Trị Đầu Ra Chính Sách

Đầu ra của mô hình điều phối đồng thời cả quyết định chiến thuật lẫn thao tác điều khiển:

Tầng 1 - Quyết định hành vi bậc cao (lấy vị trí có điểm số cao nhất trong 8 đầu ra đầu tiên):
* Trạng thái 0 (FreeFlow): Đường thông thoáng, xe duy trì vận tốc ổn định theo tốc độ thiết kế.
* Trạng thái 1 (CarFollowing): Bám đuôi xe trước, tự động giữ cự ly an toàn theo biến thiên vận tốc xe dẫn đầu.
* Trạng thái 2 (ActiveEvasion): Chủ động đổi sang làn kế bên cùng chiều khi phát hiện vật cản hoặc xe phía trước di chuyển quá chậm.
* Trạng thái 3 (SafeYielding): Giảm tốc độ hoặc phanh dừng an toàn trước vạch kẻ đường khi có người đi bộ qua đường.
* Trạng thái 4 (EmergencyShield): Kích hoạt chế độ phanh khẩn cấp và tách va chạm khi cự ly nguy cấp.
* Trạng thái 5 (OppositeOvertake): Vượt xe bằng cách mượn làn đối diện khi làn nhà bị tắc nghẽn và chiều ngược lại hoàn toàn thông thoáng.
* Trạng thái 6 (AbortLaneChange): Tự động hủy chuyển làn khi phát hiện làn kế bên bị kẹt đột ngột, bẻ lái căn thẳng xe trở lại tim làn cũ để triệt tiêu góc nghiêng, ngăn ngừa phương tiện chắn ngang đường.
* Trạng thái 7 (CreepForward): Tự động bò nhích tới ở dải vận tốc thấp khi xe trước di chuyển nhẹ hoặc xuất hiện khoảng trống ngắn, giải tỏa dòng ùn ứ.

Tầng 2 - Chấp hành điều khiển liên tục:
* Đầu ra thứ 9 (chỉ số 8): Giá trị gia tốc chuẩn hóa từ -1.0 đến +1.0, được ánh xạ vào dải gia tốc vật lý cực đại của phương tiện.
* Đầu ra thứ 10 (chỉ số 9): Giá trị góc lái chuẩn hóa từ -1.0 đến +1.0, được ánh xạ vào góc đánh lái tối đa của phương tiện.

---

## 2. Quy Trình Huấn Luyện Mô Hình

Các mô hình được huấn luyện bằng PyTorch thông qua 3 kịch bản chính:
* `train_adaptive_traffic_agent.py`: Huấn luyện mô hình ô tô toàn diện hỗ trợ chuyển làn, vượt xe ngược chiều, hủy chuyển làn và bò nhích giải tỏa ùn tắc.
* `train_motorcycle_agent.py`: Huấn luyện mô hình xe máy với đặc thù phản xạ nhanh, gia tốc bốc, góc quay linh hoạt và khả năng luồn lách qua các khoảng trống hẹp.
* `train_opposite_overtake_agent.py`: Kịch bản thử nghiệm cơ chế mượn làn đối diện và thu hồi làn đường.

### 2.1. Tổng Hợp Dữ Liệu Huấn Luyện

Mỗi kịch bản khởi tạo từ 150.000 đến 180.000 mẫu dữ liệu mô phỏng ngẫu nhiên phân bố đều trên toàn bộ không gian trạng thái giao thông. Các nhãn hành vi và mục tiêu điều khiển được gán theo quy tắc chuyên gia kết hợp hàm mục tiêu:
* Ngưỡng phanh khẩn cấp: Áp dụng khi cự ly dưới 4% tầm quét hoặc khi vận tốc tiếp cận âm vượt ngưỡng an toàn.
* Ngưỡng nhường đường người đi bộ: Ưu tiên dừng xe khi người đi bộ xuất hiện trong hành lang cắt ngang.
* Quyết định hủy chuyển làn: Kích hoạt khi xe đang trong trạng thái chuyển làn nhưng khoảng trống làn mới bị thu hẹp đột ngột dưới mức an toàn hoặc vận tốc bị nghẽn.
* Quyết định bò nhích: Kích hoạt khi vận tốc gần như dừng hẳn, khoảng cách phía trước từ 2.5m đến 14m và không có người đi bộ.
* Quyết định mượn làn vượt xe: Kích hoạt khi mật độ làn nhà cao hoặc xe trước đi chậm, đồng thời khoảng trống làn đối diện đảm bảo tầm nhìn an toàn lớn.

### 2.2. Hàm Mất Mát Và Tối Ưu Hóa

Quá trình học sử dụng hàm mất mát kết hợp đa nhiệm:
* Mất mát phân loại hành vi: Sử dụng hàm mất mát phân loại (CrossEntropyLoss) đo sai số giữa nhãn hành vi chuyên gia và điểm số dự đoán của nhánh quyết định.
* Mất mát điều khiển liên tục: Sử dụng hàm mất mát hồi quy (MSELoss) đo sai số bình phương trung bình giữa gia tốc, góc lái dự đoán và mục tiêu điều khiển.
* Tổng mất mát: Kết hợp tuyến tính `Loss = Loss_cls + trọng_số * Loss_reg` với trọng số 1.8 đối với ô tô và 2.0 đối với xe máy.

Thuật toán tối ưu hóa (Optimizer) Adam được áp dụng với tốc độ học ban đầu từ 0.0025 đến 0.003, kết hợp bộ điều chỉnh suy giảm hàm cos (CosineAnnealingLR) qua 15 chu kỳ huấn luyện để đạt độ chính xác phân loại trên 99.8%.

### 2.3. Xuất Mô Hình Sang ONNX

Sau khi huấn luyện hoàn tất, mô hình được xuất sang định dạng ONNX chuẩn phiên bản toán tử 13 (opset 13), cho phép kích thước lô biến thiên theo số lượng phương tiện thực tế chạy trong cảnh mô phỏng.

---

## 3. So Sánh Động Lực Học Giữa Ô Tô Và Xe Máy

Dự án mô phỏng hai loại phương tiện với sự khác biệt rõ rệt về cấu hình vật lý và chiến thuật điều khiển:

| Đặc tính | Phương tiện Ô tô | Phương tiện Xe máy |
| :--- | :--- | :--- |
| File mô hình ONNX | `traffic_aif_agent.onnx` | `traffic_motorcycle_agent.onnx` |
| Kích thước bao (Rộng x Cao x Dài) | 1.8m x 1.0m x 4.0m | 0.6m x 1.0m x 2.67m (bằng 1/3 bề rộng ô tô) |
| Khối lượng vật lý | 1500 kg | 300 kg |
| Gia tốc tối đa | 8.0 m/s2 | 12.0 m/s2 (tăng tốc và phanh nhạy hơn) |
| Góc đánh lái tối đa | 18 độ | 28 độ (góc bẻ lái cơ động hơn) |
| Khoảng cách phanh khẩn cấp | 2.5 m | 1.8 m |
| Cự ly an toàn làn đối diện để vượt | Tối thiểu 35 m | Tối thiểu 22 m |
| Thời gian giãn cách giữa 2 lần đổi làn | 2.0 - 5.0 giây | 0.8 - 2.0 giây (thường xuyên đổi làn né vật cản) |
| Dãn cách xuất phát dọc trục đường | 18.0 - 36.0 m | 6.0 - 14.0 m |
| Độ lệch ngang tim làn xuất phát | 0.0 m (chính giữa làn) | Lệch linh hoạt sang trái hoặc phải 0.65 m |

---

## 4. Suy Luận Thời Gian Thực Trên Unity

Tại mỗi khung hình vật lý trong Unity, lớp điều khiển `AIFVehicleAgent` thực hiện các bước sau:
1. Trích xuất dữ liệu cảm biến: Sử dụng các tia quét phía trước, hai bên hông và phía sau để tính toán cự ly, vận tốc tương đối và khoảng trống an toàn.
2. Xây dựng véc-tơ đầu vào: Chuẩn hóa 13 tham số môi trường và đóng gói vào mảng Tensor.
3. Thực thi suy luận qua Sentis: Đưa véc-tơ qua mô hình mạng nơ-ron sâu để nhận về 10 giá trị đầu ra.
4. Điều phối hành vi:
   * Nếu mô hình chọn trạng thái vượt làn đối diện, xe bật đèn báo rẽ trái, dịch chuyển mục tiêu tim làn sang phần đường ngược chiều và gia tăng tốc độ.
   * Khi gặp xe đối đầu tiến lại gần dưới 30m hoặc phát hiện làn nhà đã thông thoáng, xe tự động bật đèn báo rẽ phải và thu hồi về phần đường của mình.
   * Nếu đang trong quá trình chuyển làn mà gặp vật cản kẹt, hành vi hủy chuyển làn sẽ cưỡng bức đưa góc xe trở lại song song với tim đường, triệt tiêu nguy cơ nằm chéo làn.
   * Khi dòng phương tiện phía trước dừng, hành vi bò nhích giúp xe di chuyển từ tốn ở vận tốc 1.5 - 2.5 m/s để lấp đầy các khoảng hở an toàn nhỏ.
5. Áp dụng lực vật lý: Chuyển hóa gia tốc và góc lái thành lực đẩy Rigidbody theo trục dọc và góc quay quanh trục đứng.

---

## 5. Cấu Hình Hạ Tầng Giao Thông & Môi Trường Mô Phỏng

### 5.1. Hình Học Lòng Đường

* Chiều rộng mặt đường: 16.0m (tọa độ ngang từ -8.0m đến +8.0m).
* Chiều dài tuyến đường: 500.0m (tọa độ dọc từ 0m đến 500m).
* Tim đường (tọa độ 0.0m): Mặt đường liền mạch, không có dải phân cách cứng, tạo điều kiện cho các phương tiện thực hiện hành vi mượn làn vượt xe khi đủ điều kiện an toàn.
* Phân chia 4 làn xe đối xứng:
  * Làn 0 (Trái ngoài cùng): Tọa độ -6.0m, chiều di chuyển theo hướng trục âm Z.
  * Làn 1 (Trái trong cùng): Tọa độ -2.0m, chiều di chuyển theo hướng trục âm Z.
  * Làn 2 (Phải trong cùng): Tọa độ +2.0m, chiều di chuyển theo hướng trục dương Z.
  * Làn 3 (Phải ngoài cùng): Tọa độ +6.0m, chiều di chuyển theo hướng trục dương Z.
* Rào chắn hai bên: Bố trí tại tọa độ ngang cộng trừ 8.15m với chiều cao 1.2m dọc toàn tuyến.

### 5.2. Hệ Thống Vạch Sang Đường Và Người Đi Bộ

* Cụm vạch qua đường: Được đặt tại các vị trí dọc trục đường ở 120m, 250m và 380m. Mỗi cụm gồm 15 vạch trắng song song, không gắn va chạm vật lý và thuộc lớp nhận diện bỏ qua tia quét để xe lưu thông êm thuận.
* Tác nhân người đi bộ: Hoạt động qua lớp điều khiển `PedestrianAgent`, tự động băng qua đường từ cả hai hướng trái sang phải và ngược lại. Tác nhân được trang bị hệ thống 5 tia quét phát hiện xe từ xa, tự động dừng lại nhường đường khi có xe đang lao tới và tiếp tục hành trình khi khoảng trống đã an toàn.

### 5.3. Sinh Phương Tiện Tự Động

Bộ điều phối `TrafficSpawner` quản lý việc xuất hiện đồng thời của cả ô tô và xe máy:
* Tỷ lệ cơ cấu phương tiện: Khoảng 8 ô tô và 28 xe máy phân bố trên cả 4 làn đường.
* Thuật toán khử trùng lặp vị trí: Tự động rà soát tọa độ khi sinh xe để đảm bảo khoảng cách giãn cách tối thiểu, loại bỏ hoàn toàn hiện tượng lồng ghép vật lý lúc bắt đầu mô phỏng.
* Yếu tố tính cách tài xế: Mỗi phương tiện nhận một hệ số vội vàng ngẫu nhiên từ 0.75 đến 1.35, quyết định mức độ bám sát đuôi xe trước và tốc độ hành trình mong muốn.

### 5.4. Trực Quan Hóa Tín Hiệu Và Va Chạm

* Bộ điều khiển đèn xe: Quản lý hệ thống đèn pha phía trước và đèn hậu phía sau. Đèn hậu tự động chuyển màu đỏ rực khi xe thực hiện phanh hoặc dừng khẩn cấp.
* Trực quan hóa va chạm: Khi xảy ra va chạm vật lý với phương tiện khác hoặc rào chắn bên đường, toàn bộ thân xe sẽ chuyển sang màu đỏ cảnh báo và trở về màu sơn gốc khi thoát ly va chạm.

---

## 6. Hướng Dẫn Vận Hành

1. Mở dự án bằng phiên bản Unity 6 (khuyến nghị 6000.3.16f1 trở lên).
2. Mở cảnh mô phỏng tại đường dẫn `Assets/Scenes/TwoLaneSimulationScene.unity`.
3. Nếu cần tái tạo toàn bộ dữ liệu cấu hình hạ tầng và các Prefab phương tiện:
   * Chọn thanh công cụ trên menu: `Tools -> TwoLane Simulation -> Rebuild Scene and Prefabs`.
4. Nhấn nút Play trong Unity Editor để bắt đầu quá trình mô phỏng tương tác giao thông thời gian thực.
5. Để huấn luyện lại các mô hình trí tuệ nhân tạo:
   * Chạy lệnh `python train_adaptive_traffic_agent.py` để cập nhật mô hình ô tô.
   * Chạy lệnh `python train_motorcycle_agent.py` để cập nhật mô hình xe máy.
   * Các file ONNX đầu ra sẽ tự động được ghi đè trực tiếp vào thư mục `Assets/Models/` của dự án Unity.
