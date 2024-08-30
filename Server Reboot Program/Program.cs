using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreRCON;

namespace ServerMonitor
{
    class Program
    {
        private static string rconHost;
        private static ushort rconPort;
        private static string rconPassword;
        private static string serverDirectory;
        private static long memoryThresholdMB;
        private static int serverStartTimeoutSeconds;
        private static string jcmdPath;
        private static string shutdownMessage;
        private static int shutdownDelaySeconds;
        private static int restartDelaySeconds;
        private static int memoryUsageThresholdPercent;
        private static int memoryMonitoringIntervalSeconds;
        private static int memorySampleSize;
        private static string serverStartCommand;
        private static string serverProcessName;
        private static string serverCommandLineFilter;
        private static string serverStopCommand;

        private static Queue<long> memoryUsageSamples = new Queue<long>();

        static async Task Main(string[] args)
        {
            LoadConfiguration();

            while (true)
            {
                string minecraftPid = FindMinecraftServerPid();

                if (string.IsNullOrEmpty(minecraftPid))
                {
                    Console.WriteLine("Minecraft 서버 PID를 찾을 수 없습니다.");
                    return;
                }

                Console.WriteLine($"Minecraft 서버 PID: {minecraftPid}");

                long memoryUsage = GetJvmMemoryUsage(minecraftPid);

                if (memoryUsage > 0)
                {
                    Console.WriteLine($"[{DateTime.Now}] 현재 메모리 사용량: {memoryUsage / (1024 * 1024)} MB");
                    memoryUsageSamples.Enqueue(memoryUsage);

                    if (memoryUsageSamples.Count > memorySampleSize)
                    {
                        memoryUsageSamples.Dequeue();
                    }

                    long averageMemoryUsage = (long)Math.Round(memoryUsageSamples.Average());
                    long totalMemory = memoryThresholdMB * 1024 * 1024;
                    double memoryUsagePercent = (double)averageMemoryUsage / totalMemory * 100;

                    Console.WriteLine($"[{DateTime.Now}] 최근 메모리 사용량 평균: {averageMemoryUsage / (1024 * 1024)} MB ({memoryUsagePercent:F2}%)");

                    if (memoryUsagePercent > memoryUsageThresholdPercent)
                    {
                        Console.WriteLine($"[{DateTime.Now}] 메모리 사용량 평균이 임계값({memoryUsageThresholdPercent}%)을 초과했습니다. 재시작을 준비합니다...");

                        bool isCommandSent = await SendRconCommand(shutdownMessage);

                        if (isCommandSent)
                        {
                            Thread.Sleep(shutdownDelaySeconds * 1000);

                            await SendRconCommand(serverStopCommand);

                            if (WaitForServerToStop(60))
                            {
                                Console.WriteLine($"[{DateTime.Now}] 서버가 성공적으로 종료되었습니다.");

                                Thread.Sleep(restartDelaySeconds * 1000);

                                RestartServer();

                                if (await WaitForServerToStart(serverStartTimeoutSeconds))
                                {
                                    Console.WriteLine($"[{DateTime.Now}] 서버가 성공적으로 재시작되었습니다.");
                                }
                                else
                                {
                                    Console.WriteLine($"[{DateTime.Now}] 서버가 지정된 시간 내에 재시작되지 않았습니다.");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[{DateTime.Now}] 서버 종료에 실패했습니다.");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now}] RCON 명령어 전송에 실패했습니다.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now}] 메모리 사용량을 가져오지 못했습니다.");
                }

                Thread.Sleep(memoryMonitoringIntervalSeconds * 1000);
            }
        }

        static void LoadConfiguration()
        {
            try
            {
                string configText = File.ReadAllText("config.json");
                var config = JsonSerializer.Deserialize<Config>(configText);

                rconHost = config.RconHost;
                rconPort = config.RconPort;
                rconPassword = config.RconPassword;
                serverDirectory = config.ServerDirectory;
                memoryThresholdMB = config.MemoryThresholdMB;
                serverStartTimeoutSeconds = config.ServerStartTimeoutSeconds;
                jcmdPath = config.JcmdPath;
                shutdownMessage = config.ShutdownMessage;
                shutdownDelaySeconds = config.ShutdownDelaySeconds;
                restartDelaySeconds = config.RestartDelaySeconds;
                memoryUsageThresholdPercent = config.MemoryUsageThresholdPercent;
                memoryMonitoringIntervalSeconds = config.MemoryMonitoringIntervalSeconds;
                memorySampleSize = config.MemorySampleSize;
                serverStartCommand = config.ServerStartCommand;
                serverProcessName = config.ServerProcessName;
                serverCommandLineFilter = config.ServerCommandLineFilter;
                serverStopCommand = config.ServerStopCommand;

                Console.WriteLine("설정이 성공적으로 로드되었습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"설정 파일을 로드하는 중 오류 발생: {ex.Message}");
                throw;
            }
        }

        static string FindMinecraftServerPid()
        {
            try
            {
                Console.WriteLine("Minecraft 서버를 검색 중...");

                using (var searcher = new ManagementObjectSearcher($"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{serverProcessName}'"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var processId = (uint)obj["ProcessId"];
                        var commandLine = obj["CommandLine"]?.ToString();

                        if (commandLine != null && commandLine.Contains(serverCommandLineFilter))
                        {
                            Console.WriteLine($"Minecraft 서버 프로세스 발견. PID: {processId}");
                            return processId.ToString();
                        }
                    }
                }

                Console.WriteLine("Minecraft 서버 프로세스를 찾을 수 없습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Minecraft 서버 PID 검색 중 오류 발생: {ex.Message}");
            }

            return null;
        }

        static long GetJvmMemoryUsage(string jvmPid)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = jcmdPath,
                    Arguments = $"{jvmPid} GC.heap_info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string errorOutput = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        Console.WriteLine($"[{DateTime.Now}] jcmd 출력: {output}");
                        return ParseJcmdOutput(output);
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now}] jcmd 출력이 비어 있습니다. 오류 출력: {errorOutput}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"메모리 사용량을 가져오는 중 오류 발생: {ex.Message}");
            }

