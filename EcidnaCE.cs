using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EcidnaOverlay
{
    // Configuration class
    // Data members are persisted in the JSON settings file.
    [DataContract]
    public class EcidnaConfig
    {
        [DataMember]
        public bool TeamFilterEnabled {get; set;}
        
        [DataMember]
        public bool DistanceEnabled {get; set;}
        
        [DataMember]
        public bool SnaplinesEnabled {get; set;}

        [DataMember]
        public uint DistanceTextColor {get; set;}
        
        [DataMember]
        public uint NameColor {get; set;}
        
        [DataMember]
        public uint BoxOutlineColor {get; set;}
        
        [DataMember]
        public uint HighHealthColor {get; set;}
        
        [DataMember]
        public uint MediumHealthColor {get; set;}
        
        [DataMember]
        public uint LowHealthColor {get; set;}
        
        [DataMember]
        public uint DefaultBoxColor {get; set;}

        [DataMember]
        public int FontSize {get; set;}
        
        // Provides the initial settings when no usable configuration is available.
        public static EcidnaConfig CreateDefault()
        {
            return new EcidnaConfig
            {
                TeamFilterEnabled = true,
                DistanceEnabled = true,
                SnaplinesEnabled = true,
                DistanceTextColor = 0x00FFFFFF,
                NameColor = 0x00462ECB,
                BoxOutlineColor = 0x00000000,
                
                HighHealthColor = 0x0000FF00,
                MediumHealthColor = 0x0000FFFF,
                LowHealthColor = 0x000000FF,
                DefaultBoxColor = 0x00D97093,
                
                FontSize = 14
            };
        }
    }

    internal static class EcidnaOffsets
    {
        public const uint EntityList = 0x270D5B0;
        public const uint ViewMatrix = 0x255D3D0;
        public const uint LocalPawn = 0x2557AB8;
        public const uint Health = 0x34C;
        public const uint Origin = 0x14A4;
        public const uint ControllerHandle = 0x14BC;
        public const uint SanitizedPlayerName = 0x878;
        public const uint TeamNum = 0x3E7;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Vec3
    {
        public float x;
        public float y;
        public float z;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Vec2
    {
        public float x;
        public float y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32
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
    internal struct MODULEENTRY32
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

    internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct WNDCLASSA
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string lpszClassName;
    }

    internal static class Program
    {
        private const int EntryStride = 0x70;
        private const int ChunksToScan = 4;
        private const int EntriesPerChunk = 512;

        private const uint PROCESS_VM_READ = 0x0010;
        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private const uint TH32CS_SNAPMODULE = 0x00000008;
        private const uint TH32CS_SNAPMODULE32 = 0x00000010;

        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_POPUP = 0x80000000;
        private const uint LWA_COLORKEY = 0x00000001;

        private const int SW_SHOW = 5;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        private const int PS_SOLID = 0;
        private const int NULL_BRUSH = 5;
        private const int BLACK_BRUSH = 4;
        private const int TRANSPARENT = 1;

        private const int VK_INSERT = 0x2D;
        private const int VK_END = 0x23;
        private const int VK_X = 0x58;
        private const uint PM_REMOVE = 0x0001;

        private static IntPtr TeaRoom = IntPtr.Zero;
        private static ulong clientBase;
        
        private static EcidnaConfig config;
        private static bool insertKeyWasDown = false;
        private static bool xKeyWasDown = false;
        private static bool distanceAndLinesEnabled = false;
        private const string ConfigFileName = "ecidna_config.json";

        // kernel32
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Module32First(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Module32Next(IntPtr hSnapshot, ref MODULEENTRY32 lpme);

        // user32
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr CreateWindowExA(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool PeekMessageA(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessageA(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcA(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern ushort RegisterClassA(ref WNDCLASSA lpWndClass);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // gdi32
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint crColor);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int fnObject);

        [DllImport("gdi32.dll")]
        private static extern bool Rectangle(IntPtr hdc, int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern bool TextOutW(IntPtr hdc, int x, int y, string lpString, int c);

        [DllImport("gdi32.dll")]
        private static extern bool SetBkMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        private static extern bool SetTextColor(IntPtr hdc, uint color);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetTextExtentPoint32W(IntPtr hdc, string lpString, int c, out SIZE lpSize);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFontW(int nHeight, int nWidth, int nEscapement, int nOrientation, 
            int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, 
            uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision, 
            uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);

        [DllImport("gdi32.dll")]
        private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lppt);

        [DllImport("gdi32.dll")]
        private static extern bool LineTo(IntPtr hdc, int xEnd, int yEnd);

        // dwmapi
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        // Generic memory read
        private static T Read<T>(ulong address) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] buffer = new byte[size];
            UIntPtr bytesRead;
            if (!ReadProcessMemory(TeaRoom, new IntPtr(unchecked((long)address)), buffer, new UIntPtr((uint)size), out bytesRead))
                return default(T);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        // Read bytes helper
        private static byte[] ReadBytes(ulong address, int size)
        {
            byte[] buffer = new byte[size];
            UIntPtr bytesRead;
            if (!ReadProcessMemory(TeaRoom, new IntPtr(unchecked((long)address)), buffer, new UIntPtr((uint)size), out bytesRead))
                return null;
            return buffer;
        }

        private static uint FindProcess(string processName)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                return 0;

            PROCESSENTRY32 processEntry = new PROCESSENTRY32();
            processEntry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
            uint pid = 0;

            if (Process32First(snapshot, ref processEntry))
            {
                do
                {
                    if (string.Equals(processEntry.szExeFile, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        pid = processEntry.th32ProcessID;
                        break;
                    }
                } while (Process32Next(snapshot, ref processEntry));
            }

            CloseHandle(snapshot);
            return pid;
        }

        private static ulong FindModule(uint pid, string dllName)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
                return 0;

            MODULEENTRY32 moduleEntry = new MODULEENTRY32();
            moduleEntry.dwSize = (uint)Marshal.SizeOf(typeof(MODULEENTRY32));
            ulong moduleBase = 0;

            if (Module32First(snapshot, ref moduleEntry))
            {
                do
                {
                    if (string.Equals(moduleEntry.szModule, dllName, StringComparison.OrdinalIgnoreCase))
                    {
                        moduleBase = (ulong)moduleEntry.modBaseAddr.ToInt64();
                        break;
                    }
                } while (Module32Next(snapshot, ref moduleEntry));
            }

            CloseHandle(snapshot);
            return moduleBase;
        }

        internal static bool KnownToPowerSCR(Vec3 knownW, ref Vec2 powerS, float[] matrix, int powerSW, int powerSH)
        {
            float w = matrix[12] * knownW.x + matrix[13] * knownW.y + matrix[14] * knownW.z + matrix[15];
            if (w < 0.1f)
                return false;

            float x = matrix[0] * knownW.x + matrix[1] * knownW.y + matrix[2] * knownW.z + matrix[3];
            float y = matrix[4] * knownW.x + matrix[5] * knownW.y + matrix[6] * knownW.z + matrix[7];

            powerS.x = (x / w + 1.0f) * (powerSW * 0.5f);
            powerS.y = (1.0f - y / w) * (powerSH * 0.5f);
            return true;
        }

        private static uint GetPerHealthColor(int health)
        {
            if (health >= 70)
                return config.HighHealthColor;
            else if (health >= 35)
                return config.MediumHealthColor;
            else
                return config.LowHealthColor;
        }

        private static uint GetBoxHealthColor(int health)
        {
            if (health >= 85)
                return config.DefaultBoxColor;
            else if (health >= 70)
                return config.HighHealthColor;
            else if (health >= 35)
                return config.MediumHealthColor;
            else
                return config.LowHealthColor;
        }

        private static float CalculateDistance(Vec3 pos1, Vec3 pos2)
        {
            float dx = pos1.x - pos2.x;
            float dy = pos1.y - pos2.y;
            float dz = pos1.z - pos2.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz)/10; // Convert to meters
        }

        private static int GetTeamNumber(ulong entityAddress)
        {
            try
            {
                int team = Read<int>(entityAddress + EcidnaOffsets.TeamNum);
                return team;
            }
            catch
            {
                return -1;
            }
        }

        private static int GetLocalTeam()
        {
            try
            {
                ulong localPawn = Read<ulong>(clientBase + EcidnaOffsets.LocalPawn);
                if (localPawn == 0)
                    return -1;
        
                int team = Read<int>(localPawn + EcidnaOffsets.TeamNum);
                return team;
            }
            catch
            {
                return -1;
            }
        }

        private static string GetPlayerName(ulong entityAddress, ulong entityList)
        {
            try
            {
                ulong controllerHandle = Read<ulong>(entityAddress + EcidnaOffsets.ControllerHandle);
                if (controllerHandle == 0)
                    return string.Empty;

                int index = (int)(controllerHandle & 0x7FFF);
                int chunk = index / EntriesPerChunk;
                int entry = index % EntriesPerChunk;

                ulong chunkBase = Read<ulong>(entityList + 0x10 + 8UL * (ulong)chunk);
                if (chunkBase == 0)
                    return string.Empty;

                ulong controllerEntity = Read<ulong>(chunkBase + (ulong)(entry * EntryStride));
                if (controllerEntity == 0)
                    return string.Empty;

                byte[] ptrBytes = ReadBytes(controllerEntity + EcidnaOffsets.SanitizedPlayerName, 8);
                if (ptrBytes == null)
                    return string.Empty;

                ulong namePtr = BitConverter.ToUInt64(ptrBytes, 0);
                if (namePtr == 0 || namePtr < 0x10000)
                    return string.Empty;

                byte[] nameBytes = ReadBytes(namePtr, 128);
                if (nameBytes == null)
                    return string.Empty;

                int nullIndex = Array.IndexOf(nameBytes, (byte)0);
                if (nullIndex == 0)
                    return string.Empty;

                byte[] trimmedBytes = (nullIndex >= 0) ? new byte[nullIndex] : nameBytes;
                if (nullIndex >= 0)
                {
                    Array.Copy(nameBytes, trimmedBytes, nullIndex);
                }
                else
                {
                    int endIndex = trimmedBytes.Length;
                    while (endIndex > 0 && trimmedBytes[endIndex - 1] == 0)
                        endIndex--;
                    if (endIndex != trimmedBytes.Length)
                    {
                        trimmedBytes = new byte[endIndex];
                        Array.Copy(nameBytes, trimmedBytes, endIndex);
                    }
                }

                string playerName = Encoding.UTF8.GetString(trimmedBytes);
                return string.IsNullOrEmpty(playerName) ? string.Empty : playerName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void DrawDistance(IntPtr hdc, float distance, int x, int y)
        {
            if (!distanceAndLinesEnabled || !config.DistanceEnabled)
                return;

            IntPtr font = CreateFontW(config.FontSize, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 2, 0, "Arial");
            IntPtr oldFont = SelectObject(hdc, font);

            SetBkMode(hdc, TRANSPARENT);
            string distanceText = string.Format("{0:F0}m", distance);

            SIZE textSize;
            GetTextExtentPoint32W(hdc, distanceText, distanceText.Length, out textSize);

            int textX = x - (textSize.cx / 2);

            SetTextColor(hdc, 0x00000000);
            TextOutW(hdc, textX + 1, y + 1, distanceText, distanceText.Length);

            SetTextColor(hdc, config.DistanceTextColor);
            TextOutW(hdc, textX, y, distanceText, distanceText.Length);

            SelectObject(hdc, oldFont);
            DeleteObject(font);
        }

        private static void DrawSnapline(IntPtr hdc, int health, int startX, int startY, int endX, int endY)
        {
            if (!distanceAndLinesEnabled || !config.SnaplinesEnabled)
                return;

            uint color = GetBoxHealthColor(health);
            IntPtr pen = CreatePen(PS_SOLID, 1, color);
            IntPtr oldPen = SelectObject(hdc, pen);

            MoveToEx(hdc, startX, startY, IntPtr.Zero);
            LineTo(hdc, endX, endY);

            SelectObject(hdc, oldPen);
            DeleteObject(pen);
        }

        private static void DrawPlayerInfo(IntPtr hdc, string name, int health, int centerX, int topY)
        {
            if (string.IsNullOrEmpty(name))
                return;

            IntPtr font = CreateFontW(config.FontSize, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 2, 0, "Arial");
            IntPtr oldFont = SelectObject(hdc, font);

            SetBkMode(hdc, TRANSPARENT);

            string hpText = string.Format("{0}%", health);
            string splitText = ">";

            SIZE hpSize;
            GetTextExtentPoint32W(hdc, hpText, hpText.Length, out hpSize);

            SIZE splitSize;
            GetTextExtentPoint32W(hdc, splitText, splitText.Length, out splitSize);

            SIZE nameSize;
            GetTextExtentPoint32W(hdc, name, name.Length, out nameSize);

            const int segmentGap = 4;
            int totalWidth = hpSize.cx + segmentGap + splitSize.cx + segmentGap + nameSize.cx;
            int textX = centerX - (totalWidth / 2);
            int textY = topY - hpSize.cy - 2;

            SetTextColor(hdc, 0x00000000);
            TextOutW(hdc, textX + 1, textY + 1, hpText, hpText.Length);

            uint hpColor = GetPerHealthColor(health);
            SetTextColor(hdc, hpColor);
            TextOutW(hdc, textX, textY, hpText, hpText.Length);

            int splitX = textX + hpSize.cx + segmentGap;
            SetTextColor(hdc, 0x00000000);
            TextOutW(hdc, splitX + 1, textY + 1, splitText, splitText.Length);

            SetTextColor(hdc, 0x00C4C4C4);
            TextOutW(hdc, splitX, textY, splitText, splitText.Length);

            int nameX = splitX + splitSize.cx + segmentGap;
            SetTextColor(hdc, 0x00000000);
            TextOutW(hdc, nameX + 1, textY + 1, name, name.Length);

            SetTextColor(hdc, config.NameColor);
            TextOutW(hdc, nameX, textY, name, name.Length);

            SelectObject(hdc, oldFont);
            DeleteObject(font);
        }

        private static void DrawColoredB_and_P(IntPtr hdc, int health, int left, int top, int right, int bottom)
        {
            uint per_color = GetPerHealthColor(health);
            uint box_color = GetBoxHealthColor(health);
            
            IntPtr outerPen = CreatePen(PS_SOLID, 3, config.BoxOutlineColor);
            IntPtr innerPenP = CreatePen(PS_SOLID, 1, per_color);
            IntPtr innerPenB = CreatePen(PS_SOLID, 1, box_color);
            
            SelectObject(hdc, GetStockObject(NULL_BRUSH));
            SelectObject(hdc, outerPen);
            Rectangle(hdc, left - 1, top - 1, right + 1, bottom + 1);

            SelectObject(hdc, innerPenP);
            Rectangle(hdc, left, top, right, bottom);
            
            SelectObject(hdc, innerPenB);
            Rectangle(hdc, left, top, right, bottom);
            
            DeleteObject(outerPen);
            DeleteObject(innerPenP);
            DeleteObject(innerPenB);
        }

        private static EcidnaConfig LoadConfig()
        {
            try
            {
                // Load saved settings when the file exists; otherwise use the defaults below.
                if (File.Exists(ConfigFileName))
                {
                    using (FileStream fs = new FileStream(ConfigFileName, FileMode.Open, FileAccess.Read))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(EcidnaConfig));
                        EcidnaConfig loadedConfig = serializer.ReadObject(fs) as EcidnaConfig;
                        if (loadedConfig != null)
                        {
                            return loadedConfig;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Format("Failed to load config: {0}", ex.Message));
                Console.WriteLine("Using default settings.");
            }
            
            return EcidnaConfig.CreateDefault();
        }

        private static void SaveConfig()
        {
            try
            {
                // Serialize to UTF-8 JSON before writing the complete settings file.
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(EcidnaConfig));
                    
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, true, true, "  "))
                    {
                        serializer.WriteObject(writer, config);
                        writer.Flush();
                        
                        string jsonText = Encoding.UTF8.GetString(ms.ToArray());
                        File.WriteAllText(ConfigFileName, jsonText, Encoding.UTF8);
                    }
                }
                
                Console.WriteLine("Config saved successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Format("Failed to save config: {0}", ex.Message));
            }
        }

        private static void HandleKeyInputs()
        {
            bool insertKeyIsDown = (GetAsyncKeyState(VK_INSERT) & 0x8000) != 0;
            if (insertKeyIsDown && !insertKeyWasDown)
            {
                config.TeamFilterEnabled = !config.TeamFilterEnabled;
                SaveConfig();
                Console.WriteLine(String.Format("Team Filter: {0}", config.TeamFilterEnabled ? "ON" : "OFF"));
            }
            insertKeyWasDown = insertKeyIsDown;

            bool xKeyIsDown = (GetAsyncKeyState(VK_X) & 0x8000) != 0;
            if (xKeyIsDown && !xKeyWasDown)
            {
                distanceAndLinesEnabled = !distanceAndLinesEnabled;
                config.DistanceEnabled = distanceAndLinesEnabled;
                config.SnaplinesEnabled = distanceAndLinesEnabled;
                SaveConfig();
                Console.WriteLine(String.Format("Distance & Snaplines: {0}", distanceAndLinesEnabled ? "ON" : "OFF"));
            }
            xKeyWasDown = xKeyIsDown;

        }

        private static int Main()
        {
            config = LoadConfig();
            
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("ЕхиДна ВХ ЧИТ - Версия. 1.2");
            Console.WriteLine(String.Format("Config loaded from {0}", ConfigFileName));
            Console.ResetColor();

            uint pid = 0;
            while (pid == 0)
            {
                pid = FindProcess("cs2.exe");
                Thread.Sleep(500);
            }

            clientBase = 0;
            while (clientBase == 0)
            {
                clientBase = FindModule(pid, "client.dll");
                Thread.Sleep(500);
            }

            TeaRoom = OpenProcess(PROCESS_VM_READ, false, pid);
            if (TeaRoom == IntPtr.Zero)
                return 0;

            WNDCLASSA wc = new WNDCLASSA();
            wc.lpfnWndProc = DefWindowProcA;
            wc.hInstance = GetModuleHandle(null);
            wc.lpszClassName = "DnaOverlayTea";
            RegisterClassA(ref wc);

            int powerSW = GetSystemMetrics(SM_CXSCREEN);
            int powerSH = GetSystemMetrics(SM_CYSCREEN);

            IntPtr dot = CreateWindowExA(
                WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT,
                "DnaOverlayTea", null, WS_POPUP, 0, 0, powerSW, powerSH,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            SetLayeredWindowAttributes(dot, 0x00000000, 0, LWA_COLORKEY);

            MARGINS cglass = new MARGINS();
            cglass.cxLeftWidth = -1;
            cglass.cxRightWidth = -1;
            cglass.cyTopHeight = -1;
            cglass.cyBottomHeight = -1;
            DwmExtendFrameIntoClientArea(dot, ref cglass);
            ShowWindow(dot, SW_SHOW);

            while (GetAsyncKeyState(VK_END) == 0)
            {
                HandleKeyInputs();

                DwmFlush();

                IntPtr hdc = GetDC(dot);
                RECT fully = new RECT();
                fully.Left = 0;
                fully.Top = 0;
                fully.Right = powerSW;
                fully.Bottom = powerSH;
                FillRect(hdc, ref fully, GetStockObject(BLACK_BRUSH));

                byte[] matrixBytes = new byte[64];
                UIntPtr bytesRead;
                ReadProcessMemory(TeaRoom, new IntPtr(unchecked((long)(clientBase + EcidnaOffsets.ViewMatrix))), matrixBytes, new UIntPtr(64), out bytesRead);
                float[] matrix = new float[16];
                Buffer.BlockCopy(matrixBytes, 0, matrix, 0, 64);

                int localTeam = GetLocalTeam();
                ulong localPawn = Read<ulong>(clientBase + EcidnaOffsets.LocalPawn);
                ulong entityList = Read<ulong>(clientBase + EcidnaOffsets.EntityList);
                
                Vec3 localPos = new Vec3();
                if (localPawn != 0)
                {
                    localPos = Read<Vec3>(localPawn + EcidnaOffsets.Origin);
                }

                byte[] slots = new byte[EntriesPerChunk * EntryStride];
                for (int chunk = 0; chunk < ChunksToScan; chunk++)
                {
                    ulong chunkBase = Read<ulong>(entityList + 0x10 + 8UL * (ulong)chunk);
                    if (chunkBase == 0)
                        continue;

                    if (!ReadProcessMemory(TeaRoom, new IntPtr(unchecked((long)chunkBase)), slots, new UIntPtr((uint)slots.Length), out bytesRead))
                        continue;
                    if (bytesRead.ToUInt64() != (ulong)slots.Length)
                        continue;

                    for (int i = 0; i < EntriesPerChunk; i++)
                    {
                        ulong entity = BitConverter.ToUInt64(slots, i * EntryStride);
                        if (entity == 0 || entity == localPawn)
                            continue;

                        int entityTeam = GetTeamNumber(entity);
                        if (config.TeamFilterEnabled && localTeam != -1 && entityTeam == localTeam)
                            continue;

                        int health = Read<int>(entity + EcidnaOffsets.Health);
                        if (health < 1 || health > 100)
                            continue;

                        Vec3 origin = Read<Vec3>(entity + EcidnaOffsets.Origin);
                        if (float.IsNaN(origin.x) || float.IsNaN(origin.y) || float.IsNaN(origin.z) ||
                            float.IsInfinity(origin.x) || float.IsInfinity(origin.y) || float.IsInfinity(origin.z))
                            continue;
                        if (Math.Abs(origin.x) > 30000 || Math.Abs(origin.y) > 30000)
                            continue;

                        Vec3 headPos = new Vec3();
                        headPos.x = origin.x;
                        headPos.y = origin.y;
                        headPos.z = origin.z + 72;

                        Vec2 feet2d = new Vec2();
                        Vec2 head2d = new Vec2();
                        if (!KnownToPowerSCR(origin, ref feet2d, matrix, powerSW, powerSH))
                            continue;
                        if (!KnownToPowerSCR(headPos, ref head2d, matrix, powerSW, powerSH))
                            continue;

                        float boxHeight = feet2d.y - head2d.y;
                        if (boxHeight <= 0)
                            continue;

                        float boxWidth = boxHeight / 2;
                        int left = (int)(feet2d.x - boxWidth / 2);
                        int top = (int)head2d.y;
                        int right = (int)(feet2d.x + boxWidth / 2);
                        int bottom = (int)feet2d.y;

                        int screenBottomCenter = powerSW / 2;
                        // top snapline
                        // DrawSnapline(hdc, health, screenBottomCenter, powerSH, (left + right) / 2, head2d.y >= 0 ? (int)head2d.y : top);
                        
                        // bottom snapline
                        DrawSnapline(hdc, health, screenBottomCenter, powerSH, (left + right) / 2, bottom);

                        float distance = CalculateDistance(localPos, origin);
                        DrawDistance(hdc, distance, (left + right) / 2, bottom + 5);

                        DrawColoredB_and_P(hdc, health, left, top, right, bottom);

                        string playerName = GetPlayerName(entity, entityList);
                        if (!string.IsNullOrEmpty(playerName))
                        {
                            int boxCenterX = (left + right) / 2;
                            DrawPlayerInfo(hdc, playerName, health, boxCenterX, top);
                        }
                    }
                }

                ReleaseDC(dot, hdc);

                MSG msg;
                while (PeekMessageA(out msg, dot, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageA(ref msg);
                }
            }

            SaveConfig();
            CloseHandle(TeaRoom);
            return 0;
        }
    }
}