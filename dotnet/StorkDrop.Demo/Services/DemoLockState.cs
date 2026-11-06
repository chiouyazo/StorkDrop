using System.IO;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Demo.Services;

internal sealed class DemoLockState
{
    private readonly object _gate = new object();
    private readonly List<(string Dir, string FileName)> _armed = [];
    private readonly Dictionary<string, List<LockingProcessInfo>> _active = new Dictionary<
        string,
        List<LockingProcessInfo>
    >(StringComparer.OrdinalIgnoreCase);

    public void Arm(string directory, string fileName)
    {
        lock (_gate)
        {
            string key = Key(directory, fileName);
            _armed.RemoveAll(a => Key(a.Dir, a.FileName) == key);
            _armed.Add((directory, fileName));
            _active[key] = NewProcesses();
        }
    }

    public void Rearm()
    {
        lock (_gate)
        {
            foreach ((string dir, string file) in _armed)
                _active[Key(dir, file)] = NewProcesses();
        }
    }

    public IReadOnlyList<LockedFileInfo> GetLocked(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            List<LockedFileInfo> result = [];
            foreach (string path in paths)
            {
                string key = Key(
                    Path.GetDirectoryName(path) ?? string.Empty,
                    Path.GetFileName(path)
                );
                if (
                    _active.TryGetValue(key, out List<LockingProcessInfo>? processes)
                    && processes.Count > 0
                )
                    result.Add(
                        new LockedFileInfo
                        {
                            FilePath = path,
                            FileName = Path.GetFileName(path),
                            Processes = [.. processes],
                        }
                    );
            }

            return result;
        }
    }

    public bool Release(int processId)
    {
        lock (_gate)
        {
            foreach (List<LockingProcessInfo> list in _active.Values)
                list.RemoveAll(p => p.ProcessId == processId);
            return true;
        }
    }

    private static string Key(string dir, string file) =>
        Path.Combine(dir, file).ToLowerInvariant();

    private static List<LockingProcessInfo> NewProcesses() =>
        [
            new LockingProcessInfo
            {
                ProcessId = 4312,
                ProcessName = "StepsRunner",
                UserName = Environment.UserName,
                StartTime = DateTime.Now.AddHours(-2),
                SessionId = 1,
            },
            new LockingProcessInfo
            {
                ProcessId = 9120,
                ProcessName = "NovaMonitor",
                UserName = Environment.UserName,
                StartTime = DateTime.Now.AddMinutes(-17),
                SessionId = 1,
            },
        ];
}
