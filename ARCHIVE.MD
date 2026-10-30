# LG Switch — Bàn giao hội thoại và dự án

Ngày tổng hợp: 30/10/2026, Asia/Saigon. Phiên bản source được bàn giao: **1.0.1**.

Tài liệu này tổng hợp yêu cầu, quyết định, quá trình xử lý và trạng thái triển khai để Codex có thể tiếp tục ở dự án mới mà không cần đọc lại chat. Đây là bản tổng hợp kỹ thuật, không phải bản chép nguyên văn từng tin nhắn.

## 1. Phạm vi và nguồn đối chiếu

- Hội thoại hiện tại: các yêu cầu và phản hồi người dùng, cùng phần ngữ cảnh triển khai được giữ sau khi rút gọn chat.
- ChatGPT gốc: **Tạo tool chuyển màn hình**, ID `6ae1824e-0308-83ec-8951-b2d9b65739a7`. Đã đọc trang duy nhất trả về gồm yêu cầu ban đầu và phản hồi đầu; phần trả lời dài có bị cắt. Các đề xuất ban đầu không thay thế kết quả triển khai thực tế về sau.
- Đã đối chiếu source và tài liệu 1.0.1 còn trong bản đóng gói/thư mục kiểm tra của workspace cũ, gồm CURRENT_STATE, VALIDATION, BUILD, CHANGELOG, FEASIBILITY, project, manifest và các điểm chính trong source.
- Hai snapshot source đóng gói và source giải nén kiểm tra khớp SHA-256 ở toàn bộ **22 file** tại thời điểm bàn giao. Đây là kiểm tra tính nhất quán giữa hai bản còn lại, không phải bằng chứng bản đã di chuyển có cùng hash.
- Source/ZIP tại vị trí output ban đầu đã được người dùng di chuyển sang chỗ lưu riêng. Người dùng xác nhận **không cần đóng gói source lại**. Chưa xác định đường dẫn mới; Codex tiếp theo phải dùng thư mục dự án anh mở.
- Dự án được chuẩn bị để đưa lên Git; trong quá trình triển khai trước chưa tạo repository/commit/push. Không suy ra trạng thái Git của thư mục mới từ trạng thái workspace cũ.
- Không build, chạy app, gọi DDC hoặc thay đổi màn hình trong lần tạo tài liệu này. Các PASS triển khai dưới đây là kết quả lịch sử ngày 28/10/2026.

Workspace cũ để truy vết, không hard-code vào app hoặc script mới:

`C:\Users\nghia\Documents\Codex\2026-10-28\referenced-chatgpt-conversation-this-is-an`

Các đường dẫn source bên dưới đều tương đối với **root source dự án mới**, nơi chứa `LgSwitch.csproj` và `build-publish.ps1`.

## 2. Bài toán thực tế

Người dùng sử dụng một NUC Windows với hai màn hình rời:

| Thiết bị | Kết nối | Vai trò |
|---|---|---|
| LG 27UP600-W.ATV / LG ULTRAFINE | DisplayPort nối NUC | Nguồn chính khi làm việc trên NUC; bình thường là màn 1 |
| ASUS VA24EHF | HDMI nối NUC | Bình thường là màn 2; phải tiếp tục dùng được khi LG hiển thị thiết bị khác |
| Laptop Windows hoặc thiết bị ngoài | HDMI-1 hoặc HDMI-2 của LG | Tận dụng màn LG lớn khi cần |

Bình thường NUC chạy **Extend**, dùng cả LG và ASUS. Khi dùng laptop, trước đây anh phải chuyển input LG bằng joystick, rồi chọn Windows “Only screen 2” để NUC chỉ dùng ASUS. Khi quay lại NUC phải làm ngược cả hai thao tác.

Mục tiêu cuối cùng đã chốt là **ba nút input rõ ràng**, thay cho một nút toggle khó đoán:

| Nút | Input LG mong muốn | Topology NUC sau khi hoàn tất |
|---|---|---|
| DisplayPort | DP từ NUC | Extend LG + ASUS |
| HDMI-1 | Thiết bị ngoài ở HDMI-1 | Chỉ ASUS |
| HDMI-2 | Thiết bị ngoài ở HDMI-2 | Chỉ ASUS |

