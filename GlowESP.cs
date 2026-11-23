using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace GlowESP
{
    public struct ProcessModule
    {
        public ulong Base;
        public ulong Size;
    }

    public sealed class PMemory
    {
        [DllImport("ntdll.dll")]
        private static extern int NtReadVirtualMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint numberOfBytesToRead, out uint numberOfBytesRead);

        [DllImport("ntdll.dll")]
        private static extern int NtWriteVirtualMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten);

        public int ReadVirtualMemory(IntPtr handle, ulong address, byte[] buffer, uint size, out uint bytesRead)
        {
            return NtReadVirtualMemory(handle, new IntPtr(unchecked((long)address)), buffer, size, out bytesRead);
        }

        public int WriteVirtualMemory(IntPtr handle, ulong address, byte[] buffer, uint size, out uint bytesWritten)
        {
            return NtWriteVirtualMemory(handle, new IntPtr(unchecked((long)address)), buffer, size, out bytesWritten);
        }
    }

    public sealed class PProcess
    {
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint TH32CS_SNAPMODULE = 0x00000008;
        private const uint LIST_MODULES_64BIT = 0x00000003;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_READWRITE = 0x04;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;

        public uint Pid;
        public IntPtr Handle;
        public IntPtr Hwnd;
        public ProcessModule BaseClient;

        public bool AttachProcess(string processName, uint access)
        {
            Pid = FindProcessIdByProcessName(processName);

            if (Pid != 0)
            {
                IntPtr[] modules = new IntPtr[0xFF];
                uint needed = 0;

                Handle = OpenProcess(access, false, Pid);

                if (Handle == IntPtr.Zero)
                    return false;

                EnumProcessModulesEx(Handle, modules, (uint)(modules.Length * IntPtr.Size), out needed, LIST_MODULES_64BIT);
                BaseClient.Base = (ulong)modules[0].ToInt64();

                MODULEINFO moduleInfo;
                GetModuleInformation(Handle, modules[0], out moduleInfo, (uint)Marshal.SizeOf(typeof(MODULEINFO)));
                BaseClient.Size = moduleInfo.SizeOfImage;

                Hwnd = GetWindowHandleFromProcessId(Pid);

                return true;
            }

            return false;
        }

        public bool AttachProcess(string processName)
        {
            return AttachProcess(processName, PROCESS_ALL_ACCESS);
        }

        public bool AttachWindow(string windowName)
        {
            Pid = FindProcessIdByWindowName(windowName);

            if (Pid != 0)
            {
                IntPtr[] modules = new IntPtr[0xFF];
                uint needed = 0;

                Handle = OpenProcess(PROCESS_ALL_ACCESS, false, Pid);

                EnumProcessModulesEx(Handle, modules, (uint)(modules.Length * IntPtr.Size), out needed, LIST_MODULES_64BIT);
                BaseClient.Base = (ulong)modules[0].ToInt64();

                MODULEINFO moduleInfo;
                GetModuleInformation(Handle, modules[0], out moduleInfo, (uint)Marshal.SizeOf(typeof(MODULEINFO)));
                BaseClient.Size = moduleInfo.SizeOfImage;

                Hwnd = GetWindowHandleFromProcessId(Pid);

                return true;
            }
            return false;
        }

        public bool UpdateHwnd()
        {
            Hwnd = GetWindowHandleFromProcessId(Pid);
            return Hwnd != IntPtr.Zero;
        }

        public void Close()
        {
            CloseHandle(Handle);
        }

        public ProcessModule GetModule(string moduleName)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, Pid);
            MODULEENTRY32W entry = new MODULEENTRY32W();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32W));

            ProcessModule result = new ProcessModule();
            result.Base = 0;
            result.Size = 0;

            if (Module32FirstW(snapshot, ref entry))
            {
                do
                {
                    if (entry.szModule == moduleName)
                    {
                        CloseHandle(snapshot);
                        result.Base = (ulong)entry.modBaseAddr.ToInt64();
                        result.Size = entry.modBaseSize;
                        return result;
                    }
                } while (Module32NextW(snapshot, ref entry));
            }

            CloseHandle(snapshot);
            return result;
        }

        public IntPtr Allocate(int sizeInBytes)
        {
            return VirtualAllocEx(Handle, IntPtr.Zero, (UIntPtr)sizeInBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        }

        public ulong FindCodeCave(uint lengthInBytes)
        {
            byte[] cavePattern = new byte[lengthInBytes];
            return FindSignature(cavePattern);
        }

        public ulong FindSignature(byte[] signature)
        {
            int readSize = (int)BaseClient.Size;
            byte[] data = new byte[readSize];

            if (!ReadProcessMemory(Handle, new IntPtr(unchecked((long)BaseClient.Base)), data, new IntPtr(readSize), IntPtr.Zero))
                return 0;

            for (ulong i = 0; i < (ulong)readSize; i++)
            {
                for (ulong j = 0; j < (ulong)signature.Length; j++)
                {
                    if (signature[j] == 0x00)
                        continue;

                    if (i + j >= (ulong)readSize)
                        break;

                    if (data[(int)(i + j)] == signature[j])
                    {
                        if (j == (ulong)signature.Length - 1)
                            return BaseClient.Base + i;
                        continue;
                    }
                    break;
                }
            }
            return 0;
        }

        public ulong FindSignature(ProcessModule targetModule, byte[] signature)
        {
            int readSize = (int)targetModule.Size;
            byte[] data = new byte[readSize];

            if (!ReadProcessMemory(Handle, new IntPtr(unchecked((long)targetModule.Base)), data, new IntPtr(readSize), IntPtr.Zero))
                return 0;

            for (ulong i = 0; i < (ulong)readSize - (ulong)signature.Length; i++)
            {
                for (ulong j = 0; j < (ulong)signature.Length; j++)
                {
                    if (signature[j] == 0x00)
                        continue;

                    if (data[(int)(i + j)] == signature[j])
                    {
                        if (j == (ulong)signature.Length - 1)
                            return targetModule.Base + i;
                        continue;
                    }
                    break;
                }
            }
            return 0;
        }

        public ulong FindSignature(ProcessModule targetModule, byte[] signature, ulong offset)
        {
            int readSize = (int)targetModule.Size;
            if (signature == null || signature.Length == 0 || (ulong)signature.Length > (ulong)readSize)
                return 0;

            byte[] data = new byte[readSize];

            if (!ReadProcessMemory(Handle, new IntPtr(unchecked((long)targetModule.Base)), data, new IntPtr(readSize), IntPtr.Zero))
                return 0;

            return ScanSignature(data, signature, targetModule.Base, offset);
        }

        public static ulong ScanSignature(byte[] data, byte[] signature, ulong moduleBase, ulong offset)
        {
            if (signature == null || signature.Length == 0 || (ulong)signature.Length > (ulong)data.Length)
                return 0;

            ulong result = 0;
            for (ulong i = 0; i <= (ulong)data.Length - (ulong)signature.Length; i++)
            {
                bool found = true;

                for (ulong j = 0; j < (ulong)signature.Length; j++)
                {
                    if (signature[j] == 0x00)
                        continue;

                    if (data[(int)(i + j)] != signature[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    if (result != 0)
                        return 0;
                    result = moduleBase + i + offset;
                }
            }

            return result;
        }

        public ulong ReadOffsetFromSignature<T>(byte[] signature, byte offset) where T : struct
        {
            ulong patternAddress = FindSignature(signature);
            if (patternAddress == 0)
                return 0;

            T offsetValue = Read<T>(patternAddress + offset);
            ulong offsetValueUlong = Convert.ToUInt64(offsetValue);
            return patternAddress + offsetValueUlong + offset + (ulong)Marshal.SizeOf(typeof(T));
        }

        public bool ReadRaw(ulong address, byte[] buffer, int size)
        {
            uint bytesRead = 0;
            PMemory memory = new PMemory();

            int status = memory.ReadVirtualMemory(Handle, address, buffer, (uint)size, out bytesRead);

            return status == 0 /*STATUS_SUCCESS*/ || bytesRead == size;
        }

        public void Write<T>(ulong address, T value) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[size];

            GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(value, pinned.AddrOfPinnedObject(), false);
            }
            finally
            {
                pinned.Free();
            }

            PMemory memory = new PMemory();
            uint bytesWritten = 0;
            memory.WriteVirtualMemory(Handle, address, buffer, (uint)size, out bytesWritten);
        }

        public T Read<T>(ulong address) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[size];

            PMemory memory = new PMemory();
            uint bytesRead = 0;
            memory.ReadVirtualMemory(Handle, address, buffer, (uint)size, out bytesRead);

            GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(pinned.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                pinned.Free();
            }
        }

        public void WriteBytes(ulong addr, byte[] patch)
        {
            uint oldProtect;
            VirtualProtectEx(Handle, new IntPtr(unchecked((long)addr)), (UIntPtr)1, PAGE_EXECUTE_READWRITE, out oldProtect);

            PMemory memory = new PMemory();
            uint bytesWritten = 0;
            memory.WriteVirtualMemory(Handle, addr, patch, (uint)patch.Length, out bytesWritten);

            VirtualProtectEx(Handle, new IntPtr(unchecked((long)addr)), (UIntPtr)1, oldProtect, out oldProtect);
        }

        public ulong ReadMultiAddress(ulong ptr, List<ulong> offsets)
        {
            ulong buffer = ptr;
            for (int i = 0; i < offsets.Count; i++)
                buffer = Read<ulong>(buffer + offsets[i]);

            return buffer;
        }

        public T ReadMulti<T>(ulong baseAddr, List<ulong> offsets) where T : struct
        {
            ulong buffer = baseAddr;
            for (int i = 0; i < offsets.Count - 1; i++)
                buffer = Read<ulong>(buffer + offsets[i]);

            return Read<T>(buffer + offsets[offsets.Count - 1]);
        }

        private uint FindProcessIdByProcessName(string processName)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            PROCESSENTRY32W entry = new PROCESSENTRY32W();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));

            uint pid = 0;
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    if (entry.szExeFile == processName)
                    {
                        pid = entry.th32ProcessID;
                        break;
                    }
                } while (Process32NextW(snapshot, ref entry));
            }
            CloseHandle(snapshot);
            return pid;
        }

        private uint FindProcessIdByWindowName(string windowName)
        {
            uint processId = 0;
            IntPtr windowHandle = FindWindow(null, windowName);
            if (windowHandle != IntPtr.Zero)
                GetWindowThreadProcessId(windowHandle, out processId);
            return processId;
        }

        private IntPtr GetWindowHandleFromProcessId(uint processId)
        {
            IntPtr hwnd = IntPtr.Zero;
            do
            {
                hwnd = FindWindowEx(IntPtr.Zero, hwnd, null, null);
                uint pid = 0;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == processId)
                {
                    StringBuilder windowTitle = new StringBuilder(260);
                    GetWindowText(hwnd, windowTitle, 260);
                    if (IsWindowVisible(hwnd) && windowTitle.Length > 0)
                        return hwnd;
                }
            } while (hwnd != IntPtr.Zero);
            return IntPtr.Zero;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, IntPtr nSize, IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, uint cb);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MODULEINFO
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }

    public static class Offsets
    {
        public static long CvarUnknown = 0;

        public static class Signatures
        {
            public static readonly byte[] CvarUnknown = new byte[]
            {
                0x80, 0x78, 0x58, 0x00,
                0x0F, 0x85, 0x00, 0x00, 0x00, 0x00,
                0x33, 0xC9
            };
        }
    }

    public sealed class Engine
    {
        private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;

        private static readonly Engine instance = new Engine();

        private PProcess process;
        private ProcessModule client;

        private Engine() { }

        private static Engine GetInstance() { return instance; }

        public static bool Init() { return GetInstance().InitImpl(); }
        public static bool Run() { return GetInstance().RunImpl(); }
        public static ProcessModule GetClient() { return GetInstance().client; }
        public static PProcess GetProcess() { return GetInstance().process; }


        private bool InitImpl()
        {
            process = new PProcess();

            if (!AwaitProcess())
            {
                Console.WriteLine("[ERR] Could not find process, please make sure the game is open");
                return false;
            }

            if (!AwaitModules())
            {
                Console.WriteLine("[ERR] Game took too long to load, please open me again once its fully loaded");
                return false;
            }

            if (Offsets.CvarUnknown == 0)
            {
                ulong address = process.FindSignature(client, Offsets.Signatures.CvarUnknown, 0);
                if (address != 0)
                {
                    Offsets.CvarUnknown = (long)(address - client.Base) + 3;
                    Console.WriteLine("[DBG] Found offset by signature at base 0x{0:X}, cvar offset 0x{1:X}",
                        address - client.Base, Offsets.CvarUnknown);
                }
            }

            if (Offsets.CvarUnknown == 0)
            {
                Console.WriteLine("[ERR] Failed to find offset with provided signature, please update Offsets.hpp with the latest dump");
                return false;
            }

            // std::thread(&Engine::Thread, this).detach();
            Console.WriteLine("[DBG] Successfully initialized engine...");
            return true;
        }

        private bool RunImpl()
        {
            ulong patchAddr = client.Base + (ulong)Offsets.CvarUnknown;

            byte before = process.Read<byte>(patchAddr);

            if (before != 0 && before != 1)
            {
                Console.WriteLine("[WRN] Address returened an invalid value, the game probably updated");
                return false;
            }

#if DEBUG
            Console.WriteLine("[DBG] Before patching bytes is {0}", before);
#endif

            byte patch = before == 0 ? (byte)0x01 : (byte)0x00;

            process.WriteBytes(client.Base + (ulong)Offsets.CvarUnknown, new byte[] { patch });

            byte after = process.Read<byte>(patchAddr);

#if DEBUG
            Console.WriteLine("[DBG] After patching bytes is {0}", after);
#endif

            if (after != patch)
            {
                Console.WriteLine("[ERR] Failed to patch memory, expected value (0x{0:X}) actual value (0x{1:X})", patch, after);
                return false;
            }

            Console.WriteLine("[INF] Patch sucessfull, hack has been '{0}'! run me again to reverse the effect!", (after == 1 ? "enabled" : "disabled"));

            if (after == 1)
                Console.WriteLine("[INF] If it doesn't seem to work, open the in-game console and type 'spec_show_xray 1'");

            return true;
        }

        private void Thread()
        {
            while (true)
            {
                System.Threading.Thread.Sleep(1);
            }
        }

        private bool AwaitProcess()
        {
            if (process == null || process.Handle != IntPtr.Zero)
                return false;

            int attempts = 0;

            do
            {
                if (process.AttachProcess("cs2.exe", PROCESS_ALL_ACCESS))
                    break;

                if (process.Pid != 0 && process.Handle == IntPtr.Zero)
                {
                    Console.WriteLine("[ERR] Insufficient permissions to open a handle to the process. Try running as Administrator.");
                    return false;
                }

                if (attempts == 0)
                    Console.WriteLine("[DBG] Waiting 50s for the game to open...");

                if (attempts > 10)
                    return false;
                attempts++;

                System.Threading.Thread.Sleep(5000);
            } while (true);

            return true;
        }

        private bool AwaitModules()
        {
            if (process == null || process.Handle == IntPtr.Zero)
                return false;

            Console.WriteLine("[DBG] Waiting for the game to open...");

            int attempts = 0;

            do
            {
                client = process.GetModule("client.dll");

                if (client.Base != 0)
                    break;

                if (attempts > 10)
                    return false;
                attempts++;

                System.Threading.Thread.Sleep(5000);
            } while (true);

            return true;
        }
    }

    public static class Program
    {
        public static int Main()
        {
            Console.WriteLine("[INF] Compiled C# 5 (.NET Framework 4.8) port, Welcome to cs2-onebyte-hack!");

            if (!Engine.Init())
            {
                Console.WriteLine("[ERR] Engine failed to initialize, cannot continue execution");
            }
            else if (!Engine.Run())
            {
                Console.WriteLine("[ERR] Engine failed to patch game memory, im sorry, game updated");
            }

            Console.WriteLine("[DBG] Thats it, im done, closing in 5s!");
            System.Threading.Thread.Sleep(5000);
            return 0;
        }
    }
}