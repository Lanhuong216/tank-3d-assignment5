# Hướng dẫn thiết lập Menu và Waiting Room

## Tổng quan

Hệ thống menu đã được tạo với các script sau:

- `MainMenu.cs` - Quản lý menu chính với HOST và JOIN buttons
- `WaitingRoom.cs` - Quản lý phòng chờ với danh sách players, READY và PLAY buttons
- `PlayerReadyNetwork.cs` - Network component để theo dõi trạng thái ready của players
- `NetworkGameManager.cs` - Quản lý cấu hình network

## Các bước thiết lập:

### 1. Tạo Scene MainMenu

1. Tạo scene mới tên "MainMenu"
2. Thêm GameObject và attach script `MainMenu.cs`
3. Tạo UI Canvas với các elements:
   - Button "HOST"
   - Button "JOIN"
   - Panel "JoinPanel" (ban đầu ẩn) chứa:
     - InputField để nhập IP
     - Button "Confirm"
     - Button "Cancel"
4. Gán các references vào MainMenu script:
   - `m_HostButton` → Button HOST
   - `m_JoinButton` → Button JOIN
   - `m_JoinPanel` → Panel JoinPanel
   - `m_IPInputField` → InputField IP
   - `m_ConfirmJoinButton` → Button Confirm
   - `m_CancelJoinButton` → Button Cancel

### 2. Tạo Scene WaitingRoom

1. Tạo scene mới tên "WaitingRoom"
2. Thêm GameObject và attach script `WaitingRoom.cs`
3. Tạo UI Canvas với các elements:
   - GameObject "PlayerListContent" (có thể là ScrollView Content hoặc Vertical Layout Group)
   - Prefab "PlayerListItemPrefab" (một GameObject đơn giản với Text component)
   - Button "PLAY" (chỉ hiển thị cho host)
   - Button "READY" (chỉ hiển thị cho client)
   - Text "StatusText" để hiển thị trạng thái
   - Button "LEAVE"
4. Gán các references vào WaitingRoom script

### 3. Thiết lập NetworkManager

1. Trong scene MainMenu, thêm GameObject "NetworkManager" (hoặc sử dụng NetworkManager có sẵn)
2. Add Component: NetworkManager
3. Add Component: Unity Transport (UTP)
4. Cấu hình NetworkManager:
   - Player Prefab: Tạo hoặc chọn một prefab có NetworkObject component
   - Thêm PlayerReadyNetwork component vào Player Prefab
   - Đảm bảo Player Prefab có NetworkObject component

### 4. Thiết lập Build Settings

1. File → Build Settings
2. Add Open Scenes:
   - MainMenu (đặt làm scene đầu tiên)
   - WaitingRoom
   - Main (scene game chính)

### 5. Cấu hình Network Scene Manager

1. Trong NetworkManager, enable "Enable Scene Management"
2. Thêm các scene vào Network Scene List:
   - WaitingRoom
   - Main

## Lưu ý:

- Đảm bảo Player Prefab có NetworkObject component
- Player Prefab nên có PlayerReadyNetwork component để tracking ready status
- Port mặc định là 7777 (có thể thay đổi trong NetworkGameManager)
- IP mặc định khi join là 127.0.0.1 (localhost)

## Flow hoạt động:

1. **HOST**: Click HOST → Vào WaitingRoom → Chờ players join → Click PLAY khi tất cả ready → Vào game
2. **CLIENT**: Click JOIN → Nhập IP → Click Confirm → Vào WaitingRoom → Click READY → Chờ host click PLAY → Vào game
