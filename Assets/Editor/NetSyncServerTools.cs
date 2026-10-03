#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
internal static class NetSyncServerTools
{
    private const string Key = "CoPlaceQuestSetup.NetSyncServer.";
    private static double nextPoll;
    private static long logOffset;
    private static string lastStatus;

    [Serializable]
    private class ServerState
    {
        public string token, status, message;
        public int supervisorPid, serverPid;
    }

    static NetSyncServerTools()
    {
        EditorApplication.update += Poll;
        EditorApplication.quitting += OnQuit;
    }

    [MenuItem("Tools/NetSync/Start Server")]
    internal static void StartServer()
    {
        try
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                Debug.LogWarning("[NetSync Server] This development tool currently supports macOS.");
                return;
            }
            if (IsRunning())
            {
                Debug.Log("[NetSync Server] Already running (supervisor PID " + SessionState.GetInt(Key + "Pid", 0) + ").");
                return;
            }
            // Leave externally launched servers untouched, including partial startups.
            foreach (int port in new[] { 5555, 5556, 5557, 8800 })
            {
                var listener = new TcpListener(IPAddress.Any, port);
                try { listener.Start(); }
                catch (SocketException)
                {
                    Debug.LogWarning($"[NetSync Server] Port {port} is occupied. Existing server/process kept; no new server started.");
                    return;
                }
                finally { listener.Stop(); }
            }
            Launch(ResolveExecutable("uvx"), ResolveExecutable("python3"));
        }
        catch (Exception error) { Debug.LogWarning("[NetSync Server] Start failed: " + error.Message); }
    }

    private static void Launch(string uvx, string python)
    {
        string token = Guid.NewGuid().ToString("N");
        string directory = Path.GetFullPath("Library/NetSyncServer/run-" + token);
        Directory.CreateDirectory(directory);
        string helper = Path.Combine(Application.dataPath, "Editor/NetSyncServerSupervisor.py");
        using (var owner = Process.GetCurrentProcess())
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = python,
            Arguments = Quote(helper) + " " + Quote(uvx) + " " + Quote(directory) + " " + Quote(token) + " " + owner.Id,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory
        }))
        {
            if (process == null) throw new InvalidOperationException("Supervisor did not start.");
            SessionState.SetString(Key + "Directory", directory);
            SessionState.SetString(Key + "Token", token);
            SessionState.SetInt(Key + "Pid", process.Id);
            SessionState.SetString(Key + "Started", process.StartTime.ToUniversalTime().Ticks.ToString());
        }
        logOffset = 0;
        lastStatus = null;
        Debug.Log($"[NetSync Server] Starting {uvx} styly-netsync-server@0.17.4. Logs: {directory}/server.log");
    }

    [MenuItem("Tools/NetSync/Stop Server")]
    internal static void StopServer()
    {
        SessionState.SetBool(Key + "Restart", false);
        RequestStop();
    }

    private static void RequestStop()
    {
        if (!IsRunning()) return;
        try
        {
            File.WriteAllText(Path.Combine(SessionState.GetString(Key + "Directory", ""), "stop.request"),
                SessionState.GetString(Key + "Token", ""));
            Debug.Log("[NetSync Server] Stop requested for this Editor's server.");
        }
        catch (Exception error) { Debug.LogWarning("[NetSync Server] Stop request failed: " + error.Message); }
    }

    [MenuItem("Tools/NetSync/Restart Server")]
    internal static void RestartServer()
    {
        if (!IsRunning()) { StartServer(); return; }
        SessionState.SetBool(Key + "Restart", true);
        RequestStop(); // Poll starts the replacement only after the supervisor exits.
    }

    [MenuItem("Tools/NetSync/Prepare Environment")]
    internal static void PrepareEnvironment()
    {
        NetSyncAutoServerAddress.RefreshServerAddress();
        StartServer();
    }

    [MenuItem("Tools/NetSync/Set uvx Path...")]
    private static void SetUvxPath()
    {
        string path = EditorUtility.OpenFilePanel("Select uvx executable (Cancel keeps current setting)", "", "");
        if (!string.IsNullOrEmpty(path)) EditorPrefs.SetString(Key + Application.dataPath + ".uvx", path);
    }

    private static string ResolveExecutable(string name)
    {
        string configured = EditorPrefs.GetString(Key + Application.dataPath + "." + name, "");
        if (!string.IsNullOrEmpty(configured))
        {
            if (File.Exists(configured)) return configured;
            throw new FileNotFoundException("Configured executable is missing: " + configured);
        }
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(entry)) continue;
            string candidate = Path.Combine(entry, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        string shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell) || !File.Exists(shell)) shell = "/bin/zsh";
        using (var process = Process.Start(new ProcessStartInfo
        {
            FileName = shell, Arguments = "-l -c " + Quote("command -v " + name),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        }))
        {
            if (process == null) throw new InvalidOperationException("Shell did not start.");
            if (!process.WaitForExit(2000)) { process.Kill(); throw new TimeoutException("Executable lookup timed out."); }
            string result = process.StandardOutput.ReadToEnd().Trim();
            if (process.ExitCode == 0 && Path.IsPathRooted(result) && File.Exists(result)) return result;
        }
        throw new FileNotFoundException(name + " was not found in PATH/login shell. Install it" +
            (name == "uvx" ? " or choose Tools > NetSync > Set uvx Path..." : "") + ".");
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static bool IsRunning()
    {
        int pid = SessionState.GetInt(Key + "Pid", 0);
        if (pid <= 0) return false;
        try
        {
            using (var process = Process.GetProcessById(pid))
                return !process.HasExited && process.StartTime.ToUniversalTime().Ticks.ToString() == SessionState.GetString(Key + "Started", "");
        }
        catch (Exception) { return false; }
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string directory = SessionState.GetString(Key + "Directory", "");
        if (string.IsNullOrEmpty(directory)) return;
        try
        {
            string statePath = Path.Combine(directory, "status.json");
            if (File.Exists(statePath))
            {
                var state = JsonUtility.FromJson<ServerState>(File.ReadAllText(statePath));
                if (state != null && state.token == SessionState.GetString(Key + "Token", "") && state.status != lastStatus)
                {
                    lastStatus = state.status;
                    if (state.status == "failed") Debug.LogWarning("[NetSync Server] " + state.message);
                    else Debug.Log($"[NetSync Server] {state.status} (server PID {state.serverPid}).");
                }
            }
            string logPath = Path.Combine(directory, "server.log");
            if (File.Exists(logPath))
            {
                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length < logOffset) logOffset = 0;
                    stream.Position = Math.Max(logOffset, stream.Length - 32768);
                    byte[] buffer = new byte[32768];
                    int count = stream.Read(buffer, 0, buffer.Length);
                    logOffset = stream.Position;
                    using (var reader = new StringReader(System.Text.Encoding.UTF8.GetString(buffer, 0, count)))
                    {
                        int displayed = 0;
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string upper = line.ToUpperInvariant();
                            if (displayed < 3 && (upper.Contains("ERROR") || upper.Contains("WARNING") || upper.Contains("TRACEBACK")))
                            {
                                Debug.LogWarning("[NetSync Server] " + line.Substring(0, Math.Min(line.Length, 1000)));
                                displayed++;
                            }
                        }
                    }
                }
            }
            if (!IsRunning() && SessionState.GetBool(Key + "Restart", false))
            {
                SessionState.SetBool(Key + "Restart", false);
                StartServer();
            }
        }
        catch (IOException) { /* Status/log file may be in the middle of a rotation. */ }
    }

    private static void OnQuit()
    {
        SessionState.SetBool(Key + "Restart", false);
        RequestStop(); // Supervisor also detects parent exit/crash independently.
    }
}
#endif