            return 0;
        }

        static long ParseJcmdOutput(string jcmdOutput)
        {
            try
            {
                string[] lines = jcmdOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.Contains("garbage-first heap"))
                    {
                        string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "used")
                            {
                                string usedMemoryString = parts[i + 1].Replace("K", "");
                                if (long.TryParse(usedMemoryString, out long usedMemoryKB))
                                {
                                    long usedMemoryBytes = usedMemoryKB * 1024;
                                    return usedMemoryBytes;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"jcmd 출력 파싱 중 오류 발생: {ex.Message}");
            }

            return 0;
        }

        static async Task<bool> SendRconCommand(string command)
        {
            try
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(rconHost), rconPort);

                using (var rcon = new RCON(endpoint, rconPassword))
                {
                    await rcon.ConnectAsync();
                    string response = await rcon.SendCommandAsync(command);
                    Console.WriteLine($"[{DateTime.Now}] 명령어 전송: {command}");
                    Console.WriteLine($"[{DateTime.Now}] 응답: {response}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RCON 명령어 전송 중 오류 발생: {ex.Message}");
                return false;
            }
        }

        static bool WaitForServerToStop(int timeoutSeconds)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            string minecraftPid = FindMinecraftServerPid();

            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                if (string.IsNullOrEmpty(minecraftPid))
                {
                    memoryUsageSamples.Clear();  // 서버 종료 시 메모리 샘플 초기화
                    return true;
                }

                minecraftPid = FindMinecraftServerPid();
                Thread.Sleep(1000);
            }

            return false;
        }

        static void RestartServer()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {serverStartCommand}",
                    WorkingDirectory = serverDirectory,
                    CreateNoWindow = false,
                    UseShellExecute = true
                };

                Process.Start(psi);
                Console.WriteLine($"[{DateTime.Now}] 서버를 재시작하는 명령어를 실행했습니다: {serverStartCommand}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"서버 재시작 중 오류 발생: {ex.Message}");
            }
        }

        static async Task<bool> WaitForServerToStart(int timeoutSeconds)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                try
                {
                    using (var rcon = new RCON(new IPEndPoint(IPAddress.Parse(rconHost), rconPort), rconPassword))
                    {
                        await rcon.ConnectAsync();
                        Console.WriteLine("서버가 부팅되어 RCON 연결이 가능합니다.");
                        return true;
                    }
                }
                catch
                {
                    Console.WriteLine("서버가 아직 부팅 중입니다. 대기 중...");
                    await Task.Delay(5000);
                }
            }

            return false;
        }
    }

    class Config
    {
        public string RconHost { get; set; }
        public ushort RconPort { get; set; }
        public string RconPassword { get; set; }
        public string ServerDirectory { get; set; }
        public long MemoryThresholdMB { get; set; }
        public int ServerStartTimeoutSeconds { get; set; }
        public string JcmdPath { get; set; }
        public string ShutdownMessage { get; set; }
        public int ShutdownDelaySeconds { get; set; }
        public int RestartDelaySeconds { get; set; }
        public int MemoryUsageThresholdPercent { get; set; } // 메모리 사용량 임계값 (퍼센트)
        public int MemoryMonitoringIntervalSeconds { get; set; } // 메모리 모니터링 주기 (초)
        public int MemorySampleSize { get; set; } // 샘플 크기
        public string ServerStartCommand { get; set; } // 서버를 시작하는 명령어
        public string ServerProcessName { get; set; } // 서버가 사용하는 메인 프로세스의 이름
        public string ServerCommandLineFilter { get; set; } // 서버 프로세스를 식별하기 위한 명령어 라인 필터
        public string ServerStopCommand { get; set; } // 서버를 종료하는 명령어
    }
}