“Only screen 2” là ý nghĩa sử dụng của anh; implementation phải chọn đúng ASUS bằng định danh thiết bị, không được mặc định màn số 2 luôn là ASUS.

## 3. Yêu cầu UX đã chốt

- App nhỏ, portable, giao diện hiện đại hơn WinForms mặc định, có tray.
- DisplayPort là lựa chọn dành cho NUC; hai HDMI dành cho thiết bị ngoài.
- Nếu đọc được input hợp lệ từ LG thì hiển thị input đó. Nếu không đọc được, phải ghi rõ trạng thái chưa biết hoặc lệnh gần nhất, không giả vờ đã xác nhận input.
- **Bỏ popup hỏi có thấy hình hay không** và luồng đếm ngược yêu cầu người dùng xác nhận. Đây là yêu cầu rõ ràng của anh.
- Khi không thấy hình, anh sẽ bấm **DisplayPort** trên tool ở ASUS để quay lại. Joystick là phương án cuối.
- Giữ snapshot, kiểm tra lỗi và watchdog phục hồi; việc bỏ popup không đồng nghĩa bỏ các cơ chế bảo vệ nội bộ.
- Yêu cầu admin ngay khi mở app, thay vì hỏi quyền ở lần bấm input đầu tiên.
- Không tự tắt UAC hoặc cam kết “được cấp quyền sẵn” không cần Windows xác nhận. Manifest `requireAdministrator` chuyển thời điểm hỏi quyền sang lúc mở app; Windows vẫn có thể hỏi UAC mỗi phiên app mới.
- Không thêm service hoặc scheduled task để né UAC nếu chưa có yêu cầu riêng.
- Portable Hub: chỉ cần chạy EXE để mở panel. Chưa triển khai plugin/API tích hợp chuyên biệt. Không đổi hành vi mở app thành tự chuyển input.
- Người dùng đã chấp nhận chuyển tín hiệu chưa thật mượt nhưng dùng được. Không rút ngắn khoảng chờ driver một cách phỏng đoán.

## 4. Diễn biến và các lỗi đã giải quyết

### Khảo sát ban đầu và 0.1 / 0.2

Ban đầu cần xác định DDC/CI có hoạt động qua DP khi LG đang nhận HDMI hay không. Nếu NUC tắt đường DP trước khi gửi input thì có thể mất đường điều khiển.

Bản thử đầu có nhiều nút setup/chẩn đoán/test. Có lần hiển thị lỗi `Pass the input-only test first.` vì chưa qua bước thử input. UI phức tạp chưa phù hợp nhu cầu sử dụng thường ngày.

Sau đó người dùng làm rõ yêu cầu ba nút. Khi bấm HDMI, Windows chuyển ASUS-only đúng nhưng LG đen; kiểm tra joystick cho thấy LG **vẫn đang ở DisplayPort**. Kết luận quan trọng: màn đen do DP NUC bị disable, không chứng minh màn LG đã đổi input. ACK của API ghi cũng không đủ chứng minh firmware đã thực thi lệnh.

Đường Win32 VCP chuẩn và Intel thử đầu chưa giải quyết được đổi input. Cần kiểm tra phản hồi đọc thật trước khi cho phép gửi lệnh riêng LG.

### 0.3 — chu trình thật đã hoạt động

Thêm Intel raw AUX/I2C với phép đọc độ sáng làm preflight, kiểm tra envelope/checksum và đối chiếu giá trị Win32 trước SET. Sau bản này người dùng báo: **“chạy được ngon lành”**, rồi xác nhận **“chạy ok luôn”**.

Đã ghi nhận chu trình sang HDMI và quay lại DP + Extend chạy được trên bộ máy hiện tại. Chưa ghi riêng bài thử vòng đầy đủ của HDMI-2; không tự nâng thành tất cả cổng đều đã nghiệm thu.

### 1.0 — chốt UX

Chuyển sang giao diện tối với ba card nguồn; thêm trạng thái lệnh gần nhất, tray, tài liệu/build/source sẵn sàng lưu Git. Bỏ popup xác nhận hình ảnh theo yêu cầu. Backend Intel và các khoảng chờ đã chạy ổn được giữ.

