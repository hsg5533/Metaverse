# Metaverse (Unity 6 + Netcode for GameObjects)

멀티플레이 3D 공간. 아바타로 돌아다니고, 서로 보이고, 채팅한다.

## 구성

| 경로 | 역할 |
| --- | --- |
| `Scenes/Metaverse.unity` | 생성된 월드 씬 (빌드 세팅 0번) |
| `Prefabs/PlayerAvatar.prefab` | 인간형 아바타 - 머리·몸통·팔2·다리2 (NetworkObject + NetworkTransform 소유자 권한) |
| `Scripts/AvatarLimbAnimator.cs` | 이동 속도 보고 팔다리 흔드는 걷기 모션 (원격 플레이어도 적용) |
| `Scripts/PlayerAvatar.cs` | 이동/점프, 닉네임·색상 동기화, 머리 위 이름표 |
| `Scripts/FollowCamera.cs` | 3인칭 카메라 (우클릭 드래그 회전, 휠 줌) |
| `Scripts/ChatSystem.cs` | 서버 경유 채팅 릴레이 |
| `Scripts/MetaverseHUD.cs` | Host / Join 접속 메뉴, 접속 상태 패널 |
| `Scripts/NetText.cs` | 한글·이모지 안전한 FixedString 변환 |
| `Editor/MetaverseSceneBuilder.cs` | 씬·프리팹·머티리얼 전부 생성 (`Tools > Metaverse > Build World Scene`) |

## 실행

1. Unity 에디터에서 `Assets/Metaverse/Scenes/Metaverse.unity` 열기
2. Play → 왼쪽 위 패널에서 닉네임 입력 → **Host (play + serve)**
3. 다른 사람은 같은 씬에서 서버 IP 입력 → **Join as client**

기본 포트 `7777` (UDP). 같은 공유기 안이면 호스트의 내부 IP(`ipconfig`), 밖에서 붙으려면 7777 포트포워딩 필요.

## 조작

- `WASD` / 방향키 — 이동
- `Shift` — 달리기
- `Space` — 점프
- 마우스 우클릭 드래그 — 시점 회전, 휠 — 줌
- `Enter` — 채팅창 포커스, 다시 `Enter` — 전송

## 커맨드라인 자동 접속

빌드한 exe는 클릭 없이 바로 붙을 수 있다 (전용 서버·테스트용):

```
Metaverse.exe -mvhost -mvnick 호스트
Metaverse.exe -mvclient 192.168.0.10 -mvnick 손님
Metaverse.exe -batchmode -nographics -mvhost        # 화면 없는 전용 서버
```

`-mvport 7777`로 포트도 지정 가능.

## 한 PC에서 여러 명 테스트

- **방법 A (권장)**: `File > Build Settings > Build`로 exe 뽑고, 에디터에서 Host + exe 여러 개 Join
- **방법 B**: `Window > Multiplayer > Multiplayer Center`에서 Multiplayer Play Mode 패키지 설치 → 가상 플레이어 창으로 테스트

## 씬 다시 만들기

씬을 망가뜨렸으면 메뉴 `Tools > Metaverse > Build World Scene` 실행. 월드·프리팹·머티리얼을 덮어써서 새로 만든다.

## 다음에 붙일 만한 것

- 인터랙션 오브젝트 (앉기, 문, 포털)
- 음성 채팅 (Unity Vivox)
- 릴레이/로비 (Unity Gaming Services Relay — IP 없이 코드로 접속)
- 아바타 커스터마이즈 저장
