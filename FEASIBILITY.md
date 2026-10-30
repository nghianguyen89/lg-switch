# Điều khiển LG UP600 và ranh giới

Người dùng đã xác nhận v0.3 chuyển từ DP sang HDMI và quay lại DP + Extend thành công trên bộ NUC + LG27UP600 + ASUS. Đây là bằng chứng cho bộ máy/cổng/driver hiện tại; không phải xác nhận mọi firmware UP600 đều giống nhau. Chưa ghi nhận riêng một bài kiểm tra HDMI-2 đầy đủ.

Bản1.0 giữ backend đã chạy, bỏ popup theo yêu cầu. Nếu LG không trả input hợp lệ, app tiếp tục theo lệnh đã gửi rồi áp dụng layout; người dùng có thể bấm DisplayPort để quay lại. Không giả thành trạng thái monitor đã xác nhận.

## Các đường điều khiển

Chuẩn Win32: VCP0x60, DP0x0F, HDMI-10x11, HDMI-20x12. Màn hiện tại trả input0 dù đang dùng DP; các bản thử đầu nhận lệnh nhưng không đổi input.

Intel: ánh xạ đúng adapter LUID + Windows target ID, chỉ chấp nhận LG qua DP ngoài. Chỉ tải ControlLib từ System32. GET độ sáng qua AUX/I2C phải có envelope/checksum/range hợp lệ và khớp Win32 trước một SET F4. Ưu tiên AUX khi GET hợp lệ; không thử chồng SET qua transport khác sau kết quả không chắc chắn.

Gói AUX DDC là suy luận từ API và sample Intel, được kiểm tra bằng GET trước khi cho phép SET. Phản hồi GET hợp lệ chứng minh đường truyền của lệnh đọc; phần firmware thực hiện F4 vẫn phải quan sát thực tế.

## Windows và thứ tự

- Sang HDMI: gửi input khi đường LG còn bật, sau đó dùng supplied DisplayConfig để chỉ giữ đúng ASUS.
- Về DP: áp dụng snapshot Extend trước, đợi driver, tạo lại handle LG rồi gửi DP.
- Chuyển giữa hai HDMI cũng cần tạm bật lại đường LG để gửi lệnh.
- Không dựa vào số màn hình hoặc SDC_TOPOLOGY_EXTERNAL để chọn ASUS.
- Không SAVE_TO_DATABASE; snapshot gốc không bị sửa khi tạo cấu hình một màn.

Nếu HPD/DDC mất khi LG nhận nguồn khác, Windows hoặc driver có thể không khôi phục được đường DP. Nút DP, cơ chế phục hồi lỗi và watchdog là best effort. Phương án cuối là joystick. Helper trên laptop qua mạng chưa được triển khai.

## Nguồn tham khảo

- [Intel I2C/AUX sample](https://github.com/intel/drivers.gpu.control-library/blob/master/Samples/I2C_AUX_Samples/I2C_AUX_Sample_App.cpp)
- [Intel API](https://intel.github.io/drivers.gpu.control-library/Control/api.html)
- [ddcutil: LG input switching](https://github.com/rockowitz/ddcutil/wiki/Switching-input-source-on-LG-monitors)
- [Windows LG input switch tham khảo](https://github.com/Jason7536/lg-input-switch)
- [Microsoft SetDisplayConfig](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)