### 1.0.1 — sửa repaint và admin khi mở

Người dùng gửi ảnh các chữ “LG Switch” và dòng phụ bị lặp ở nút góc phải/tray, góc card đen. Nguyên nhân là custom paint dùng GDI TextRenderer/clear không giữ đúng clip/transform khi repaint một phần; ảnh render toàn form trước đó chưa bắt được lỗi này.

Sửa cả SourceButton và QuietButton: vẽ nền kín ClientRectangle trong clip giao nhau, Save/Restore Graphics, dùng GDI+ DrawString và vẽ focus bằng Pen. Bài tái hiện trước sửa ghi **223 pixel ngoài vùng nút**, sau sửa **0 pixel**. Đã kiểm tra cửa sổ UI thật qua helper ở mức scale 150%, không chỉ ảnh DrawToBitmap.

Manifest EXE phát hành đổi thành `requireAdministrator`, `uiAccess=false`; bỏ luồng nâng quyền/relaunch khi chọn input. Helper dành cho phát triển vẫn `asInvoker`, chỉ phục vụ self-test/preview/đọc PE manifest, không đóng gói vào portable.

## 5. Kiến trúc hiện tại

**C# 5, .NET Framework 4.8, Windows x64, WinForms. Không package ngoài/NuGet.** Dùng compiler Framework64 có sẵn. Không cần đổi framework để tiếp tục sửa tool nhỏ này.

| File | Trách nhiệm |
|---|---|
| `src/Program.cs` | Entry point, UI, tray, single instance/IPC, dispatch worker và preview |
| `src/ModernControls.cs` | SourceButton, QuietButton và renderer giao diện tối |
| `src/Core.cs` | Settings, điều phối chuyển input/topology, worker, mutex, snapshot và recovery/watchdog |
| `src/Native.cs` | P/Invoke DisplayConfig, nhận diện màn hình, snapshot native, Win32 DDC/CI |
| `src/IntelDdc.cs` | Intel IGCL, ánh xạ DP target, raw AUX/I2C và LG private VCP |
| `src/SelfTests.cs` | Kiểm tra logic/ABI/parser/transition/repaint không đổi màn thật |
| `scripts/CheckRunner.cs` | Entry point kiểm tra riêng, không dispatch sang điều khiển màn hình |
| `app.manifest` / `checks.manifest` | Quyền admin của bản phát hành / asInvoker của helper |
| `build-publish.ps1` | Build, self-test, kiểm tra manifest nhúng trong EXE |
| `assets/` | Icon và screenshot |
| `docs/CURRENT_STATE.md` | Trạng thái kỹ thuật ngắn để nối lại |
| `FEASIBILITY.md` | Ranh giới DDC/CI, thứ tự và nguồn tham khảo |
| `VALIDATION.md` | Bằng chứng PASS/NOT RUN của 1.0.1 |

Settings, log và recovery được lưu cạnh EXE. Không đưa chúng vào Git hoặc portable phát hành. `.gitignore` đã loại `release/`, `bin/`, `obj/`, settings, recovery, log, diagnostics, ZIP và các output tạm.

## 6. Contract kỹ thuật phải giữ

### Nhận diện và topology Windows

- Dùng DisplayConfig API, nhận diện theo device path/adapter LUID/target ID; không chỉ số màn hoặc nhãn UI.
- Bộ máy khảo sát: LG manufacturer GSM5BC2, ASUS AUS2445; LG target 8263 và ASUS target 48 trên adapter Intel khi đo. Đây là dữ liệu lịch sử, **không hard-code** target/LUID vì có thể thay đổi theo máy/driver.
- Chỉ xử lý cấu hình phù hợp; từ chối clone, màn dư hoặc ánh xạ mơ hồ thay vì đoán.
- Snapshot giữ nguyên path/mode gốc để khôi phục Extend. Khi tạo cấu hình ASUS-only, deep clone, giữ mode table phù hợp và đưa ASUS về origin (0,0).
- Validate supplied config trước Apply; không dùng `SAVE_TO_DATABASE` để ghi topology tạm vào cấu hình lâu dài.
- Không thay bằng `DisplaySwitch.exe /external` một cách máy móc: “external” không xác định chắc đúng ASUS trong bộ máy này.
- ABI đã kiểm tra: Luid 8, Source 20, Target 48, Path 72, Mode 64, TargetName 420, SourceName 84 byte. Không sửa struct packing tùy tiện.

