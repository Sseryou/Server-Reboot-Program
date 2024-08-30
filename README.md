# 서버 모니터링 및 자동 재시작 프로그램

이 프로그램은 Minecraft 서버의 메모리 사용량을 모니터링하고, 설정된 임계값을 초과할 경우 서버를 안전하게 재시작합니다. 프로그램은 `config.json` 파일에서 설정 값을 읽어와 동작합니다. 아래는 프로그램의 사용법과 작동 방식을 설명합니다.

## 프로그램 설정

1. **config.json 파일 설정**:
   - 프로그램을 실행하기 전에, 프로그램이 사용할 설정 값을 `config.json` 파일에 입력해야 합니다.
   - 설정 항목에 대한 자세한 설명은 `config_description.txt` 파일을 참조하십시오.

2. **config.json 파일의 위치**:
   - `config.json` 파일은 프로그램이 실행되는 디렉토리(프로그램의 `.exe` 파일이 있는 디렉토리)에 있어야 합니다.
   - 올바른 설정을 입력하고, 해당 파일이 프로그램과 동일한 디렉토리에 있는지 확인하세요.

## 주요 설정 항목

- **ServerStartCommand**: 서버를 시작할 때 사용하는 명령어를 설정합니다. 기본값은 `cmd.exe /C run.bat auto`입니다. 서버를 다른 방식으로 실행하는 경우, 이 명령어를 적절히 수정하세요.
- **ServerProcessName**: 서버의 메인 프로세스 이름을 설정합니다. 기본값은 `java.exe`입니다. 서버가 다른 프로세스를 사용하는 경우, 해당 프로세스 이름으로 수정하세요.
- **ServerCommandLineFilter**: 서버 프로세스를 식별하기 위한 명령어 라인 필터를 설정합니다. 기본값은 `minecraft`입니다. 서버가 다른 특성을 가지고 있다면 이 필터를 수정하세요.
- **ServerStopCommand**: 서버를 종료할 때 사용하는 명령어입니다. 기본값은 `stop`입니다. 서버가 다른 명령어를 사용하는 경우, 이 명령어를 수정하세요.

## 프로그램 작동 방식

1. **설정 파일 로드**:
   - 프로그램이 시작되면, 먼저 `config.json` 파일을 로드하여 설정 값을 메모리에 저장합니다.

2. **서버 PID 검색**:
   - 프로그램은 설정된 프로세스 이름(`ServerProcessName`)과 명령어 라인 필터(`ServerCommandLineFilter`)를 사용하여 Minecraft 서버의 프로세스 ID(PID)를 검색합니다.
   - 서버가 실행 중이면, JVM 메모리 사용량을 모니터링합니다.

3. **메모리 사용량 모니터링**:
   - 설정된 주기(`MemoryMonitoringIntervalSeconds`)마다 서버의 메모리 사용량을 기록합니다.
   - 최근 메모리 사용량의 평균값을 계산하고, 이 값이 설정된 임계값(`MemoryUsageThresholdPercent`)을 초과하는지 확인합니다.

4. **서버 재시작**:
   - 메모리 사용량이 설정된 임계값을 초과하면, 서버가 안전하게 종료되도록 합니다.
   - `ShutdownMessage`를 RCON을 통해 서버에 있는 모든 플레이어에게 알리고, 설정된 시간(`ShutdownDelaySeconds`)만큼 대기한 후 서버를 종료합니다.
   - 서버 종료 후 설정된 시간(`RestartDelaySeconds`)만큼 대기한 후 서버를 다시 시작합니다.

5. **재시작 후 확인**:
   - 서버가 재시작된 후, 설정된 시간(`ServerStartTimeoutSeconds`) 내에 서버가 정상적으로 부팅되고 RCON 연결이 가능한지 확인합니다.
   - 모든 과정이 완료되면, 프로그램은 다시 서버를 모니터링하기 시작합니다.

## 추가 정보

- `config.json` 설정에 대한 자세한 내용은 `config_description.txt` 파일을 참조하십시오.
- 프로그램의 작동과 관련된 문제나 질문이 있다면 [GitHub Issues](https://github.com/your-repository/issues)에 보고해 주세요.
