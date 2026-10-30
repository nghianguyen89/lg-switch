# Changelog

## 1.0.1 — 28/10/2026

- Sửa chữ/nền bị vẽ chồng ở các nút nhỏ và góc ô chọn nguồn: vẽ kín vùng nút, giữ clip/transform, dùng chung pipeline GDI+ cho text và focus.
- Kiểm tra hồi quy tái hiện 223 pixel vẽ ra ngoài vùng nút trước sửa; sau sửa là0.
- Manifest yêu cầu admin ngay khi mở, bỏ nâng quyền/khởi động lại khi bấm input.
- Thêm helper build để tự kiểm tra và xem UI không cần admin, không khởi tạo điều khiển màn hình.
- Giữ nguyên backend Intel, thứ tự chuyển màn và các khoảng chờ đã chạy ổn.

## 1.0 — 28/10/2026

- Giao diện tối, ba ô nguồn với icon, hover/focus và trạng thái riêng cho lệnh gần nhất/input đọc được.
- Bỏ popup xác nhận hình ảnh và đếm ngược theo yêu cầu người dùng.
- Giữ preflight Intel, nhận diện đúng màn, snapshot, watchdog và nút DP quay về NUC.
- Bỏ lượt đọc input và chờ xác nhận nằm giữa lệnh HDMI và chuyển ASUS-only; giữ các khoảng chờ cho driver.
- Chuẩn bị source cho Git; runtime và các bản build được ignore.

## 0.3

Thêm preflight AUX/I2C GET và kiểm tra phản hồi trước SET; người dùng xác nhận chu trình hai chiều hoạt động.

## 0.1 / 0.2

Các bản khảo sát DDC chuẩn và Intel ban đầu. Người dùng ghi nhận Windows chuyển layout nhưng LG chưa đổi input; được giữ riêng để đối chiếu, không đóng gói vào source hiện tại.
