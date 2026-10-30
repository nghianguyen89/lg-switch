# Build và đóng gói

Yêu cầu Windows x64, .NET Framework4.8. Dùng compiler Framework64 có sẵn; không cài package ngoài.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-publish.ps1
```

Script build x64 với warnings as errors, icon/manifest nhúng và chạy tự kiểm tra. Kết quả ở release/LgSwitch-1.0.1/. Không tự chuyển input, thay đổi topology, đăng ký startup hoặc khởi chạy UAC.

Script tạo thêm obj/checks/LgSwitch.Checks.exe với entry point kiểm tra riêng và manifest asInvoker. Helper này chỉ chạy tự kiểm tra, xem trước UI hoặc đọc manifest PE, không chuyển lệnh tới backend màn hình và không nằm trong ZIP portable. Build dùng helper để kiểm tra mà không cần quyền admin; EXE phát hành luôn nhúng requireAdministrator. Có kiểm tra trực tiếp tài nguyên manifest của EXE, không chỉ đọc file XML source.

LgSwitch.csproj dành cho Visual Studio/MSBuild có targeting pack4.8. Script compiler là đường build đã được chạy. Nếu thiếu Framework, script chỉ rõ nguồn Microsoft. Đóng app trước khi rebuild vào cùng thư mục.

Runtime Intel do driver cung cấp: System32/ControlLib.dll. Không chép DLL driver vào source hoặc ZIP. App phát hành yêu cầu admin ngay khi mở; các tiến trình DDC/watchdog kế thừa quyền của phiên đó. Không còn luồng nâng quyền ở lần chọn input đầu tiên.

Bản portable cần EXE, LgSwitch.exe.config và tài liệu. Không gom settings.xml, recovery/, log hoặc chẩn đoán dùng thật vào ZIP phát hành. Source archive chỉ chọn source, assets, project, script và tài liệu; release/ đã được .gitignore loại khỏi Git.

--render-preview tạo preview.png cạnh EXE bằng cửa sổ dựng riêng, không có tray/IPC/khởi tạo phần cứng. Preview dùng dữ liệu minh hoạ và không chứng minh chuyển input.
