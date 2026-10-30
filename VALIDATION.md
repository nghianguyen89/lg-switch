# Validation — 1.0.1, 28/10/2026, Asia/Saigon

| Hạng mục | Kết quả |
|---|---|
| Tái hiện lỗi vẽ text của bản1.0 | PASS: bài thử QuietButton ghi223 pixel ra ngoài vùng nút trước sửa |
| Kiểm tra hồi quy sau sửa | PASS:0 pixel ngoài vùng; full-buffer repaint, normal/hover/disabled/partial clip cho cả hai loại nút; focus dùng GDI+ |
| Build Framework C#5 x64, warnings as errors | PASS |
| 51 tự kiểm tra, gồm nhóm kiểm tra Intel | PASS |
| Manifest EXE phát hành | PASS: đọc trực tiếp PE resource, requireAdministrator và uiAccess=false |
| Luồng hỏi quyền khi bấm input | Đã bỏ; quyền được Windows yêu cầu ngay khi mở app |
| Preview render150% | PASS, text/caption/badge nằm trong vùng |
| Cửa sổ UI thử trực tiếp | PASS: đã mở cửa sổ helper ở150%, các nút đầu/cuối và góc card không còn text chồng |
| UAC của EXE phát hành / worker elevated thực tế | NOT RUN trong lần sửa này |
| Chuyển input thật bằng binary1.0.1 | NOT RUN; backend và khoảng chờ không sửa |
| Source ZIP sạch / build lại từ ZIP | PASS: source archive được giải nén và build lại bằng helper không cần admin |
| Git manifest / dữ liệu runtime | PASS: Git index tách biệt loại runtime; diff --check không lỗi |

Người dùng đã xác nhận chu trình sang HDMI và về DP + Extend hoạt động với bản0.3. Bản1.0.1 chỉ sửa renderer và thời điểm yêu cầu admin; không tuyên bố đã thử lại chuyển màn bằng binary mới.

Renderer hiện tự vẽ kín ClientRectangle trong OnPaint, giữ clip/transform và khôi phục Graphics state. Các control không dùng TextRenderer hoặc GDI focus rectangle trong nhánh vẽ tối. Điều này xử lý trường hợp repaint một phần khác với ảnh render toàn cửa sổ.

Build không chạy EXE admin. Helper kiểm tra dùng manifest asInvoker, entry point giới hạn ở kiểm tra, render/live preview dữ liệu mẫu và đọc PE manifest. Preview bỏ qua tray/IPC, khởi tạo settings, refresh, input, options và diagnostics; không có nhánh dispatch DDC/watchdog.

Không thay đổi cài đặt UAC, tạo scheduled task/service hay tự cấp quyền bền vững. Windows có thể hỏi UAC ở mỗi phiên app mới. Không chạy lệnh đổi input/topology trong lần sửa này.