### DDC/CI và Intel

- VCP chuẩn `0x60`: DP `0x0F`, HDMI-1 `0x11`, HDMI-2 `0x12`.
- LG thực tế từng trả input **0** ngay cả khi đang DP. Capabilities có `60(11 12 0F 00)` và F4; brightness VCP `0x10` đọc được 70/100 lúc đo. Không dùng giá trị 70 làm hằng kiểm tra.
- Backend Intel dùng `ControlLib.dll` từ **System32**, runtime do driver cung cấp; không chép DLL driver vào source/ZIP.
- Khớp đúng adapter LUID + target; chỉ chấp nhận DP ngoài trực tiếp, từ chối thiết bị/converter/ánh xạ không phù hợp.
- Lệnh riêng LG `0xF4`: HDMI-1 `0x90`, HDMI-2 `0x91`, DP `0xD0`.
- Trước SET phải có GET độ sáng raw hợp lệ, kiểm tra header/opcode/feature/result/type/checksum/range và khớp baseline Win32.
- Ưu tiên AUX khi GET hợp lệ, nếu không xét I2C theo preflight. Không gửi chồng SET qua transport thứ hai sau kết quả SET không chắc chắn.
- AUX framing được suy luận từ API/sample và kiểm tra bằng GET; không tuyên bố mọi firmware LG/Intel đều hỗ trợ giống nhau.
- Native handle phải được lấy lại sau đổi topology; Win32 physical monitor handles cần giải phóng đúng.

### Thứ tự chuyển màn và mất DDC

**DP → HDMI:** giữ/khôi phục đường LG hoạt động nếu cần → gửi lệnh HDMI → đợi driver khoảng 1.200 ms → apply ASUS-only → đợi khoảng 1.200 ms → kiểm tra topology.

**HDMI → DP:** restore snapshot Extend trước → đợi khoảng 1.500 ms → kiểm tra đường màn → lấy lại handle/định danh → gửi DP. Có retry có giới hạn, không retry mù khi timeout có kết quả không rõ.

**HDMI-1 ↔ HDMI-2:** cũng cần tạm khôi phục đường LG trước khi gửi lệnh qua NUC. Không giả định DDC luôn còn dùng được khi DP disable.

- Send HDMI thất bại thì không được tiếp tục tắt LG để sang ASUS-only.
- Không đặt popup hoặc yêu cầu readback input hợp lệ giữa send HDMI và ASUS-only; LG có thể không trả trạng thái dù đã thực hiện.
- Nếu HPD/DDC mất hẳn khi LG nhận HDMI, Windows/driver có thể không dựng lại được DP. Recovery vẫn là best effort; joystick là fallback cuối.
- Helper laptop điều khiển qua mạng, USB DDC và các phương án phần cứng khác **chưa triển khai**.

### Đồng thời, timeout và recovery

- Serialize thao tác cấp app; topology write và DDC native cùng dùng I/O mutex để tránh thay topology trong lúc driver còn chạy lệnh.
- DDC worker tách process, timeout khoảng **8 giây**; kill và xác nhận process kết thúc. Timeout có thể là kết quả ghi không rõ, không coi là lệnh chưa xảy ra.
- Watchdog độc lập khoảng **90 giây**, có snapshot và ràng buộc process identity để phục hồi khi app treo.
- Chỉ hủy guard khi thao tác thành công hoặc immediate recovery thành công. Recovery thất bại phải để guard còn armed và chặn thao tác mới phù hợp.
- Settings ghi atomic, có backup. Không sửa snapshot gốc khi tạo chế độ một màn.
- Không bỏ các cơ chế này để “mượt hơn” nếu chưa có bằng chứng và bài kiểm tra phù hợp.

### UI và quyền

- Phân biệt “Đang dùng” được đọc hợp lệ với “Đã gửi lệnh”/“Yêu cầu gần nhất”.
- Đóng cửa sổ thường ẩn xuống tray; Thoát kết thúc app. Cửa sổ ưu tiên nằm trên ASUS.
- Khi sửa renderer phải thử partial repaint, focus, hover/disabled và DPI 150%; ảnh render toàn form không đủ bắt lỗi đã gặp.
- Preview/helper không khởi tạo settings, tray/IPC hoặc dispatch phần cứng. Đừng dùng helper không admin làm đường vòng điều khiển màn hình.
- Admin-at-launch do Windows manifest, không phải cơ chế bỏ qua UAC.

## 7. Build và kiểm tra

Chạy tại root source, sau khi đóng app nếu đang dùng đúng thư mục output:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-publish.ps1
```

Output dự kiến: `release/LgSwitch-1.0.1/`. Helper kiểm tra: `obj/checks/LgSwitch.Checks.exe`.

Compiler đã dùng: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Visual Studio/MSBuild là đường thay thế khi có targeting pack 4.8; đường đã thực thi trong chat là script compiler.

Build script không chuyển input/topology và không chạy production EXE để hỏi UAC; dùng helper asInvoker để tự kiểm tra rồi đọc manifest PE của production EXE như dữ liệu.

| Phạm vi | Kết quả lịch sử 28/10/2026 | Giới hạn |
|---|---|---|
| Chu trình thật với 0.3 | Người dùng xác nhận chạy được hai chiều | Không mặc định áp dụng cho binary mới hoặc máy khác |
| HDMI-2 vòng đầy đủ | Chưa ghi nhận xác nhận riêng | Cần thử riêng nếu muốn chốt nghiệm thu |
| Build 1.0.1 x64, warnings as errors | PASS | Không chứng minh DDC thật |
| 51 self-checks | PASS | Kiểm tra logic/ABI/parser/renderer, không phải nghiệm thu màn thật |
| Repaint isolation | PASS: 223 pixel ngoài vùng trước sửa → 0 sau sửa | Cần giữ hồi quy khi sửa renderer |
| Preview 150% và cửa sổ UI-only thật | PASS | Không gửi lệnh phần cứng |
| Embedded PE manifest admin | PASS | Chưa thử UAC launch/worker elevated thực tế ở lần sửa 1.0.1 |
| Source ZIP giải nén build lại | PASS | Kết quả lịch sử của snapshot 1.0.1 |
| Git file selection/runtime exclusion/whitespace | PASS qua index kiểm tra riêng | Chưa tạo Git repo thực cho source lúc đó |
| Chuyển màn thật bằng binary 1.0.1 | NOT RUN | Backend và khoảng chờ giữ nguyên nhưng chưa nghiệm thu binary mới |

## 8. Cách thử tiếp an toàn

Không tự chạy thử phần cứng chỉ để đọc archive. Khi anh yêu cầu thử tiếp:

1. Kiểm tra đúng NUC, ASUS đang có hình và vẫn là nơi thao tác được; chuẩn bị joystick LG. Laptop/thiết bị HDMI cần đang bật và xuất hình.
2. Kiểm tra đúng phiên bản EXE và tránh chạy chồng bản cũ. Build/self-test trước nếu source có thay đổi.
3. Mở app phát hành, chấp nhận UAC bằng giao diện Windows; xác nhận UI không lỗi chữ ở DPI đang dùng.
4. Từ Extend, thử HDMI-1: LG phải hiển thị thiết bị HDMI, ASUS vẫn chạy NUC. Không coi LG đen là thành công; kiểm tra input bằng joystick nếu cần.
5. Từ ASUS bấm DP: NUC trở về Extend và LG nhận DP.
6. Thử riêng HDMI-2 với thiết bị có tín hiệu, rồi quay DP. Có thể thử HDMI-1 ↔ HDMI-2 sau khi từng vòng cơ bản ổn.
7. Khi lỗi, ưu tiên DP trên app; nếu không được, joystick DP và phục hồi Extend. Giữ log/chẩn đoán để truy vết, không xóa settings/recovery giữa lúc đang cần phục hồi.

Ghi kết quả kèm binary/version, cổng, topology trước/sau và triệu chứng; tách “lệnh được chấp nhận” khỏi “màn đã hiển thị đúng”.

## 9. Việc tiếp theo ở dự án mới

- Đặt file này vào root source và mở đúng thư mục bằng Codex.
- Đọc AGENTS.md nếu có; kiểm tra Git status/branch/diff và giữ mọi thay đổi hiện có của anh.
- Đọc `docs/CURRENT_STATE.md`, `VALIDATION.md`, `BUILD.md`, rồi các module liên quan nhu cầu mới.
- Xác nhận source đang là 1.0.1 trước khi sửa; không dùng các bản 0.1/0.2 để thay backend hiện tại.
- Nếu chưa có yêu cầu mới, chỉ tiếp nhận dự án và báo ngắn trạng thái. Không tự viết lại app, đổi stack, thêm dependency, auto-start/service, commit/push hoặc thử chuyển màn.
- Chỗ còn cần người dùng xác nhận nếu muốn nghiệm thu: binary 1.0.1 thật, UAC/worker elevated, HDMI-2; mức mượt chuyển tín hiệu là cải tiến tùy chọn cần đo trước.
- Người dùng sẽ tự tạo dự án/Git và đã chuyển source sang chỗ lưu. Archive không thay source, driver Intel hoặc dữ liệu runtime cá nhân; không xóa bản source đã lưu chỉ vì có tài liệu này.

## 10. Thông tin phụ từ chat

Đã có câu hỏi về lỗi Codex “Selected model is at capacity”. Trả lời trước phân biệt capacity của model với hạn mức tài khoản; dữ liệu usage lúc kiểm tra không tách capacity từng model. Các phần trăm quota được báo trong chat là snapshot nhất thời, không đưa vào yêu cầu app hoặc coi là trạng thái hiện tại. Đây không phải lỗi LG Switch.

## 11. Prompt dùng ở Codex dự án mới

Copy prompt sau sau khi đã mở thư mục source và đặt ARCHIVE.MD ở root:

```text
Em tiếp quản dự án LG Switch từ cuộc hội thoại trước. Hãy đọc AGENTS.md (nếu có), ARCHIVE.MD, docs/CURRENT_STATE.md, VALIDATION.md và BUILD.md trong thư mục hiện tại, rồi đối chiếu source trước khi kết luận. Dùng thư mục hiện tại làm root, không dựa vào đường dẫn workspace cũ.

Tool portable C# WinForms/.NET Framework 4.8 x64 này điều khiển LG 27UP600-W.ATV nối DisplayPort với NUC và giữ ASUS VA24EHF nối HDMI của NUC. Ba nút: DisplayPort = LG nhận NUC + Extend LG/ASUS; HDMI-1 hoặc HDMI-2 = LG nhận thiết bị ngoài + NUC chỉ ASUS. Backend Intel AUX/I2C với LG private VCP F4 đã chạy được trên máy anh từ 0.3; source bàn giao là 1.0.1.

Giữ các quyết định đã chốt: không popup xác nhận hình; nút DP để quay lại; không giả input đã xác nhận khi LG trả VCP60=0; admin ngay khi mở bằng manifest requireAdministrator, không né UAC. Giữ thứ tự gửi HDMI trước ASUS-only, restore Extend trước gửi DP, preflight brightness, nhận diện đúng màn, snapshot/mutex/watchdog và các khoảng chờ driver. Không viết lại backend đang chạy hoặc đổi framework nếu chưa có lý do cụ thể.

Trước tiên kiểm tra trạng thái Git và phiên bản thực tế, báo ngắn đã hiểu kiến trúc, phần nào đã được kiểm tra và phần nào còn NOT RUN. Chưa sửa code, chưa chuyển màn thật, chưa commit/push nếu anh chưa giao việc tiếp theo. Không coi PASS lịch sử của 0.3 hay self-test/preview là bằng chứng binary 1.0.1 đã được thử trên màn thật. Khi anh đưa yêu cầu mới, tiếp tục từ source hiện tại với thay đổi nhỏ nhất đáng tin cậy, xưng Em và trả lời tiếng Việt ngắn gọn.
```
